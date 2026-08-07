# Vapor Serialization Language — Specification v1

VSL is a text serialization format for Unity. Its primary author is an AI; its secondary reader is a
human. Every design decision below trades a small amount of machine convenience for a large amount of
generation reliability and legibility.

```
@vsl 1

!Player {
  # display name shown in the HUD
  name: "Aria"
  level: 12
  health: 87.5
  position: (0, 1.5, -3)
  tint: 0xFF8800FF
  owner: @9241421688590303745        # EntityId reference
  tags: [ Ability.Fire.Burn  Status.Stunned ]
  inventory: [
    { id: "sword"  count: 1 }
    { id: "potion"  count: 3 }
  ]
  ability: !FireballAbility { damage: 25 }
  bio: """
    Multi-line raw text.
    No escaping needed.
    """
}
```

---

## 1. Lexical structure

### 1.1 Whitespace and separators

Space, tab, carriage return, newline **and comma** are all whitespace. There are no mandatory
separators anywhere in the language — structure is carried entirely by `{}`, `[]`, `()` and `:`.

This means all three of these are the same document:

```
{ a: 1  b: 2 }
```
```
{
  a: 1
  b: 2
}
```
```
{a:1,b:2}
```

Commas are legal *everywhere* and mean nothing. A model that emits JSON-style commas out of habit
produces a valid VSL document. A model that omits them also produces a valid document. This removes
the single most common structural error in machine-generated data formats.

### 1.2 Comments

`#` begins a comment that runs to end of line. A comment is legal anywhere a token may begin.

```
# leading comment
{
  a: 1   # trailing comment
  # comment between members
  b: 2
}
```

Comments are not preserved across a read/write round trip. They are emitted on write from
`[VslComment]` metadata.

### 1.3 Identifiers

```
ident := [A-Za-z_] [A-Za-z0-9_.]*
```

Dots are part of an identifier, so `Ability.Fire.Burn` is a single token. Identifiers are used for
member names, enum members, gameplay tags, type tags, and the keywords `true`, `false`, `null`.

### 1.4 Numbers

```
number := '-'? digit+ ('.' digit+)? ([eE] [+-]? digit+)?
hex    := '0x' hexdigit+
```

`inf`, `-inf` and `nan` are recognized for floating-point targets. All parsing and formatting uses
the invariant culture. Floats and doubles are written round-trip exact (`"R"`).

Hex literals are unsigned and may carry a leading `0x` of any width. They are the canonical form for
colors and bitmasks.

### 1.5 Strings

Quoted strings use `"` with the escapes `\" \\ \n \r \t \0 \uXXXX`.

Raw strings use `"""` and contain no escapes:

```
bio: """
  Line one.
  Line two "with quotes" and \backslashes.
  """
```

The newline immediately after the opening `"""` is dropped, and the indentation of the closing `"""`
line is stripped from every content line — the same rule as C# raw string literals. This lets
multi-line text sit at the natural indentation of the document without polluting the value.

Where a string is expected, a bare identifier is also accepted: `name: Aria` reads as `"Aria"`. This
is a read-side leniency only; the writer always quotes strings.

---

## 2. Values

```
document  := header? value
header    := '@vsl' integer
value     := scalar | object | sequence | tuple | reference | typed
typed     := '!' ident value?
object    := '{' member* '}'
member    := ident ':' value
sequence  := '[' value* ']'
tuple     := '(' value* ')'
reference := '@null'
           | '@' uint64
           | '@' '(' uint64? source string ')'
           | '@' source string
source    := 'resource' | 'addressable'
scalar    := string | rawstring | number | hex | 'true' | 'false' | 'null' | flags
flags     := ident ('|' ident)*
```

The `@vsl 1` header is optional on read and always emitted on write.

### 2.1 Objects

`{ }` carries both typed objects and dictionaries. Which one it is comes from the target type, not
the syntax. Member order is not significant on read; on write it follows declaration order.

A member whose name does not exist on the target type is skipped, not an error. A member of the
target type absent from the text keeps its existing value. Both rules exist so partially-specified
AI output loads successfully.

### 2.2 Sequences

`[ ]` carries arrays, lists, sets, queues, stacks, and dictionaries with non-string keys.

