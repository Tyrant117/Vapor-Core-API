# Scratchpad handoff format

A **handoff** is a file an assistant writes at the end of a piece of work, describing what it
changed and why. The Vapor Scratchpad window (`Vapor → Scratchpad`) renders it so the changes can be
read and commented on, and turns those comments back into a prompt for the next session.

This file is the contract. It is what the window's **Copy Contract** button summarises.

---

## 1. Where the file goes

```
Assets/Vapor/Editor/Scratchpad/<Feature>/<yyyy-MM-dd-HHmm>.handoff.vsl
```

- **`<Feature>`** is a folder name, and it is the feature. Create the folder if it does not exist;
  never rename an existing one to fit a new session.
- **The stamp** is when the work finished, e.g. `2026-08-31-1430`. It is the session's identity.
  If that exact file already exists, append `-2`, `-3`, and so on.
- Beside it the window keeps `<stamp>.notes.vsl`. **That file is the human's. Never read it, never
  write it.** Nothing you need is in there — anything being asked of you arrives in a pasted prompt.
- `index.vsl` at the root is a cache the window rebuilds. Leave it alone.

Write one handoff per meaningful chunk of work, not per file edited and not per message.

---

## 2. The format

VSL — the serialization language in this package. Three things matter for writing one:

- **Commas are whitespace.** Writing them or not writing them both parse. So do newlines.
- **Members you leave out keep their default**, and a member the reader does not recognise is
  skipped rather than failing. A partial handoff loads; a slightly wrong one still loads.
- **`"""` opens a raw multi-line block.** No escaping inside it. Use it for anything longer than a
  phrase.

Full language spec: [SPEC.md](../../../Vapor%20Serialization%20Language/Runtime/SPEC.md).

---

## 3. The schema

The `#` lines are the field documentation, emitted from the model itself. This is exactly what
**Copy Contract** puts on the clipboard.

```
@vsl 1

{
  # Which feature this session belongs to. Must match the containing folder name.
  feature: "UV Editor"

  # One line: what this session set out to do.
  title: "Pin round-trip and the SLIM toggle"

  # A few sentences of context for the whole session.
  summary: """
    Pins were being dropped by extrude and dissolve. Fixed both, and put SLIM
    behind a toggle that defaults on.
    """

  # ISO-8601 local timestamp this session was written, e.g. 2026-08-31T14:30:00.
  written: "2026-08-31T14:30:00"

  # Note ids from earlier sessions that this session addressed. The editor closes them.
  resolved: [ "uv-editor-17"  "uv-editor-19" ]

  # One entry per meaningful change. This is what gets commented on.
  changes: [
    {
      # Short stable slug, unique within this file. Notes attach to it, so do not reuse
      # a slug for a different change.
      id: "pin-roundtrip"

      # One line naming the change.
      title: "Pins survive an extrude"

      # What actually changed, in a sentence or three.
      summary: """..."""

      # Why it was done this way, and what was considered and rejected.
      rationale: """..."""

      # What is uncertain, untested, or deliberately cut.
      risk: """..."""

      # Project-relative paths touched. May be empty.
      files: [ "Assets/Vapor Modular Characters/Editor/Forge/Edit/UvEditorCanvas.cs" ]
    }
  ]

  # Work this session deliberately left undone. Arrives as proposed, and is accepted
  # or dismissed in the editor.
  followUps: [
    {
      id: "split-canvas"
      title: "Split UvEditorCanvas.cs into partials"
      detail: """It is ~4800 lines and getting hard to navigate."""
    }
  ]

  # Tests covering this work, as namespaces, fixtures or single test names. The editor
  # runs these on a button and files failures as issues.
  tests: [ "Vapor.Tests.Uv.PinTests" ]
}
```

Only `feature`, `title` and `changes` carry any weight. Everything else is optional, and a handoff
without it renders fine.

There is one field not to write: `placeholder`. The window sets it on empty sessions it creates
itself so notes have somewhere to live before any real handoff has landed.

---

## 4. What actually gets read

The three fields the reviewer spends their attention on:

**`rationale`** — why this way. A list of edited files says what happened and invites nothing back.
A stated reason is something that can be agreed or disagreed with, which is the only thing that
makes a review worth doing.

**`risk`** — what is untested, uncertain, or cut. Say it plainly. **A blank `risk` reads as a claim
that there isn't one**, so leaving it empty to look tidy is worse than useless. "The extrude path is
covered by a test, the dissolve path is not" is exactly the sentence worth writing.

**`followUps`** — work you knowingly left. These arrive greyed out as *proposed* and stay that way
until accepted, so nothing files itself into someone else's backlog. Putting deferred work here
rather than burying it in prose is what lets it be triaged instead of merely read.

**`tests`** — what covers this work. The feature's test set is the union of this field across its
sessions, so naming a fixture once keeps it running for every later session too. The reviewer runs
them from a button, and anything that fails is filed as an Issue carrying its message and stack — so
a test named here is a failure that reports itself, rather than one somebody has to retype.

---

## 5. The round trip

The reviewer annotates changes with **Comments**, **Issues** and **Work**, and each note gets an id
like `uv-editor-17`. Copying a prompt hands those notes back with the change they are about quoted
in full.

When you act on one, **name its id in the next handoff's `resolved:` list.** That is the only thing
that closes it — the window has no other way to know. Ids arrive in the prompt and are listed again
at the end of it.

```
resolved: [ "uv-editor-17"  "uv-editor-19" ]
```

Naming an id you did not actually address is worse than naming none: it closes a note that is still
outstanding, and it will not come back on its own.

---

## 6. Getting it wrong

Most mistakes are survivable, by design:

| Mistake | What happens |
|---|---|
| Malformed VSL | Unity reports it against the file on import; the session still lists, showing the parse error |
| Unknown member | Skipped |
| Missing optional member | Keeps its default |
| A change with no `id` | The window invents and remembers one |
| `feature:` disagrees with the folder | The folder wins; the window logs a warning |
| A `!Tag` in front of an object | Tolerated |
| Duplicate change `id` in one file | The second gets an invented id; a warning is logged |
| Writing to the `.notes.vsl` | Nothing good. Don't. |
