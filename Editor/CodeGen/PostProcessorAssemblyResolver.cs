using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Mono.Cecil;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Vapor.CodeGen
{
    /// <summary>
    /// Resolves assembly references for the in-memory assembly being post-processed. Modeled on
    /// Unity.Netcode.Editor.CodeGen.PostProcessorAssemblyResolver: files are slurped into memory so
    /// Unity's dlls are never locked, and the assembly being operated on resolves to itself.
    /// </summary>
    internal sealed class PostProcessorAssemblyResolver : IAssemblyResolver
    {
        private readonly string[] m_AssemblyReferences;
        private readonly Dictionary<string, AssemblyDefinition> m_AssemblyCache = new();
        private readonly ICompiledAssembly m_CompiledAssembly;
        private AssemblyDefinition m_SelfAssembly;

        public PostProcessorAssemblyResolver(ICompiledAssembly compiledAssembly)
        {
            m_CompiledAssembly = compiledAssembly;
            m_AssemblyReferences = compiledAssembly.References;
        }

        public void Dispose() { }

        public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters(ReadingMode.Deferred));

        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            lock (m_AssemblyCache)
            {
                if (name.Name == m_CompiledAssembly.Name)
                {
                    return m_SelfAssembly;
                }

                var fileName = FindFile(name);
                if (fileName == null)
                {
                    return null;
                }

                var lastWriteTime = File.GetLastWriteTime(fileName);
                var cacheKey = $"{fileName}{lastWriteTime}";
                if (m_AssemblyCache.TryGetValue(cacheKey, out var result))
                {
                    return result;
                }

                parameters.AssemblyResolver = this;

                var ms = MemoryStreamFor(fileName);
                var pdb = Path.ChangeExtension(fileName, ".pdb");
                if (File.Exists(pdb))
                {
                    parameters.SymbolStream = MemoryStreamFor(pdb);
                }

                var assemblyDefinition = AssemblyDefinition.ReadAssembly(ms, parameters);
                m_AssemblyCache.Add(cacheKey, assemblyDefinition);
                return assemblyDefinition;
            }
        }

        private string FindFile(AssemblyNameReference name)
        {
            var fileName = m_AssemblyReferences.FirstOrDefault(r => Path.GetFileName(r) == $"{name.Name}.dll");
            if (fileName != null)
            {
                return fileName;
            }

            fileName = m_AssemblyReferences.FirstOrDefault(r => Path.GetFileName(r) == $"{name.Name}.exe");
            if (fileName != null)
            {
                return fileName;
            }

            // ICompiledAssembly.References only lists direct references, but Cecil resolution can reach
            // indirect ones — probe every directory a direct reference lives in.
            foreach (var parentDir in m_AssemblyReferences.Select(Path.GetDirectoryName).Distinct())
            {
                var candidate = Path.Combine(parentDir!, $"{name.Name}.dll");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static MemoryStream MemoryStreamFor(string fileName)
        {
            return Retry(10, TimeSpan.FromSeconds(1), () =>
            {
                byte[] byteArray;
                using (var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byteArray = new byte[fileStream.Length];
                    var readLength = fileStream.Read(byteArray, 0, (int)fileStream.Length);
                    if (readLength != fileStream.Length)
                    {
                        throw new InvalidOperationException("File read length is not full length of file.");
                    }
                }

                return new MemoryStream(byteArray);
            });
        }

        private static MemoryStream Retry(int retryCount, TimeSpan waitTime, Func<MemoryStream> func)
        {
            try
            {
                return func();
            }
            catch (IOException)
            {
                if (retryCount == 0)
                {
                    throw;
                }

                Console.WriteLine($"Caught IO Exception, trying {retryCount} more times");
                Thread.Sleep(waitTime);
                return Retry(retryCount - 1, waitTime, func);
            }
        }

        /// <summary>
        /// Types inside the assembly currently being woven can only resolve if the resolver knows to
        /// answer with the in-memory definition instead of probing the (stale or missing) file on disk.
        /// </summary>
        public void AddAssemblyDefinitionBeingOperatedOn(AssemblyDefinition assemblyDefinition)
        {
            m_SelfAssembly = assemblyDefinition;
        }
    }
}
