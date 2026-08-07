# VaporRpc source generator

Writes the send path, the receive handler, and the registration for every `[VaporRpc]` method on a
`VaporNetworkObject`. Replaces the IL post-processor that used to live in `Editor/CodeGen`.

## Writing an rpc

The rpc is a **partial method with no body**. Its body is a second partial named
`<Name>_Implementation`, and it is always `private`:

```csharp
public partial class GameplayAbility : VaporNetworkObject
{
    [VaporRpc(SendTo.Server)]
    private partial void ActivateAbilityRpc(ulong predictedKey, IGameplayEventData userInput);

    private partial void ActivateAbilityRpc_Implementation(ulong predictedKey, IGameplayEventData userInput)
    {
        // the body — runs on every peer the rpc reached
    }
}
```

Call sites are unchanged: `ActivateAbilityRpc(key, input)` still means "send this". The declaring
type — and every type containing it — has to be `partial`.

The split exists because a source generator can only add code, never rewrite a body, which is the one
thing the IL weaver could do. Two properties fall out of it, both good:

- **A missing body is a compile error.** The generator declares `_Implementation` with an explicit
  accessibility, which is what obliges the compiler to demand an implementation (CS8795). An rpc
  whose body was never written cannot ship as a call that silently does nothing.
- **The body is unreachable from outside the type.** `_Implementation` is `private` whatever the rpc
  itself is, so there is no way to run an rpc's body while skipping the send. If you give it any
  other accessibility the compiler rejects the pair (CS8799).

## Building

```
build.bat
```

Builds Release and copies `Vapor.Rpc.SourceGenerator.dll` into
`Assets/Vapor Core API/Runtime/Network Objects/Analyzers/`. Unity reimports it and feeds it to the C#
compiler. Close Unity first if the copy fails — the editor holds a lock on loaded analyzers.

Requires the .NET SDK. `netstandard2.0` and `Microsoft.CodeAnalysis.CSharp` 4.3.0 are deliberate: a
generator built against a newer Roslyn than the editor hosts loads without running, silently.

## Testing

```
dotnet run --project tests/VaporRpcGeneratorTests.csproj -c Release
```

Runs the generator over a stub of the runtime surface, compiles the result at C# 9 (Unity 6's
language version), and exits non-zero on failure. `-- --print` dumps a generated file.

`tests/RuntimeStubs.cs` mirrors `VaporNetworkObject` and `RpcSerialization` **including their
accessibility**, which is the part worth keeping in step. Generated code lives in a subclass in
someone else's assembly, so tightening `BeginSendRpc` or `RpcSerialization` would break every
generated file while everything inside `vapor.core.runtime` still compiled.

## Which assemblies it applies to

Unity 6 applies a `RoslynAnalyzer`-labelled DLL to every assembly in the project, not only to the one
whose folder holds it. That is what makes it reach `vapor.gameplayframework.runtime` and your game
code from a folder inside Vapor Core.

Unlike the VSL generator, there is no reflection fallback here. An rpc the generator never saw is not
a slower rpc — it is a method that does not compile. That is deliberate: every failure mode is loud.

## What it generates

Per declaring type, one file holding — for each rpc — the implementing half of your declaration, the
declaring half of `_Implementation`, a `_Receive` handler, and one `RegisterVaporRpcs` entry point:

```csharp
private partial void ActivateAbilityRpc(ulong predictedKey, global::Vapor.IGameplayEventData userInput)
{
    if (!BeginSendRpc(0xB6157454u, out var rpcWriter)) { return; }

    global::Vapor.NetworkObjects.RpcSerialization.WriteValue<ulong>(rpcWriter, predictedKey);
    global::Vapor.NetworkObjects.RpcSerialization.WritePacket(rpcWriter, userInput);

    // true means this peer is also a target, so the body runs here too.
    if (!EndSendRpc(rpcWriter, SendTo.Server, NetworkDelivery.ReliableSequenced)) { return; }

    ActivateAbilityRpc_Implementation(predictedKey, userInput);
}
```

Registration is emitted **into the type**, not into one class per assembly. That is what lets every
generated member stay `private` and makes an rpc on a private nested type need no special handling.