### 2.3 Tuples

`( )` carries fixed-arity values — vectors, quaternions, rects, bounds, keyframes,
`KeyValuePair`, `ValueTuple`. Tuples are always written on one line. A `Vector3` costs one line and
about nine tokens instead of five lines and twenty-five.

### 2.4 Type tags

`!Name` before a value selects a concrete type for a polymorphic slot.

```
ability: !FireballAbility { damage: 25 }
```

Resolution order: a name registered via `[VslType("Name")]`, then a type whose short name matches
among the declared type's subclasses, then a full type name. The writer emits a tag only when the
runtime type differs from the declared type, and prefers the shortest form that resolves.

### 2.5 References

A `UnityEngine.Object` member is written as a reference, never by value — serializing it inline would
either duplicate an asset or recurse through the whole scene.

| Form | Means |
| --- | --- |
| `@null` | no reference |
| `@9241421688590303745` | an `EntityId`, valid only in the writing session |
| `@(9241421688590303745, resource, "UI/Icons/Sword")` | that id, plus how to load it in a build |
| `@(addressable, "Boss_Fireking")` | a durable locator with no id |
| `@resource "UI/Icons/Sword"` | the same, without brackets |
| `@(addressable, "Characters/Hero[Run]")` | a named object inside that asset |

The two halves answer different questions, which is why both are written.

**`EntityId`** is Unity's instance-id successor. It is exact and instant, and it covers scene objects
and runtime instances that are not assets at all — but it does not survive a domain reload, a scene
reload, or an application restart.

**The locator** — `resource` plus a `Resources.Load` path, or `addressable` plus an Addressables key
— survives anything, but only exists for assets that actually ship: something under a `Resources`
folder, or something marked addressable. A scene object has no locator, and gets an id only.

Writing both means a document written in the editor still loads in a player. Reading tries the id
first, because when it works it is free and exact, then falls back to loading by locator.

An asset that is neither under `Resources` nor addressable gets an id only, and will not resolve in a
build — that is a property of the project, not of the format. Put it in one of the two places if the
reference has to survive.

### Sub-assets

A locator's key may name an object *inside* an asset, as `key[Name]`. An `AnimationClip` imported as
part of a model is the common case: the file is what ships, but the clip is what the reference means,
and the path alone would load the model instead.

The bracket form is Addressables' own convention, so an addressable key written this way needs no
special handling on read. The same form is used for `Resources`, where the path is scanned and matched
by name and type together — a model holding a dozen clips, a mesh and a material can satisfy either
check alone with the wrong object.

The name is the sub-asset's own, which is unique within its file by construction. A path that itself
contains brackets is fine: the sub-asset is the last bracketed group.

`res` and `addr` are accepted as short forms on read. A bare string in a reference slot —
`icon: "UI/Icons/Sword"` — is read as a Resources path, which is the most likely thing a
hand-written document means by it.

---

## 3. Type mapping

### 3.1 Unity math

| Type | Form | Example |
| --- | --- | --- |
| `Vector2` | `(x, y)` | `(1, 2)` |
| `Vector3` | `(x, y, z)` | `(0, 1.5, -3)` |
| `Vector4` | `(x, y, z, w)` | `(0, 0, 0, 1)` |
| `Vector2Int` | `(x, y)` | `(3, 4)` |
| `Vector3Int` | `(x, y, z)` | `(3, 4, 5)` |
| `Quaternion` | `(x, y, z, w)` | `(0, 0.7071, 0, 0.7071)` |
| `Matrix4x4` | `[ 16 floats, row major ]` | `[ 1 0 0 0  0 1 0 0  0 0 1 0  0 0 0 1 ]` |

### 3.2 Color

| Type | Form | Example |
| --- | --- | --- |
| `Color` | `0xRRGGBBAA` | `0xFF8800FF` |
| `Color32` | `0xRRGGBBAA` | `0xFF8800FF` |

`Color` also accepts `(r, g, b, a)` with float components, and the writer falls back to that form
whenever hex would lose information — an HDR value outside 0–1, or simply one that is not on an
8-bit boundary, since `0.5` is `127.5/255`. Hex is used only when it is exact, so a colour picked in
the editor stays readable and a computed colour still round-trips bit-for-bit. `Color32` is always
hex, being 8-bit by construction.

