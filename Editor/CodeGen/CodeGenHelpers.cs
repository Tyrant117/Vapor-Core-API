using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace Vapor.CodeGen
{
    internal static class CodeGenHelpers
    {
        public static AssemblyDefinition AssemblyDefinitionFor(ICompiledAssembly compiledAssembly, out PostProcessorAssemblyResolver assemblyResolver)
        {
            assemblyResolver = new PostProcessorAssemblyResolver(compiledAssembly);
            var readerParameters = new ReaderParameters
            {
                SymbolStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData),
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                AssemblyResolver = assemblyResolver,
                ReflectionImporterProvider = new PostProcessorReflectionImporterProvider(),
                ReadingMode = ReadingMode.Immediate
            };

            var assemblyDefinition = AssemblyDefinition.ReadAssembly(new MemoryStream(compiledAssembly.InMemoryAssembly.PeData), readerParameters);

            // Resolving a type that lives inside the assembly being processed must answer with the
            // in-memory definition, or resolution fails when weaving the runtime assembly itself.
            assemblyResolver.AddAssemblyDefinitionBeingOperatedOn(assemblyDefinition);

            return assemblyDefinition;
        }

        /// <summary>
        /// Finds a module (e.g. "vapor.core.runtime.dll") in the assembly being woven or anywhere in its
        /// transitive reference closure.
        /// </summary>
        public static ModuleDefinition FindModule(AssemblyDefinition assemblyDefinition, IAssemblyResolver assemblyResolver, string moduleFileName)
        {
            foreach (var module in assemblyDefinition.Modules)
            {
                if (module.Name == moduleFileName)
                {
                    return module;
                }
            }

            var visited = new HashSet<string>();
            return FindModuleRecursive(assemblyDefinition, assemblyResolver, moduleFileName, visited);
        }

        private static ModuleDefinition FindModuleRecursive(AssemblyDefinition assemblyDefinition, IAssemblyResolver assemblyResolver, string moduleFileName, HashSet<string> visited)
        {
            var resolvedReferences = new List<AssemblyDefinition>();
            foreach (var reference in assemblyDefinition.MainModule.AssemblyReferences)
            {
                if (!visited.Add(reference.Name))
                {
                    continue;
                }

                AssemblyDefinition resolved;
                try
                {
                    resolved = assemblyResolver.Resolve(reference);
                }
                catch (Exception)
                {
                    continue;
                }

                if (resolved == null)
                {
                    continue;
                }

                foreach (var module in resolved.Modules)
                {
                    if (module.Name == moduleFileName)
                    {
                        return module;
                    }
                }

                resolvedReferences.Add(resolved);
            }

            foreach (var resolved in resolvedReferences)
            {
                var found = FindModuleRecursive(resolved, assemblyResolver, moduleFileName, visited);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        public static bool IsSubclassOf(this TypeDefinition typeDefinition, string classTypeFullName)
        {
            if (!typeDefinition.IsClass)
            {
                return false;
            }

            var baseTypeRef = typeDefinition.BaseType;
            while (baseTypeRef != null)
            {
                if (baseTypeRef.FullName == classTypeFullName)
                {
                    return true;
                }

                try
                {
                    baseTypeRef = baseTypeRef.Resolve()?.BaseType;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }

        public static bool IsSubclassOfOrEqual(this TypeReference typeReference, string classTypeFullName)
        {
            if (typeReference.FullName == classTypeFullName)
            {
                return true;
            }

            TypeDefinition typeDefinition;
            try
            {
                typeDefinition = typeReference.Resolve();
            }
            catch (Exception)
            {
                return false;
            }

            return typeDefinition != null && typeDefinition.IsSubclassOf(classTypeFullName);
        }

        /// <summary>
        /// True when the type is the given interface, inherits it through another interface, or
        /// implements it anywhere in its class hierarchy.
        /// </summary>
        public static bool ImplementsOrIsInterface(this TypeReference typeReference, string interfaceFullName)
        {
            if (typeReference.FullName == interfaceFullName)
            {
                return true;
            }

            TypeDefinition typeDefinition;
            try
            {
                typeDefinition = typeReference.Resolve();
            }
            catch (Exception)
            {
                return false;
            }

            while (typeDefinition != null)
            {
                foreach (var implementation in typeDefinition.Interfaces)
                {
                    if (implementation.InterfaceType.ImplementsOrIsInterface(interfaceFullName))
                    {
                        return true;
                    }
                }

                try
                {
                    typeDefinition = typeDefinition.BaseType?.Resolve();
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }

        public static bool HasParameterlessConstructor(this TypeReference typeReference)
        {
            if (typeReference.IsValueType)
            {
                return true;
            }

            TypeDefinition typeDefinition;
            try
            {
                typeDefinition = typeReference.Resolve();
            }
            catch (Exception)
            {
                return false;
            }

            return typeDefinition != null && typeDefinition.Methods.Any(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0);
        }

        /// <summary>
        /// Importing closed generics of methods defined in the module being woven makes Cecil add the
        /// module as a reference to itself, which breaks Unity's test discovery. Strip any such self-reference.
        /// </summary>
        public static void RemoveRecursiveReferences(this ModuleDefinition moduleDefinition)
        {
            AssemblyNameReference selfReference = null;
            foreach (var reference in moduleDefinition.AssemblyReferences)
            {
                if (reference.Name == moduleDefinition.Assembly.Name.Name)
                {
                    selfReference = reference;
                    break;
                }
            }

            if (selfReference != null)
            {
                moduleDefinition.AssemblyReferences.Remove(selfReference);
            }
        }

        #region - Diagnostics -

        public static void AddError(this List<DiagnosticMessage> diagnostics, string message)
        {
            diagnostics.AddError((SequencePoint)null, message);
        }

        public static void AddError(this List<DiagnosticMessage> diagnostics, MethodDefinition methodDefinition, string message)
        {
            diagnostics.AddError(methodDefinition.DebugInformation.SequencePoints.FirstOrDefault(), message);
        }

        public static void AddError(this List<DiagnosticMessage> diagnostics, SequencePoint sequencePoint, string message)
        {
            diagnostics.Add(new DiagnosticMessage
            {
                DiagnosticType = DiagnosticType.Error,
                File = sequencePoint?.Document.Url.Replace($"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}", ""),
                Line = sequencePoint?.StartLine ?? 0,
                Column = sequencePoint?.StartColumn ?? 0,
                MessageData = $" - {message}"
            });
        }

        public static void AddWarning(this List<DiagnosticMessage> diagnostics, MethodDefinition methodDefinition, string message)
        {
            var sequencePoint = methodDefinition.DebugInformation.SequencePoints.FirstOrDefault();
            diagnostics.Add(new DiagnosticMessage
            {
                DiagnosticType = DiagnosticType.Warning,
                File = sequencePoint?.Document.Url.Replace($"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}", ""),
                Line = sequencePoint?.StartLine ?? 0,
                Column = sequencePoint?.StartColumn ?? 0,
                MessageData = $" - {message}"
            });
        }

        #endregion

        #region - Hashing -

        /// <summary>
        /// xxHash32 (seed 0) over "{module name} / {method full name}", mirroring both
        /// Vapor.Unsafe.XxHash's algorithm and Unity Netcode's rpc-id scheme. The value is baked into
        /// the woven send prologue, the handler name, and the registration call — the runtime never
        /// recomputes it, so all that matters is determinism.
        /// </summary>
        public static uint Hash(this MethodDefinition methodDefinition)
        {
            return Hash32(Encoding.UTF8.GetBytes($"{methodDefinition.Module.Name} / {methodDefinition.FullName}"));
        }

        private static uint Hash32(byte[] buffer)
        {
            const uint prime1 = 2654435761u;
            const uint prime2 = 2246822519u;
            const uint prime3 = 3266489917u;
            const uint prime4 = 0668265263u;
            const uint prime5 = 0374761393u;

            unchecked
            {
                int length = buffer.Length;
                int index = 0;

                uint hash = prime5;

                if (length >= 16)
                {
                    uint val0 = prime1 + prime2;
                    uint val1 = prime2;
                    uint val2 = 0;
                    uint val3 = (uint)-(int)prime1;

                    int count = length >> 4;
                    for (int i = 0; i < count; i++)
                    {
                        val0 += ReadUInt32(buffer, index + 0) * prime2;
                        val0 = Rol(val0, 13);
                        val0 *= prime1;

                        val1 += ReadUInt32(buffer, index + 4) * prime2;
                        val1 = Rol(val1, 13);
                        val1 *= prime1;

                        val2 += ReadUInt32(buffer, index + 8) * prime2;
                        val2 = Rol(val2, 13);
                        val2 *= prime1;

                        val3 += ReadUInt32(buffer, index + 12) * prime2;
                        val3 = Rol(val3, 13);
                        val3 *= prime1;

                        index += 16;
                    }

                    hash = Rol(val0, 1) + Rol(val1, 7) + Rol(val2, 12) + Rol(val3, 18);
                }

                hash += (uint)length;

                while (length - index >= 4)
                {
                    hash += ReadUInt32(buffer, index) * prime3;
                    hash = Rol(hash, 17) * prime4;
                    index += 4;
                }

                while (index < length)
                {
                    hash += buffer[index] * prime5;
                    hash = Rol(hash, 11) * prime1;
                    index++;
                }

                hash ^= hash >> 15;
                hash *= prime2;
                hash ^= hash >> 13;
                hash *= prime3;
                hash ^= hash >> 16;

                return hash;
            }
        }

        private static uint Rol(uint value, int count) => (value << count) | (value >> (32 - count));

        private static uint ReadUInt32(byte[] buffer, int index) =>
            buffer[index] | ((uint)buffer[index + 1] << 8) | ((uint)buffer[index + 2] << 16) | ((uint)buffer[index + 3] << 24);

        #endregion
    }
}