## How an argument crosses the wire

Decided at compile time, in this order — the order matters, because `VaporNetworkObject` is itself an
`INetworkPacket`:

| Parameter type | Serialized by | Notes |
| --- | --- | --- |
| `VaporNetworkObject` or a subclass | `WriteNetworkObject` | Sent by id, resolved against the receiver's own instance |
| `NetworkBehaviour` or a subclass | `WriteNetworkBehaviour` | Sent as a `NetworkBehaviourReference` |
| implements `INetworkPacket` | `WritePacket` | Type-tagged, so an interface-typed parameter keeps its concrete type |
| implements `INetworkSerializable` | `WriteSerializable<T>` | Needs a parameterless constructor |
| anything else | `WriteValue<T>` | `NetworkVariableSerialization<T>` — primitives, vectors, enums, plain structs |

### Telling Netcode which types to generate serialization for

`WriteValue<T>` is the only branch that goes through `NetworkVariableSerialization<T>`, and Netcode's
own post-processor discovers the types it must generate for by scanning `NetworkVariable<T>`
declarations and `[GenerateSerializationFor…]` attributes — it has no idea what an rpc signature looks
like. A type used *only* as an rpc argument therefore reaches `FallbackSerializer` and throws on the
first send.

So the generator emits `[GenerateSerializationForType(typeof(T))]` on each send method for the value
types on that branch. `NetworkBehaviourILPP` scans every method in the assembly for it, so declaring
it there is enough, and it works for `VaporNetworkObject` subclasses even though they are not
`NetworkBehaviour`s.

**Value types only, deliberately.** Netcode's managed-type branch is not a soft fallback: it *errors
the build* for a managed type without `IEquatable<T>`, and even with it only generates a serializer
for `INetworkSerializable` — which is already handled a row above. So for a managed type on this
branch there is nothing useful to ask for, and asking breaks the build. Those get VRPC009 instead.

## The rpc id

xxHash32 over `"{module} / {method full name}"`, with names spelled the way `Mono.Cecil` spells them —
the scheme the IL weaver used. Nothing persists an id: it is baked into the send call and the
registration on every build, and the receiver looks it up in a table built the same way. So the only
thing that has to hold is that every peer in a session computes the same value from the same source.

Parity with the weaver is therefore not load-bearing — it is kept because it is cheap, and it means a
build that still mixes the two agrees. `MatchesCecilNaming` in the test project checks it against
Cecil directly rather than leaving it as a claim; that is how the multi-dimensional array case
(`System.Single[0...,0...]`, not `[,]`) was found.

## Diagnostics

All errors bar one. An rpc that fails to generate is not a slow rpc, it is a method that stops
crossing the network, so nothing degrades quietly.

| Id | Meaning |
| --- | --- |
| VRPC001 | The declaring type does not derive from `VaporNetworkObject`. |
| VRPC002 | The declaring type, or a type containing it, is not `partial`. |
| VRPC003 | The rpc is not a partial declaration. **This is what every rpc written against the IL weaver hits** — the message spells out the move. |
| VRPC004 | Unsupported signature: static, virtual, abstract, override, generic, non-void, async, or a body left on the declaration. |
| VRPC005 | The method name does not end with `Rpc`. |
| VRPC006 | A parameter is `ref`/`out`/`in`, a pointer, or a `FastBufferReader`. |
| VRPC007 | An `INetworkSerializable` parameter has no parameterless constructor, so the receiver cannot construct it. |
| VRPC008 | The rpc id hashed to 0, which the runtime reserves. Rename the method. |
| VRPC010 | `SendTo.SpecifiedInParams` needs a per-call client list, which VaporRpc cannot carry — the runtime throws on it. Use `NetworkMessages.SendToGroup` for a hand-picked target set. |
| VRPC009 | **Warning.** A managed parameter type with no generated serialization — it will throw on the first send. A warning rather than an error only because `UserNetworkVariableSerialization<T>` is a real escape hatch the generator cannot see being used. |

## Inspecting the output

Add this to a project's `.csproj` to write the generated files to disk:

```xml
<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
<CompilerGeneratedFilesOutputPath>generated</CompilerGeneratedFilesOutputPath>
```
