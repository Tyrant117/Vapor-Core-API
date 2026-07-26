using System.Linq;
using System.Reflection;
using Mono.Cecil;

namespace Vapor.CodeGen
{
    /// <summary>
    /// The IL post-processing runner is a .NET Core process, so reflection-based imports
    /// (module.ImportReference(typeof(...))) would bake a System.Private.CoreLib assembly reference
    /// into the woven player assembly, which links mscorlib/netstandard and would fail to load.
    /// Rewrites any such reference to whichever corlib facade the module already references.
    /// </summary>
    internal sealed class PostProcessorReflectionImporter : DefaultReflectionImporter
    {
        private const string k_SystemPrivateCoreLib = "System.Private.CoreLib";
        private readonly AssemblyNameReference m_CorrectCorlib;

        public PostProcessorReflectionImporter(ModuleDefinition module) : base(module)
        {
            m_CorrectCorlib = module.AssemblyReferences.FirstOrDefault(a => a.Name == "mscorlib" || a.Name == "netstandard" || a.Name == k_SystemPrivateCoreLib);
        }

        public override AssemblyNameReference ImportReference(AssemblyName reference)
        {
            return m_CorrectCorlib != null && reference.Name == k_SystemPrivateCoreLib ? m_CorrectCorlib : base.ImportReference(reference);
        }
    }

    internal sealed class PostProcessorReflectionImporterProvider : IReflectionImporterProvider
    {
        public IReflectionImporter GetReflectionImporter(ModuleDefinition module) => new PostProcessorReflectionImporter(module);
    }
}
