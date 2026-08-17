# VSL source generator

Emits a zero-reflection `IVslFormatter<T>` for every type taking part in VSL serialization — types
marked `[VslSerializable]`, and types holding `[VslSerialize]` members.

## Building

```
build.bat
```

Builds Release and copies `Vapor.Vsl.SourceGenerator.dll` into
`Assets/Vapor Core API/Runtime/Serialization Language/Analyzers/`. Unity reimports it and feeds it to
the C# compiler. Close Unity first if the copy fails — the editor holds a lock on loaded analyzers.

Requires the .NET SDK. `netstandard2.0` and `Microsoft.CodeAnalysis.CSharp` 4.3.0 are deliberate: a
generator built against a newer Roslyn than the editor hosts loads without running, silently.

## Which assemblies it applies to

Unity 6 applies a `RoslynAnalyzer`-labelled DLL to every assembly in the project, not only to the one
whose folder holds it — verified here by the generator running against `vapor.core.tests`, which sits
outside `Runtime/`. So the default location covers your game code too.

If a particular assembly turns out not to be covered, copy `Vapor.Vsl.SourceGenerator.dll` and its
`.meta` to `Assets/Analyzers/`.

Nothing breaks either way. A type the generator never saw still serializes correctly through
`ReflectionFormatter`; it is just slower, and boxes on every member. That is the whole difference —
the two paths are covered by a differential test asserting byte-identical output.

## What generated code needs from the runtime

Generated formatters live in the **consuming** assembly, so everything they touch has to be public:
`VslWriter`, `VslReader`, `VslFormatterRegistry`, `VslTypeRegistry`, `VslNames`, `VslException`, and
`VslContext.EnterDepth`/`ExitDepth`. Marking any of those `internal` compiles fine inside
`vapor.core.runtime` and then breaks every other assembly — a failure a single-assembly test cannot
reproduce.

## Diagnostics

| Id | Severity | Meaning |
| --- | --- | --- |
| VSL001 | Info | The type is not `partial`, so no formatter can be nested inside it. Falls back to reflection. |
| VSL002 | Warning | A `[VslSerialize]` property is missing a getter or a setter. |
| VSL003 | Warning | The type has no accessible parameterless constructor, so it cannot be deserialized. |
| VSL004 | Info | A `private` field on a base type is out of reach from the derived type's nested formatter. Falls back to reflection. |
| VSL005 | Error | Two serialized members match after VSL's case/prefix normalization. Give one a distinct `[VslName]`. |

## What it skips, and why that is fine

Abstract types, open generics, and static types never get a generated formatter — an abstract type is
never instantiated, and an open generic would need one formatter per construction. Reflection covers
them.

## Inspecting the output

Add this to a project's `.csproj` to write the generated files to disk:

```xml
<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
<CompilerGeneratedFilesOutputPath>generated</CompilerGeneratedFilesOutputPath>
```