| Type | Form |
| --- | --- |
| `Gradient` | `{ mode: Blend  colors: [ (0xFF8800, 0) (0x0088FF, 1) ]  alphas: [ (1, 0) (0, 1) ] }` |
| `GradientColorKey` | `(color, time)` |
| `GradientAlphaKey` | `(alpha, time)` |

### 3.3 Unity geometry

| Type | Form |
| --- | --- |
| `Rect`, `RectInt` | `(x, y, width, height)` |
| `Bounds` | `(centerX, centerY, centerZ, extentsX, extentsY, extentsZ)` |
| `BoundsInt` | `(positionX, positionY, positionZ, sizeX, sizeY, sizeZ)` |

Each uses whatever the struct itself stores — extents for `Bounds`, position and size for
`BoundsInt` — so nothing is derived and nothing drifts on a round trip.

### 3.4 Unity scripting

| Type | Form | Notes |
| --- | --- | --- |
| `LayerMask` | `0x00000020` or `[ Water  Player ]` | writes hex; reads either |
| `RenderingLayerMask` | `0x00000001` | |
| `AnimationCurve` | `{ preWrap: Clamp  postWrap: Clamp  keys: [ ... ] }` | |
| `Keyframe` | `(time, value, inTangent, outTangent)` | extended to `(..., inWeight, outWeight, weightedMode)` only for a weighted key |
| `Hash128` | `"0123456789abcdef0123456789abcdef"` | |
| any `UnityEngine.Object` | `@<uint64>` / `@null` | §2.5 |

### 3.5 Vapor

| Type | Form | Notes |
| --- | --- | --- |
| `GameplayTag` | `Ability.Fire.Burn` | the dotted name, not the hash |
| `GameplayTagContainer` | `[ Ability.Fire.Burn  Status.Stunned ]` | |

A `GameplayTag` is a `uint` xxHash32 of its name. Writing the name is what makes tags legible and
editable; the hash is recovered on read. An unregistered name still hashes correctly, so a tag
authored by an AI resolves even if it was never added to the tag tree. A tag whose name cannot be
recovered on write falls back to its raw `uint`.

### 3.6 .NET

| Type | Form |
| --- | --- |
| `T[]`, `List<T>` | `[ ... ]` |
| `HashSet<T>`, `Queue<T>`, `Stack<T>`, `LinkedList<T>` | `[ ... ]` |
| `Dictionary<K,V>` with string-like `K` | `{ key: value }` |
| `Dictionary<K,V>` otherwise | `[ (key, value) ... ]` |
| `KeyValuePair<K,V>` | `(key, value)` |
| `ValueTuple<T1,T2>`, `ValueTuple<T1,T2,T3>` | `( ... )` |
| `Nullable<T>` | the value, or `null` |
| `enum` | member name, or `A \| B` for `[Flags]`, or a number |
| `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, `Uri`, `Version` | quoted round-trip string |
| primitives, `string`, `decimal` | native |

Collection *interfaces* — `IList<T>`, `IEnumerable<T>` — are not supported, matching Unity's own
serializer. Declare the concrete type.

A `Stack<T>` is written top-first, the order it enumerates, and pushed back in reverse on read so
the original top stays on top.

"String-like" keys are `string`, `enum`, and integral types — anything that renders as a legal
identifier or number. `Dictionary<string, T>` therefore reads as a plain object, which is both the
most common case and the most legible one.

---

## 4. Member selection

Serialization is **opt-in**.

```csharp
public class Enemy
{
    [VslSerialize] public int Level;               // serialized
    public int Ignored;                            // skipped
    [VslSerialize] public int Score { get; set; }  // serialized
}
```

A type-level attribute switches the whole type to Unity's own rules — public fields plus non-public
fields carrying `[SerializeField]`:

```csharp
[VslSerializable]
public class Player
{
    public int Level;                  // serialized  (public field)
    [SerializeField] private float _hp;// serialized  ([SerializeField])
    private string _cache;             // skipped     (plain private)
    public int Computed => Level * 2;  // skipped     (no setter)
    [VslIgnore] public int Temp;       // skipped     (explicit opt-out)
    [VslSerialize] private int _extra; // serialized  (explicit opt-in)
}
```

| Attribute | Target | Effect |
| --- | --- | --- |
| `[VslSerialize]` | field, property | Include this member. Properties need a getter and a setter. |
| `[VslSerializable]` | class, struct | Apply Unity's rules to every member of the type. |
| `[VslIgnore]` | field, property | Exclude, overriding any type-level policy. |
| `[VslName("hp")]` | field, property | Use this name in text instead of the C# name. |
| `[VslType("Fireball")]` | class, struct | Register the `!tag` used for this type. |
| `[VslComment("...")]` | field, property | Emit `# ...` above the member on write. |

