using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Vapor.Network.SourceGenerator.Tests
{
    internal static class Program
    {
        private static int s_Failures;

        private static int Main(string[] args)
        {
            RpcOnObjectAndComponentGenerates();
            RpcArgumentShapes();
            RpcMirrorsModifiersAndNesting();
            RpcCompilesUnderUnityEditor();
            RpcRejectsNonPartialMethod();
            RpcRejectsNonPartialType();
            RpcRejectsNonHost();
            RpcRejectsBadName();
            RpcRejectsByRef();
            RpcRejectsStaticAndNonVoid();
            RpcRejectsReaderParameter();

            FormatterForStructAndClass();
            FormatterFollowsVslMemberRules();
            FormatterSkipsInaccessibleBaseMembers();
            FormatterWarnsOnNonPartial();
            FormatterWarnsOnMissingConstructor();
            FormatterFromMemberAttributeAlone();

            if (args.Contains("--print"))
            {
                PrintGeneratedSource();
            }

            Console.WriteLine();
            Console.WriteLine(s_Failures == 0 ? "All generator tests passed." : $"{s_Failures} generator test(s) FAILED.");
            return s_Failures == 0 ? 0 : 1;
        }

        #region Rpc cases

        private static void RpcOnObjectAndComponentGenerates()
        {
            const string source = @"
using Vapor.Networking;

namespace Game
{
    public partial class Ability : VaporNetworkObject
    {
        [VaporRpc(RpcTarget.Server)]
        private partial void ActivateRpc(ulong key);

        private partial void ActivateRpc(ulong key) { }
    }

    public partial class Quest : NetworkComponent
    {
        [VaporRpc(RpcTarget.Owner, Delivery.Unreliable)]
        public partial void ProgressRpc(int step);

        public partial void ProgressRpc(int step) { }
    }
}";
            var result = Run(Split(source));
            Expect("rpc on object and component", result);
            var all = string.Join("\n", result.Generated.Values);
            ExpectContains("rpc on object and component", all, "global::Vapor.Networking.RpcRegistry.Register(0x");
            ExpectContains("rpc on object and component", all, "global::Vapor.Networking.RpcTarget.Server");
            ExpectContains("rpc on object and component", all, "global::Vapor.Networking.RpcTarget.Owner, global::Vapor.Networking.Delivery.Unreliable");
            ExpectContains("rpc on object and component", all, "global::Vapor.Networking.Delivery.ReliableSequenced");
            ExpectContains("rpc on object and component", all, "private static void ActivateRpc_Receive(global::Vapor.Networking.IRpcHost rpcTarget, global::Vapor.Networking.NetworkReader rpcReader)");
            ExpectContains("rpc on object and component", all, "((global::Game.Quest)rpcTarget).ProgressRpc_Implementation(step);");
            ExpectContains("rpc on object and component", all, "NetworkFormatters.Write<ulong>(rpcWriter, key);");
            ExpectContains("rpc on object and component", all, "var key = global::Vapor.Networking.NetworkFormatters.Read<ulong>(rpcReader);");
        }

        private static void RpcArgumentShapes()
        {
            const string source = @"
using System.Collections.Generic;
using Vapor.Networking;

namespace Game
{
    public enum Team { Red, Blue }
    public struct Coords { public float X; }
    public class Pawn : VaporNetworkObject { }
    public class Inventory : NetworkComponent { }

    public partial class Combat : VaporNetworkObject
    {
        [VaporRpc(RpcTarget.Everyone)]
        private partial void HitRpc(int amount, string label, Team team, float[] spread, List<Coords> path, Pawn pawn, VaporNetworkObject any, Inventory bag, NetworkComponent anyComponent, UnityEngine.Vector3 where);

        private partial void HitRpc(int amount, string label, Team team, float[] spread, List<Coords> path, Pawn pawn, VaporNetworkObject any, Inventory bag, NetworkComponent anyComponent, UnityEngine.Vector3 where) { }
    }
}";
            var result = Run(Split(source));
            Expect("argument shapes", result);
            var generated = result.Generated.First().Value;
            ExpectContains("argument shapes", generated, "NetworkFormatters.Write<global::System.Collections.Generic.List<global::Game.Coords>>(rpcWriter, path);");
            ExpectContains("argument shapes", generated, "RpcArguments.WriteObject(rpcWriter, pawn);");
            ExpectContains("argument shapes", generated, "var pawn = (global::Game.Pawn)global::Vapor.Networking.RpcArguments.ReadObject(rpcTarget, rpcReader);");
            ExpectContains("argument shapes", generated, "var any = global::Vapor.Networking.RpcArguments.ReadObject(rpcTarget, rpcReader);");
            ExpectContains("argument shapes", generated, "var bag = (global::Game.Inventory)global::Vapor.Networking.RpcArguments.ReadComponent(rpcTarget, rpcReader);");
            ExpectContains("argument shapes", generated, "NetworkFormatters.Read<global::UnityEngine.Vector3>(rpcReader)");
        }

        private static void RpcMirrorsModifiersAndNesting()
        {
            const string source = @"
using Vapor.Networking;

namespace Deep.Down
{
    public partial class Outer
    {
        public partial class Inner : VaporNetworkObject
        {
            [VaporRpc(RpcTarget.Server)]
            protected internal partial void MutterRpc(int rpcTarget);

            protected internal partial void MutterRpc(int rpcTarget) { }
        }
    }
}";
            var result = Run(Split(source));
            Expect("modifiers and nesting", result);
            var generated = result.Generated.First().Value;
            ExpectContains("modifiers and nesting", generated, "namespace Deep.Down");
            ExpectContains("modifiers and nesting", generated, "partial class Outer");
            ExpectContains("modifiers and nesting", generated, "protected internal partial void MutterRpc(int rpcTarget)");
            ExpectContains("modifiers and nesting", generated, "private partial void MutterRpc_Implementation(int rpcTarget);");
            ExpectContains("modifiers and nesting", generated, "var arg0 =");   // parameter named like a generated local
        }

        private static void RpcCompilesUnderUnityEditor()
        {
            const string source = @"
using Vapor.Networking;
public partial class Thing : VaporNetworkObject
{
    [VaporRpc(RpcTarget.Me)]
    private partial void PokeRpc();
    private partial void PokeRpc() { }
}";
            var result = Run(Split(source), "UNITY_EDITOR");
            Expect("UNITY_EDITOR build", result);
            ExpectContains("UNITY_EDITOR build", result.Generated.First().Value, "[global::UnityEditor.InitializeOnLoadMethod]");
        }

        private static void RpcRejectsNonPartialMethod() => ExpectDiagnostic("non-partial method", "VNET003", @"
using Vapor.Networking;
public partial class Thing : VaporNetworkObject
{
    [VaporRpc(RpcTarget.Server)]
    private void PokeRpc() { }
}");

        private static void RpcRejectsNonPartialType() => ExpectDiagnostic("non-partial type", "VNET002", @"
using Vapor.Networking;
public class Thing : VaporNetworkObject
{
    [VaporRpc(RpcTarget.Server)]
    private partial void PokeRpc();
    private partial void PokeRpc_Implementation() { }
}");

        private static void RpcRejectsNonHost() => ExpectDiagnostic("not a host", "VNET001", @"
using Vapor.Networking;
public partial class Thing
{
    [VaporRpc(RpcTarget.Server)]
    private partial void PokeRpc();
    private partial void PokeRpc_Implementation() { }
}");

        private static void RpcRejectsBadName() => ExpectDiagnostic("bad name", "VNET005", @"
using Vapor.Networking;
public partial class Thing : VaporNetworkObject
{
    [VaporRpc(RpcTarget.Server)]
    private partial void Poke();
    private partial void Poke_Implementation() { }
}");

        private static void RpcRejectsByRef() => ExpectDiagnostic("by-ref parameter", "VNET006", @"
using Vapor.Networking;
public partial class Thing : VaporNetworkObject
{
    [VaporRpc(RpcTarget.Server)]
    private partial void PokeRpc(ref int value);
    private partial void PokeRpc_Implementation(ref int value) { }
}");

        private static void RpcRejectsStaticAndNonVoid()
        {
            ExpectDiagnostic("static rpc", "VNET004", @"
using Vapor.Networking;
public partial class Thing : VaporNetworkObject
{
    [VaporRpc(RpcTarget.Server)]
    private static partial void PokeRpc();
    private static partial void PokeRpc_Implementation() { }
}");
            ExpectDiagnostic("non-void rpc", "VNET004", @"
using Vapor.Networking;
public partial class Thing : VaporNetworkObject
{
    [VaporRpc(RpcTarget.Server)]
    private partial int PokeRpc();
    private partial int PokeRpc_Implementation() => 0;
}");
        }

        private static void RpcRejectsReaderParameter() => ExpectDiagnostic("reader parameter", "VNET006", @"
using Vapor.Networking;
public partial class Thing : VaporNetworkObject
{
    [VaporRpc(RpcTarget.Server)]
    private partial void PokeRpc(NetworkReader reader);
    private partial void PokeRpc_Implementation(NetworkReader reader) { }
}");

        #endregion

        #region Formatter cases

        private static void FormatterForStructAndClass()
        {
            const string source = @"
using UnityEngine;
using Vapor.Networking;

namespace Game
{
    [NetworkSerializable]
    public partial struct Damage
    {
        public int Amount;
        public Vector3 Direction;
        [SerializeField] private float _crit;
        public float Ignored => _crit;
    }

    [NetworkSerializable]
    public partial class Loot
    {
        public string Name;
        public Damage[] Rolls;
        [NetworkIgnore] public int Scratch;
        private Loot() { }
    }
}";
            var result = Run(source);
            Expect("struct and class formatters", result);
            var all = string.Join("\n", result.Generated.Values);
            ExpectContains("struct and class formatters", all, "public sealed class VaporNetworkFormatter : global::Vapor.Networking.NetworkFormatter<global::Game.Damage>");
            ExpectContains("struct and class formatters", all, "NetworkFormatters.Write<int>(writer, value.Amount);");
            ExpectContains("struct and class formatters", all, "NetworkFormatters.Write<float>(writer, value._crit);");
            ExpectContains("struct and class formatters", all, "var value = default(global::Game.Damage);");
            ExpectContains("struct and class formatters", all, "value._crit = global::Vapor.Networking.NetworkFormatters.Read<float>(reader);");
            ExpectMissing("struct and class formatters", all, "value.Ignored");
            ExpectContains("struct and class formatters", all, "writer.WriteBool(false);");
            ExpectContains("struct and class formatters", all, "var value = new global::Game.Loot();");
            ExpectContains("struct and class formatters", all, "NetworkFormatters.Write<global::Game.Damage[]>(writer, value.Rolls);");
            ExpectMissing("struct and class formatters", all, "value.Scratch");
            ExpectContains("struct and class formatters", all, "NetworkFormatters.Register<global::Game.Loot>(VaporNetworkFormatter.Instance);");
        }

        private static void FormatterFollowsVslMemberRules()
        {
            const string source = @"
using Vapor;
using Vapor.Networking;

namespace Game
{
    [VslSerializable, NetworkSerializable]
    public partial class Item
    {
        public int Level;                                  // public field: in
        [VslIgnore] public int NotThis;                    // vsl ignore: out
        [VslSerialize] private string _name;               // vsl opt-in private: in
        [VslSerialize] public float Weight { get; set; }   // attributed property: in
        public float Volume { get; set; }                  // plain property: out (opt-in only)
        public readonly int Frozen;                        // readonly: out
        public const int Max = 3;                          // const: out
        [NetworkSerialize] public int Charges { get; private set; }   // private setter, same type: in
    }
}";
            var result = Run(source);
            Expect("vsl member rules", result);
            var generated = result.Generated.First().Value;
            ExpectContains("vsl member rules", generated, "value.Level");
            ExpectMissing("vsl member rules", generated, "value.NotThis");
            ExpectContains("vsl member rules", generated, "value._name");
            ExpectContains("vsl member rules", generated, "value.Weight");
            ExpectMissing("vsl member rules", generated, "value.Volume");
            ExpectMissing("vsl member rules", generated, "value.Frozen");
            ExpectMissing("vsl member rules", generated, "value.Max");
            ExpectContains("vsl member rules", generated, "value.Charges = ");
        }

        private static void FormatterSkipsInaccessibleBaseMembers()
        {
            const string source = @"
using Vapor.Networking;

namespace Game
{
    [NetworkSerializable]
    public partial class Base
    {
        public int Visible;
        [NetworkSerialize] private int _hidden;
        protected int Reachable;
    }

    [NetworkSerializable]
    public partial class Derived : Base
    {
        public int Own;
    }
}";
            var result = Run(source);
            Expect("base members", result);
            var derived = result.Generated.First(g => g.Key.Contains("Derived")).Value;
            ExpectContains("base members", derived, "value.Visible");
            ExpectContains("base members", derived, "value.Own");
            ExpectMissing("base members", derived, "value._hidden");
            ExpectMissing("base members", derived, "value.Reachable");   // protected but not opted in (type opt-in covers public/[SerializeField])
            if (!result.Diagnostics.Any(d => d.Id == "VNET103")) Fail("base members", "expected VNET103 for the inaccessible private base member");
        }

        private static void FormatterWarnsOnNonPartial() => ExpectDiagnostic("non-partial formatter type", "VNET101", @"
using Vapor.Networking;
[NetworkSerializable]
public class Plain { public int A; }");

        private static void FormatterWarnsOnMissingConstructor() => ExpectDiagnostic("no ctor", "VNET102", @"
using Vapor.Networking;
[NetworkSerializable]
public partial class NoCtor { public int A; public NoCtor(int a) { A = a; } }");

        private static void FormatterFromMemberAttributeAlone()
        {
            const string source = @"
using Vapor.Networking;
public partial class OptIn
{
    [NetworkSerialize] public int A;
    public int B;
}";
            var result = Run(source);
            Expect("member attribute alone", result);
            var generated = result.Generated.First().Value;
            ExpectContains("member attribute alone", generated, "value.A");
            ExpectMissing("member attribute alone", generated, "value.B");
        }

        #endregion

        #region Harness

        private sealed class RunResult
        {
            public ImmutableArray<Diagnostic> Diagnostics;
            public Dictionary<string, string> Generated = new();
            public IEnumerable<Diagnostic> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
        }

        /// <summary>Renames the author-shaped bodies ('...Rpc(...) { }') to their '_Implementation' halves.</summary>
        private static string Split(string source)
        {
            var lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var open = line.IndexOf("Rpc(", StringComparison.Ordinal);
                var partial = line.IndexOf("partial ", StringComparison.Ordinal);
                if (open < 0 || partial < 0 || !line.TrimEnd().EndsWith("{ }", StringComparison.Ordinal)) continue;

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
                "vapor.network.generator.tests",
                new[]
                {
                    CSharpSyntaxTree.ParseText(RuntimeStubs.Source, parseOptions),
                    CSharpSyntaxTree.ParseText(source, parseOptions),
                },
                PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var driver = CSharpGeneratorDriver
                .Create(new[] { new RpcGenerator().AsSourceGenerator(), new FormatterGenerator().AsSourceGenerator() }, parseOptions: parseOptions)
                .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);

            var result = new RunResult { Diagnostics = generatorDiagnostics.AddRange(updated.GetDiagnostics()) };
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

            Fail(name, errors.Count > 0 ? string.Join("\n         ", errors.Select(e => e.ToString())) : "the generator produced no source");
        }

        private static void ExpectDiagnostic(string name, string id, string source)
        {
            var result = Run(source);
            if (result.Diagnostics.Any(d => d.Id == id))
            {
                Console.WriteLine($"  ok   {name} -> {id}");
                return;
            }

            var reported = result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(e => e.Id).Distinct().ToList();
            Fail(name, $"expected {id}, got {(reported.Count > 0 ? string.Join(", ", reported) : "nothing")}");
        }

        private static void ExpectContains(string name, string generated, string expected)
        {
            if (!generated.Contains(expected)) Fail(name, $"generated source is missing: {expected}");
        }

        private static void ExpectMissing(string name, string generated, string unexpected)
        {
            if (generated.Contains(unexpected)) Fail(name, $"generated source should not contain: {unexpected}");
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
using Vapor.Networking;

namespace Game
{
    [NetworkSerializable]
    public partial struct Hit { public int Amount; public string Source; }

    public partial class Ability : VaporNetworkObject
    {
        [VaporRpc(RpcTarget.Server)]
        private partial void ActivateRpc(ulong predictedKey, Hit hit);

        private partial void ActivateRpc(ulong predictedKey, Hit hit) { }
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
