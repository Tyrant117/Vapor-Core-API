using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Vapor.Rpc.SourceGenerator.Tests
{
    internal static class Program
    {
        private static int s_Failures;

        private static int Main(string[] args)
        {
            AcceptsEveryParameterShape();
            MirrorsTheAuthorsModifiers();
            HandlesNamespacesAndNesting();
            FallsBackWhenAParameterTakesAGeneratedName();
            CompilesUnderUnityEditor();
            RegistersSerializationForValueArgumentsOnly();
            MatchesCecilNaming();

            RejectsNonPartialMethod();
            RejectsNonPartialType();
            RejectsNonNetworkObject();
            RejectsBadName();
            RejectsByRefParameter();
            RejectsStaticAndNonVoid();
            RejectsSpecifiedInParams();
            WarnsOnUnserializableManagedParameter();
            RejectsSerializableWithoutConstructor();
            RejectsAlreadyImplementedPartial();

            if (args.Contains("--print"))
            {
                PrintGeneratedSource();
            }

            Console.WriteLine();
            Console.WriteLine(s_Failures == 0 ? "All generator tests passed." : $"{s_Failures} generator test(s) FAILED.");
            return s_Failures == 0 ? 0 : 1;
        }

        #region Cases that must generate

        /// <summary>
        /// One rpc per serializer branch, which is also the check that the branches are tried in the
        /// right order — VaporNetworkObject and the INetworkSerializable struct both also satisfy a
        /// later case.
        /// </summary>
        private static void AcceptsEveryParameterShape()
        {
            const string source = @"
using Unity.Netcode;
using Vapor.NetworkObjects;

namespace Game
{
    public enum Team { Red, Blue }

    public struct Damage : INetworkSerializable { }

    public struct Coords : INetworkPacket { }

    public interface IEvent : INetworkPacket { }

    public class Pawn : VaporNetworkObject { }

    public class Weapon : NetworkBehaviour { }

    public partial class Combat : VaporNetworkObject
    {
        [VaporRpc(SendTo.Server)]
        private partial void PrimitivesRpc(int amount, string label, bool flag, Team team, float[] spread);

        [VaporRpc(SendTo.Owner, NetworkDelivery.Unreliable)]
        private partial void PacketRpc(IEvent payload, Coords where, INetworkPacket anything);

        [VaporRpc(SendTo.Everyone)]
        private partial void ObjectRpc(Pawn pawn, VaporNetworkObject any, Weapon weapon, NetworkBehaviour behaviour);

        [VaporRpc(SendTo.NotOwner)]
        private partial void SerializableRpc(Damage damage);

        [VaporRpc(SendTo.Server)]
        private partial void NoArgumentsRpc();

        private partial void PrimitivesRpc(int amount, string label, bool flag, Team team, float[] spread) { }
        private partial void PacketRpc(IEvent payload, Coords where, INetworkPacket anything) { }
        private partial void ObjectRpc(Pawn pawn, VaporNetworkObject any, Weapon weapon, NetworkBehaviour behaviour) { }
        private partial void SerializableRpc(Damage damage) { }
        private partial void NoArgumentsRpc() { }
    }
}";

            // The '_Implementation' bodies are what an author writes; the generator supplies the
            // declaring halves. Renaming them here mirrors that split.
            var result = Run(Split(source));
            Expect("every parameter shape", result);

            var generated = result.Generated.FirstOrDefault().Value ?? string.Empty;
            ExpectContains("every parameter shape", generated, "RpcSerialization.WriteValue<int>");
            ExpectContains("every parameter shape", generated, "RpcSerialization.WritePacket");
            ExpectContains("every parameter shape", generated, "RpcSerialization.WriteNetworkObject");
            ExpectContains("every parameter shape", generated, "RpcSerialization.WriteNetworkBehaviour");
            ExpectContains("every parameter shape", generated, "RpcSerialization.WriteSerializable<global::Game.Damage>");
            ExpectContains("every parameter shape", generated, "global::Unity.Netcode.SendTo.Owner");
            ExpectContains("every parameter shape", generated, "global::Unity.Netcode.NetworkDelivery.Unreliable");
            ExpectContains("every parameter shape", generated, "global::Unity.Netcode.NetworkDelivery.ReliableSequenced");

            // A parameter typed as the base itself needs no cast; a subclass does.
            ExpectContains("every parameter shape", generated, "(global::Game.Pawn)global::Vapor.NetworkObjects.RpcSerialization.ReadNetworkObject");
            ExpectMissing("every parameter shape", generated, "(global::Vapor.NetworkObjects.VaporNetworkObject)global::Vapor.NetworkObjects.RpcSerialization.ReadNetworkObject");
        }

        private static void MirrorsTheAuthorsModifiers()
        {
            const string source = @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public partial class Visible : VaporNetworkObject
{
    [VaporRpc(SendTo.Everyone)]
    public partial void ShoutRpc(int volume);

    [VaporRpc(SendTo.Server)]
    protected internal partial void MutterRpc();

    public partial void ShoutRpc(int volume) { }
    protected internal partial void MutterRpc() { }
}";

            var result = Run(Split(source));
            Expect("modifier mirroring", result);

            var generated = result.Generated.FirstOrDefault().Value ?? string.Empty;
            ExpectContains("modifier mirroring", generated, "public partial void ShoutRpc(");
            ExpectContains("modifier mirroring", generated, "protected internal partial void MutterRpc(");

            // The body half is always private, which is what forces the author to supply it.
            ExpectContains("modifier mirroring", generated, "private partial void ShoutRpc_Implementation(");
        }

        /// <summary>
        /// A private nested type is unreachable from a per-assembly registrar, which is why
        /// registration is emitted into the type itself. This is that guarantee.
        /// </summary>
        private static void HandlesNamespacesAndNesting()
        {
            const string source = @"
using Unity.Netcode;
using Vapor.NetworkObjects;

namespace Deep.Down
{
    public partial class Outer
    {
        private partial class Inner : VaporNetworkObject
        {
            [VaporRpc(SendTo.Server)]
            private partial void PokeRpc(int which);

            private partial void PokeRpc(int which) { }
        }
    }
}";

            var result = Run(Split(source));
            Expect("nesting", result);

            var generated = result.Generated.FirstOrDefault().Value ?? string.Empty;
            ExpectContains("nesting", generated, "namespace Deep.Down");
            ExpectContains("nesting", generated, "partial class Outer");
            ExpectContains("nesting", generated, "partial class Inner");
        }

        /// <summary>
        /// Arguments keep their own names in the receive handler for readability, so the one case that
        /// has to give — a parameter already called what the handler calls its own — is pinned here.
        /// </summary>
        private static void FallsBackWhenAParameterTakesAGeneratedName()
        {
            const string source = @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public partial class Awkward : VaporNetworkObject
{
    [VaporRpc(SendTo.Server)]
    private partial void CollideRpc(int rpcTarget, int rpcReader, int rpcWriter);

    private partial void CollideRpc(int rpcTarget, int rpcReader, int rpcWriter) { }
}";

            var result = Run(Split(source));
            Expect("generated-name collision", result);

            var generated = result.Generated.FirstOrDefault().Value ?? string.Empty;
            ExpectContains("generated-name collision", generated, "out var rpcWriter_");
            ExpectContains("generated-name collision", generated, "var arg0 =");
        }

        private static void CompilesUnderUnityEditor()
        {
            const string source = @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public partial class Editable : VaporNetworkObject
{
    [VaporRpc(SendTo.Server)]
    private partial void EditRpc(int value);

    private partial void EditRpc(int value) { }
}";

            var result = Run(Split(source), "UNITY_EDITOR");
            Expect("UNITY_EDITOR build", result);
            ExpectContains("UNITY_EDITOR build", result.Generated.FirstOrDefault().Value ?? string.Empty,
                "global::UnityEditor.InitializeOnLoadMethod");
        }

        /// <summary>
        /// Only value types on the WriteValue&lt;T&gt; branch get named to Netcode's post-processor.
        /// Naming a managed type there breaks the build — its post-processor errors on a managed type
        /// without IEquatable&lt;T&gt; — and naming a packet or INetworkSerializable would be noise,
        /// since those serialize by paths that never consult NetworkVariableSerialization.
        /// </summary>
        private static void RegistersSerializationForValueArgumentsOnly()
        {
            const string source = @"
using Unity.Netcode;
using Vapor.NetworkObjects;

namespace Game
{
    public enum Team { Red, Blue }

    public struct Damage : INetworkSerializable { }

    public interface IEvent : INetworkPacket { }

    public class Pawn : VaporNetworkObject { }

    public partial class Mixed : VaporNetworkObject
    {
        [VaporRpc(SendTo.Server)]
        private partial void MixedRpc(int amount, Team team, IEvent payload, Damage damage, Pawn pawn, string label);

        [VaporRpc(SendTo.Server)]
        private partial void RepeatedRpc(int first, int second, int third);

        private partial void MixedRpc(int amount, Team team, IEvent payload, Damage damage, Pawn pawn, string label) { }
        private partial void RepeatedRpc(int first, int second, int third) { }
    }
}";

            var result = Run(Split(source));
            Expect("serialization registration", result);

            var generated = result.Generated.FirstOrDefault().Value ?? string.Empty;
            ExpectContains("serialization registration", generated, "[global::Unity.Netcode.GenerateSerializationForType(typeof(int))]");
            ExpectContains("serialization registration", generated, "[global::Unity.Netcode.GenerateSerializationForType(typeof(global::Game.Team))]");

            ExpectMissing("serialization registration", generated, "GenerateSerializationForType(typeof(global::Game.IEvent))");
            ExpectMissing("serialization registration", generated, "GenerateSerializationForType(typeof(global::Game.Damage))");
            ExpectMissing("serialization registration", generated, "GenerateSerializationForType(typeof(global::Game.Pawn))");

            // A managed argument would make Netcode's post-processor fail the build outright.
            ExpectMissing("serialization registration", generated, "GenerateSerializationForType(typeof(string))");

            // Three int parameters, one attribute.
            var occurrences = generated.Split(new[] { "GenerateSerializationForType(typeof(int))" }, StringSplitOptions.None).Length - 1;
            if (occurrences != 2)
            {
                Fail("serialization registration", $"expected typeof(int) once per method (2 total), found {occurrences}");
            }
        }

        /// <summary>
        /// The rpc id is xxHash32 over "{module} / {method full name}", spelled the way Mono.Cecil
        /// spells it — the scheme the IL weaver used. Nothing persists an id, so this is not
        /// load-bearing; it is checked because the alternative is a README claiming parity that no
        /// one ever verified.
        /// </summary>
        private static void MatchesCecilNaming()
        {
            const string source = @"
using System.Collections.Generic;
using Unity.Netcode;
using Vapor.NetworkObjects;

namespace Naming
{
    public enum Rank { Low, High }

    public struct Payload : INetworkSerializable { }

    public partial class Holder
    {
        public struct Nested { }
    }

    public class Shapes : VaporNetworkObject
    {
        [VaporRpc(SendTo.Server)] public void PrimitivesRpc(int a, ulong b, string c, bool d, float e) { }
        [VaporRpc(SendTo.Server)] public void ArraysRpc(int[] flat, byte[][] jagged, float[,] grid) { }
        [VaporRpc(SendTo.Server)] public void GenericsRpc(List<int> list, Dictionary<string, Rank> map, int? maybe) { }
        [VaporRpc(SendTo.Server)] public void NestedRpc(Holder.Nested nested, Payload payload) { }
        [VaporRpc(SendTo.Server)] public void EmptyRpc() { }
    }
}";

            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
            var compilation = CSharpCompilation.Create(
                "vapor.rpc.naming.tests",
                new[]
                {
                    CSharpSyntaxTree.ParseText(RuntimeStubs.Source, parseOptions),
                    CSharpSyntaxTree.ParseText(source, parseOptions),
                },
                PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var peStream = new MemoryStream();
            var emit = compilation.Emit(peStream);
            if (!emit.Success)
            {
                Fail("cecil naming parity", string.Join("\n         ",
                    emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
                return;
            }

            peStream.Position = 0;
            using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(peStream);

            var fromCecil = new Dictionary<string, string>();
            foreach (var type in assembly.MainModule.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (method.CustomAttributes.Any(a => a.AttributeType.FullName == "Vapor.NetworkObjects.VaporRpcAttribute"))
                    {
                        fromCecil[method.Name] = $"{method.Module.Name} / {method.FullName}";
                    }
                }
            }

            int compared = 0;
            foreach (var type in AllTypes(compilation.Assembly.GlobalNamespace))
            {
                foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
                {
                    if (!method.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Vapor.NetworkObjects.VaporRpcAttribute"))
                    {
                        continue;
                    }

                    var mine = VaporRpcHash.BuildKey(method, compilation.AssemblyName);
                    if (!fromCecil.TryGetValue(method.Name, out var theirs))
                    {
                        Fail("cecil naming parity", $"Cecil never saw {method.Name}");
                        continue;
                    }

                    compared++;
                    if (mine != theirs)
                    {
                        Fail("cecil naming parity", $"{method.Name}\n         mine:  {mine}\n         cecil: {theirs}");
                    }
                }
            }

            if (compared != fromCecil.Count || compared == 0)
            {
                Fail("cecil naming parity", $"compared {compared} of {fromCecil.Count} rpcs");
                return;
            }

            if (s_Failures == 0)
            {
                Console.WriteLine($"  ok   cecil naming parity ({compared} signatures)");
            }
        }

        private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceOrTypeSymbol root)
        {
            foreach (var member in root.GetMembers())
            {
                switch (member)
                {
                    case INamedTypeSymbol type:
                        yield return type;
                        foreach (var nested in AllTypes(type))
                        {
                            yield return nested;
                        }

                        break;

                    case INamespaceSymbol ns:
                        foreach (var type in AllTypes(ns))
                        {
                            yield return type;
                        }

                        break;
                }
            }
        }

        #endregion

        #region Cases that must be rejected

        private static void RejectsNonPartialMethod() => ExpectDiagnostic("non-partial method", "VRPC003", @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public partial class Legacy : VaporNetworkObject
{
    [VaporRpc(SendTo.Server)]
    private void DoThingRpc(int value) { }
}");

        private static void RejectsNonPartialType() => ExpectDiagnostic("non-partial type", "VRPC002", @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public class Sealed : VaporNetworkObject
{
    [VaporRpc(SendTo.Server)]
    private partial void DoThingRpc(int value);
}");

        private static void RejectsNonNetworkObject() => ExpectDiagnostic("not a network object", "VRPC001", @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public partial class Stray
{
    [VaporRpc(SendTo.Server)]
    private partial void DoThingRpc(int value);

    private partial void DoThingRpc_Implementation(int value) { }
}");

        private static void RejectsBadName() => ExpectDiagnostic("bad name", "VRPC005", @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public partial class Named : VaporNetworkObject
{
    [VaporRpc(SendTo.Server)]
    private partial void DoThing(int value);
}");

        private static void RejectsByRefParameter() => ExpectDiagnostic("by-ref parameter", "VRPC006", @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public partial class Reffy : VaporNetworkObject
{
    [VaporRpc(SendTo.Server)]
    private partial void DoThingRpc(ref int value);
}");

        private static void RejectsStaticAndNonVoid()
        {
            ExpectDiagnostic("static rpc", "VRPC004", @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public partial class Statics : VaporNetworkObject
{
    [VaporRpc(SendTo.Server)]
    private static partial void DoThingRpc(int value);
}");

            ExpectDiagnostic("non-void rpc", "VRPC004", @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public partial class Returns : VaporNetworkObject
{
    [VaporRpc(SendTo.Server)]
    private partial int DoThingRpc(int value);
}");

            ExpectDiagnostic("generic type", "VRPC004", @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public partial class Generic<T> : VaporNetworkObject
{
    [VaporRpc(SendTo.Server)]
    private partial void DoThingRpc(int value);
}");
        }

        /// <summary>
        /// The runtime throws on this one from inside the send. Better to say so at build time.
        /// </summary>
        private static void RejectsSpecifiedInParams() => ExpectDiagnostic("SpecifiedInParams", "VRPC010", @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public partial class Targeted : VaporNetworkObject
{
    [VaporRpc(SendTo.SpecifiedInParams)]
    private partial void PickyRpc(int value);

    private partial void PickyRpc_Implementation(int value) { }
}");

        /// <summary>
        /// A warning, not an error — UserNetworkVariableSerialization is a real escape hatch this
        /// generator cannot see being used.
        /// </summary>
        private static void WarnsOnUnserializableManagedParameter() => ExpectDiagnostic("managed parameter", "VRPC009", @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public class Bag { }

public partial class Carrier : VaporNetworkObject
{
    [VaporRpc(SendTo.Server)]
    private partial void CarryRpc(Bag bag);

    private partial void CarryRpc_Implementation(Bag bag) { }
}");

        private static void RejectsSerializableWithoutConstructor() => ExpectDiagnostic("serializable without ctor", "VRPC007", @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public class Payload : INetworkSerializable
{
    public Payload(int value) { }
}

public partial class Sender : VaporNetworkObject
{
    [VaporRpc(SendTo.Server)]
    private partial void DoThingRpc(Payload payload);
}");

        private static void RejectsAlreadyImplementedPartial() => ExpectDiagnostic("body left on the declaration", "VRPC004", @"
using Unity.Netcode;
using Vapor.NetworkObjects;

public partial class HalfMigrated : VaporNetworkObject
{
    [VaporRpc(SendTo.Server)]
    private partial void DoThingRpc(int value);

    private partial void DoThingRpc(int value) { }
}");

        #endregion

        #region Harness

        private sealed class RunResult
        {
            public ImmutableArray<Diagnostic> Diagnostics;
            public Dictionary<string, string> Generated = new();

            public IEnumerable<Diagnostic> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>
        /// Turns an author-shaped source — where the bodies are written with the rpc's own name — into
        /// what the compiler actually sees, with the bodies renamed to '_Implementation'. Keeps each
        /// test case readable as the pair an author writes.
        /// </summary>
        /// <remarks>
        /// The body half always comes out <c>private</c>, whatever the rpc itself is. That is not a
        /// simplification for the tests' sake — it is the rule, because the generator declares that
        /// half and a body reachable from outside the type would be a way to run an rpc without
        /// sending it.
        /// </remarks>
        private static string Split(string source)
        {
            var lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var open = line.IndexOf("Rpc(", StringComparison.Ordinal);
                var partial = line.IndexOf("partial ", StringComparison.Ordinal);

                if (open < 0 || partial < 0 || !line.TrimEnd().EndsWith("{ }", StringComparison.Ordinal))
                {
                    continue;
                }

                var indent = line.Substring(0, line.Length - line.TrimStart().Length);
                var renamed = line.Substring(partial, open + 3 - partial) + "_Implementation" + line.Substring(open + 3);
                lines[i] = indent + "private " + renamed;
            }

            return string.Join("\n", lines);
        }

        private static RunResult Run(string source, params string[] defines)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: defines);

            var compilation = CSharpCompilation.Create(
                "vapor.rpc.generator.tests",
                new[]
                {
                    CSharpSyntaxTree.ParseText(RuntimeStubs.Source, parseOptions),
                    CSharpSyntaxTree.ParseText(source, parseOptions),
                },
                PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var driver = CSharpGeneratorDriver
                .Create(new[] { new VaporRpcIncrementalGenerator().AsSourceGenerator() }, parseOptions: parseOptions)
                .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);

            var result = new RunResult
            {
                Diagnostics = generatorDiagnostics.AddRange(updated.GetDiagnostics()),
            };

            foreach (var tree in driver.GetRunResult().GeneratedTrees)
            {
                result.Generated[Path.GetFileName(tree.FilePath)] = tree.ToString();
            }

            return result;
        }

        private static IEnumerable<MetadataReference> PlatformReferences() =>
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));

        private static void Expect(string name, RunResult result)
        {
            var errors = result.Errors.ToList();
            if (errors.Count == 0 && result.Generated.Count > 0)
            {
                Console.WriteLine($"  ok   {name}");
                return;
            }

            Fail(name, errors.Count > 0
                ? string.Join("\n         ", errors.Select(e => e.ToString()))
                : "the generator produced no source");
        }

        private static void ExpectDiagnostic(string name, string id, string source)
        {
            var result = Run(source);
            if (result.Diagnostics.Any(d => d.Id == id))
            {
                Console.WriteLine($"  ok   {name} -> {id}");
                return;
            }

            var reported = result.Errors.Select(e => e.Id).Distinct().ToList();
            Fail(name, $"expected {id}, got {(reported.Count > 0 ? string.Join(", ", reported) : "nothing")}");
        }

        private static void ExpectContains(string name, string generated, string expected)
        {
            if (!generated.Contains(expected))
            {
                Fail(name, $"generated source is missing: {expected}");
            }
        }

        private static void ExpectMissing(string name, string generated, string unexpected)
        {
            if (generated.Contains(unexpected))
            {
                Fail(name, $"generated source should not contain: {unexpected}");
            }
        }

        private static void Fail(string name, string detail)
        {
            s_Failures++;
            Console.WriteLine($"  FAIL {name}");
            Console.WriteLine($"         {detail}");
        }

        private static void PrintGeneratedSource()
        {
            const string source = @"
using Unity.Netcode;
using Vapor.NetworkObjects;

namespace Game
{
    public interface IEvent : INetworkPacket { }

    public partial class Ability : VaporNetworkObject
    {
        [VaporRpc(SendTo.Server)]
        private partial void ActivateAbilityRpc(ulong predictedKey, IEvent userInput);

        private partial void ActivateAbilityRpc(ulong predictedKey, IEvent userInput) { }
    }
}";

            Console.WriteLine();
            foreach (var generated in Run(Split(source)).Generated)
            {
                Console.WriteLine($"===== {generated.Key} =====");
                Console.WriteLine(generated.Value);
            }
        }

        #endregion
    }
}