`[VslComment]` is the format's schema-documentation channel. Serialized output doubles as a
self-describing template, which is what makes a VSL file a good prompt for generating more VSL:

```
!Player {
  # 0-1, drives the HUD bar
  healthFraction: 0.62
}
```

### Member naming

On **write**, a member name has any `_` or `m_` prefix stripped and its first letter lowered, so
`Name`, `_health` and `m_Speed` are written `name`, `health` and `speed`. Without this a document
would mix conventions according to how each field happened to be declared — the kind of
inconsistency that makes a format harder to generate against. An acronym is left alone (`ID` stays
`ID`), and `[VslName]` overrides the result exactly. If normalising would collide with another
member, that member keeps its declared name.

On **read**, names are matched case-insensitively and the same prefixes are ignored, so `_hp`, `hp`,
`HP` and `m_Hp` all bind to the same member. This is deliberate leniency for hand- and
machine-authored input.

---

## 5. Object references

```csharp
public interface IVslReferenceResolver
{
    bool TryGetReference(Object obj, out VslObjectReference reference);
    bool TryResolve(in VslObjectReference reference, Type expectedType, out Object obj);
}
```

The default is `VslObjectReferenceResolver`. On write it records the `EntityId` and asks
`VslAssetLocator` for a durable locator; on read it tries the id first, then loads by locator.

### 5.1 Where locators come from

Finding an asset's Resources path or Addressables address needs `AssetDatabase` and the Addressables
settings, so it can only happen in the editor. `VslEditorAssetLocator` installs itself into
`VslAssetLocator.Provider` on domain load and does that work. A player has no provider and loads
through `Resources.Load` and `Addressables` directly.

Resources is preferred when an asset qualifies for both: it needs no build step and no package, so it
is the more dependable of the two.

A component is located through the prefab that carries it — the prefab is what loads, and the
component is narrowed out of it afterwards.

Anything loaded by locator is remembered, so a document read at runtime and written back out keeps
its durable keys instead of degrading to session-only ids.

### 5.2 Supplying your own ids

`VslReferenceTable` maps ids to objects from a caller-supplied table. Use it when neither an
`EntityId` nor an asset locator is the right key — a networked object graph, a save format with its
own stable ids, or a test that needs deterministic rebinding:

```csharp
var table = new VslReferenceTable();
table.Register(1, swordAsset);
var ctx = new VslContext(VslOptions.Default) { References = table };
var player = Vsl.Deserialize<Player>(text, ctx);
```

Ids in the table win; anything else falls through to the default resolver.

---

## 6. Error handling

A syntax error throws `VslException` carrying line, column, and the offending token. Parsing is
line-oriented enough that the reported position is the actual mistake, not the end of the document.

Semantic mismatches are *not* errors by default: an unknown member is skipped, a missing member keeps
its default, and an unresolvable reference becomes `null`. `VslOptions.Strict` turns each of these
into a `VslException` instead. The lenient default suits machine-authored input; strict mode suits
tests and validation tooling.

---

## 7. Layout on write

Deterministic, so that golden-file tests are meaningful:

- Indent is two spaces.
- Tuples are always one line.
- A sequence is written inline when it holds no nested objects or sequences and fits the inline
  width (default 60 columns); otherwise one element per line.
- An object is written inline under the same rule, and only when its type is marked inline-preferring
  (at most three members, all scalars).
- Empty containers are `{}` and `[]`.
