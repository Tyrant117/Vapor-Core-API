# Vapor Core API
- [Introduction](#introduction)
- [Installation](#installation)
- [Usage](#usage)
  - [Gameplay Tags & Data Registry](#gameplay-tags--data-registry)
  - [Gameplay Events](#gameplay-events)
  - [Observable Values](#observable-values)
  - [Serialization Language (VSL)](#serialization-language-vsl)
  - [UI Components](#state-machine)
  - [Inspector](#inspector)
  - [Scratchpad](#scratchpad)

## Introduction
A collection of tools for developing applications in Unity.

## Installation
- On the top right corner of this Github Page select the "Code" dropdown
- Copy the HTTPS link listed.
- In the Unity Editor open the Window -> Package Manager.
- Select the + in the top left and select "Add package from git URL".
- Paste the HTTPS link and select add.
- Updates can be managed in the Unity Package Manager from this point.

## Usage

### Gameplay Tags & Data Registry
The backbone of many of the systems. A `GameplayTag` is a stable `uint` — an xxHash32 of a hierarchical dotted
name such as `Ability.Fire.Burn` — and it is the lowest-level identifier the rest of the SDK is keyed on.

#### How To Use

Content and identifiers are registered as `IData` into the [GlobalDataRegistry](./Runtime/Data%20Registry/GlobalDataRegistry.cs),
keyed by that same `uint`. Register data either from code by implementing `IDataRegistry.BuildRegistry()`, or
from an `[IsAddressable]` `IScriptableData` scriptable object that self-registers. Tags specifically are
registered as `GameplayTagData` through any `IGameplayTagRegistry`.

Because every registered name hashes into the same key space, a `GameplayTag`, a data key, and any `IData.Key`
are interchangeable `uint`s. Look content up with `DataRegistry<T>.Get(...)`, and pick tags/keys in the
inspector with a `GameplayTag` field (a searchable, hierarchical picker) or a `[Dropdown("Category", Category)]`
filter — both source their options from the registry.

- [GameplayTag](./Runtime/Gameplay%20Tags/GameplayTag.cs): The core identifier struct.
- [GlobalDataRegistry](./Runtime/Data%20Registry/GlobalDataRegistry.cs) / [DataRegistry&lt;T&gt;](./Runtime/Data%20Registry/DataRegistry.cs): Registration and typed lookup over `IData`.
- [KeyGenerator](./Runtime/Keys/Generation/KeyGenerator.cs): Optionally emits `const uint` helper classes from the registry for compile-time-safe references; the `.tsv` manifests it emits also power IDE key/tag autocomplete.

### Gameplay Events
A `GameplayTag`-keyed, netcode-aware event and service-locator layer.

#### How To Use

[GameplayEvents](./Runtime/Gameplay%20Events/GameplayEvents.cs) is a global publish/subscribe bus keyed by
`GameplayTag`. Subscribe with `GameplayEvents.Subscribe(tag, callback)` and raise with
`GameplayEvents.TriggerEvent(tag, data)`. There are channel-scoped and per-entity overloads, plus
server/client-only variants. Event payloads implement `IGameplayEventData`, and `ValueGameplayEventData<T>`
covers simple value payloads.

[GameplayServices](./Runtime/Gameplay%20Services/GameplayServices.cs) is the provider / service-locator half:
register a component or service under a `GameplayTag` (`GameplayServices.Register(tag, this)`, usually in
`OnEnable`) and fetch it from anywhere with `GameplayServices.Get<T>(tag)` / `TryGet`, dependency-injection
style, without a hard reference.

### Observable Values
A wrapper on primitive types and some core Unity types that track when values are changed and optionally fires events when they are. 
Also contains a system for tying these values to a larger Observable Class to allow for grouped tracking of data.
They can also automatically be serialized to Json for easy save functionality.

#### How To Use
Usage is on a user desired basis. Where the user wants to have a tracked value replace the primitive value with its Observable.
```csharp
public class ExampleHealth : MonoBehaviour
{
    private const int HealthFieldID = 1;

    private FloatObservable _currentHealth;

    private void Awake()
    {
        _currentHealth = new FloatObservable(HealthFieldID, true, 100);
        _currentHealth.ValueChanged += CurrentHealthOnValueChanged;
    }

    private void CurrentHealthOnValueChanged(FloatObservable value, float oldValue)
    {
        Debug.Log($"Old Value: {oldValue} | New Value {value.Value}");
    }
}
```

### Serialization Language (VSL)
Moved to its own package: **[Vapor Serialization Language](https://github.com/Tyrant117/vapor-serialization-language)**.
Core depends on it, and every document the Data Registry reads or writes is a `.vsl` file.

Core adds two formatters of its own to it, because VSL depends on nothing in Vapor and so cannot ship
them itself:

- `GameplayTagFormatter` / `GameplayTagContainerFormatter` — write a tag as its dotted name rather
  than its hash, so `Ability.Fire.Burn` is what lands in the document.
- `VslRefFormatter<T>` — writes a [`VslRef<T>`](./Runtime/Data%20Registry/VslRef.cs) as the target
  entry's name, resolving lazily through `GlobalDataRegistry`.

Both are installed by [VaporVslFormatters](./Runtime/VaporVslFormatters.cs), which the registry calls
through the `[assembly: VslFormatterProvider]` declared in `Runtime/AssemblyInfo.cs`.

### UI Components
A simple Mantine-like component library for UI Toolkit.

### Inspector
An Odin-like custom inspector system fully running in the new Unity UI Toolkit. The backbone of custom drawers for the rest of the SDK.

#### How To Use
- Decorate the MonoBehaviour you want to draw with custom attributes.
- With the script selected in the project go to Tools -> Vapor -> Inspector -> Create Inspectors From Selection.
- This will populate your local Vapor/Editor/Inspector folder with the custom drawer for the MonoBehaviour.

### Scratchpad
An editor-side review loop for work an AI assistant just delivered. Editor only.

#### How To Use

The assistant writes a **handoff** — a `.vsl` file describing what it changed and why — into
`Assets/Vapor/Editor/Scratchpad/<Feature>/`. Open `Vapor → Scratchpad` and hit **Refresh** to read
it: each change lists its summary, its reasoning and the risk the assistant flagged. Annotate a
change with a **Comment**, an **Issue** or a piece of **Work**, mark how far you have reviewed it,
and hit **Copy Prompt** — the clipboard gets your notes with the change they are about quoted in
full, ready to paste into the next chat.

The next handoff lists the note ids it addressed in its `resolved:` field, and the window closes
them. That is the whole round trip; nothing else has to be kept in step by hand.

**Copy Contract** puts the writing instructions on the clipboard, prefilled with the current feature,
the exact path to write to, and everything still open on it — enough to start a fresh chat with.
`Ctrl+Alt+S` opens a quick-capture popup for a note you want to write without leaving what you are
doing, and it can attach a console entry or whatever is selected.

The assistant owns the handoff file and the window never writes it; the window owns the sibling
`.notes.vsl` and the assistant never reads it. Sessions that have been fully reviewed and fully
closed archive themselves after twelve hours, lazily, when the window is opened.

- [HANDOFF-SPEC.md](./Editor/Scratchpad/HANDOFF-SPEC.md): the format, and the document to hand an AI.
- [ScratchpadWindow](./Editor/Scratchpad/Window/ScratchpadWindow.cs): the window itself.
- [ScratchpadStore](./Editor/Scratchpad/Services/ScratchpadStore.cs): all of the disk access, including the archive rule.
- [ScratchpadPromptBuilder](./Editor/Scratchpad/Services/ScratchpadPromptBuilder.cs): what `Copy Prompt` puts on the clipboard.
