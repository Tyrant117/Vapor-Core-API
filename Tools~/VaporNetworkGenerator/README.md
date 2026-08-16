# Vapor network source generator

Two generators in one analyzer DLL, for the `Vapor.Networking` runtime:

- **Rpc**: the send path, receive handler and registration for every `[VaporRpc]` method on a
  `VaporNetworkObject` or `NetworkComponent`.
- **Formatter**: a binary `NetworkFormatter<T>` for every `[NetworkSerializable]` type (or any type
  carrying a `[NetworkSerialize]` member), plus its registration.

This is the successor of `Tools~/VaporRpcGenerator`, which targeted the Netcode-for-GameObjects-based
`Vapor.NetworkObjects` layer. The rpc hash scheme is unchanged.

## Writing an rpc

```csharp
public partial class GameplayAbility : VaporNetworkObject
{
    [VaporRpc(RpcTarget.Server)]
    private partial void ActivateAbilityRpc(ulong predictedKey, HitInfo hit);

    private partial void ActivateAbilityRpc_Implementation(ulong predictedKey, HitInfo hit)
    {
        // the body — runs on every peer the rpc reached
    }
}
```

The declaring type (and every type containing it) has to be `partial`. Call sites are unchanged:
`ActivateAbilityRpc(key, hit)` means "send this". Arguments go through `NetworkFormatters` —
built-ins, generated formatters, or anything registered by hand; `VaporNetworkObject` and
`NetworkComponent` arguments travel as ids and resolve against the receiving world.

Targets are `RpcTarget` (`Server`, `Owner`, `NotOwner`, `NotServer`, `Everyone`, `Me`, `NotMe`);
delivery is `Delivery` (default `ReliableSequenced`). Clients may address any target; the server
proxies.

## Writing a serializable type

```csharp
[NetworkSerializable]
public partial struct HitInfo
{
    public int Amount;
    public Vector3 Direction;
    [SerializeField] private float _crit;
    [NetworkIgnore] public int Scratch;
}
```

Member rules are VSL's: public fields and `[SerializeField]` privates when the type is
`[NetworkSerializable]` or `[VslSerializable]`; anything `[NetworkSerialize]` or `[VslSerialize]`
(properties need get and set); nothing `[NetworkIgnore]`, `[VslIgnore]` or `[NonSerialized]`. Members
are written in declaration order, base type first. Classes need a parameterless constructor (any
accessibility) and are written with a null marker.

## Diagnostics

| Id | Severity | Rule |
|---|---|---|
| VNET001 | error | `[VaporRpc]` on a type that derives from neither `VaporNetworkObject` nor `NetworkComponent` |
| VNET002 | error | declaring type (or a container) is not `partial` |
| VNET003 | error | rpc is not a bodiless partial declaration |
| VNET004 | error | static / virtual / abstract / override / generic / non-void / async / body on the declaration |
| VNET005 | error | name does not end in `Rpc` |
| VNET006 | error | `ref`/`out`/`in`, pointer, type parameter, or `NetworkReader`/`NetworkWriter` parameter |
| VNET008 | error | rpc id hashed to 0 |
| VNET101 | warning | `[NetworkSerializable]` type is not `partial` — no formatter generated |
| VNET102 | warning | class has no parameterless constructor — no formatter generated |
| VNET103 | warning | a member was skipped (inaccessible base member, property without get+set) |
| VNET104 | warning | interface / abstract / static / generic type — no formatter generated |

## Building

```
build.bat
```

Builds Release and copies `Vapor.Network.SourceGenerator.dll` into
`Assets/Vapor Core API/Runtime/Networking/Analyzers/`. Close Unity first if the copy fails.
Requires the .NET SDK. `netstandard2.0` and `Microsoft.CodeAnalysis.CSharp` 4.3.0 are deliberate.

## Testing

```
dotnet run --project tests/VaporNetworkGeneratorTests.csproj -c Release
```

Runs both generators over a stub of the runtime surface (`tests/RuntimeStubs.cs` — keep it in step
with `Runtime/Networking`), compiles the result at C# 9, and exits non-zero on failure.
`-- --print` dumps a generated file.
