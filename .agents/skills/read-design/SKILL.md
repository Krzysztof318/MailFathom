---
name: read-design
description: Use before building, changing, or reviewing any client screen — it refreshes the local mirror of the design project and points at the state inventory a screen is built from.
license: AGPL-3.0-only
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/Krzysztof318/MailFathom
---

# Read Design

The client is built UX-first, and what a screen looks like is settled in a design project read
through the `claude-design` MCP server. This skill is how that project is **read**. It is never
written to: not a file, not a screen, not a one-word fix to a label that is provably wrong. A
disagreement between the design and the code is resolved by changing the code and handing the owner
the exact correction — the design is theirs to edit, by hand, because what makes a source of truth
one is that a single person writes it.

## The source is the inventory, not a preview

A rendered preview shows the one state it was clicked into. The source states every screen, every
variant and every reaction at once, and most of a screen's states — the empty one, the failing one,
the one that only exists under `pointer: coarse`, the one no click reaches at all — are invisible in
a picture. A screen built from a screenshot is built from a partial reading of a complete document.

So the reading order is: **`artifacts/design/state-inventory.md` first**, the mirrored source under
`artifacts/design/files/` when the inventory is not specific enough, and a preview last and only to
settle something the source genuinely does not say. The inventory names the etag set it was
extracted from, so it says for itself whether it still describes the current design.

Nothing under `artifacts/` is repository content. It is gitignored, it is never staged, and no file
describing what a screen looks like is committed. The mirror is copied into a linked worktree by
`.worktreeinclude`, so a worktree does not re-fetch what the main checkout already holds.

## Refreshing the mirror

Four steps, and an unchanged project stops at the second.

1. **Resolve the project.** `mcp__claude-design__list_projects` returns it. Nothing in the repository
   names its address, and nothing that a session writes may — not a commit, an issue, a pull request,
   a log, or a file.

   Its tools are deferred: `ToolSearch` with
   `select:mcp__claude-design__list_projects,mcp__claude-design__list_files,mcp__claude-design__read_file`
   loads the schemas first. A session with no `claude-design` server configured cannot do any of
   this — say so and work from the mirror already on disk, naming its stamp.

2. **List, and compare.** One `list_files` at `depth: -1` returns every file's path, size and opaque
   `etag` with no content read at all. Write that JSON to a scratch file of your own — never into
   `artifacts/`, which holds the mirror, the manifest and the inventory and nothing else — and:

   ```bash
   bash scripts/design-mirror.sh plan "$WORK_DIR/listing.json"
   ```

   It prints one line when nothing moved, and otherwise names what appeared, what changed, what went,
   and which screen sources have to be read. **An unchanged project costs exactly this** — a listing
   and a comparison, no `read_file` at all.

   The comparison is the server's own `etag`, never a hash computed here: computing one would mean
   reading the file the check exists to avoid reading.

3. **Read only what moved.** For each path the plan named, `read_file` it. Two shapes, and the cheap
   one is the common one:

   - **A result too large to return inline** is written to a file by the harness, which names the
     path in its message. Those bytes never pass through the model, so this is the honest way to copy
     a large file. A file past the server's 256 KiB per-call cap comes back as several windows —
     read it with `offset` and `limit`, then name every saved result in order:

     ```bash
     bash scripts/design-mirror.sh extract "artifacts/design/files/<path>" <saved> [<saved>...]
     ```

   - **A small result comes back inline**, HTML-entity-escaped so it cannot close its own wrapper.
     Write the escaped body to the mirrored path exactly as it arrived — copying, never re-typing —
     and then:

     ```bash
     bash scripts/design-mirror.sh decode "artifacts/design/files/<path>"
     ```

   The mirror copies every `.html` screen source and `support.js`, the generated runtime that boots
   them — without it an artboard renders nothing at all, which is what `scripts/capture-design.sh`
   needs it for. The project's assets and its thumbnail are not read: they are images rather than
   text, carrying several megabytes of them into every worktree buys nothing, and a screen is built
   from the source rather than from them. The manifest covers **every** file, so one appearing or
   disappearing is still visible.

4. **Record.** `bash scripts/design-mirror.sh record "$WORK_DIR/listing.json"` writes the manifest
   and checks every mirrored file against the byte count the project states. A mismatch is a
   transcription that dropped or doubled a window, and it is a failure rather than a warning. It
   prints the new stamp.

Then re-extract the inventory for whatever moved, and put the new stamp at the top of it. An
inventory whose stamp disagrees with `bash scripts/design-mirror.sh stamp` is describing an older
design, which is the one failure this whole arrangement exists to make visible.

## The design half of the parity pairing

`artifacts/design/parity.json` sits beside the inventory and holds the other thing a mirror is read
for: which artboard each screen in `frontend/design-parity/screens.json` is drawn on, the component
properties it takes, and what is pressed to reach the state that manifest names. It is what
`scripts/capture-design.sh` reads, and it lives here rather than in the tree because every one of
those is design content — the manifest in `frontend/` describes the client and carries none.

It records the stamp it was written against, and the capture script refuses a stamp that disagrees
with the mirror's. So a refresh that moves a screen source is not finished until this file has been
read against what moved, exactly as the inventory is:

```json
{
  "stamp": "<the stamp record printed>",
  "screens": {
    "<screen id from the client manifest>": {
      "file": "<mirrored artboard>",
      "properties": { "theme": null },
      "steps": [{ "click": { "text": "<what is pressed>" } }]
    }
  }
}
```

`theme` is set to `null` rather than left out, because an artboard declaring a default theme would
otherwise be captured in it whatever the browser was told — and both sides of a pair are captured
under one `prefers-color-scheme`. Beyond that, a step names an element by its `text`, or by `role`
and `name` where the artboard has an accessibility tree; `nth` picks one of several.

## What the inventory holds

Every screen, the states it has, what reveals each one, and the controls that react — written so it
is read instead of the prototype. Its last section is the one that has to be there: **the states the
source has and a preview does not reach.** A gate whose condition is a constant, a state behind a
component property, an empty state no mock record produces, a composition only a width reaches. Each
of those is a screen somebody would otherwise not know they owed, and a screenshot reports none of
them.

A state the source gates but nothing draws is not a screen to invent. It is a gap in the design, and
it goes to the owner as a correction to make in the project — never into the client as a guess.

## What this skill does not do

- It never writes to the design project. `write_files`, `copy_files` and `delete_files` are not
  called, whatever a task's acceptance says and however small the change looks.
- It takes no screenshots. Holding a running screen against the design is its own step and reads this
  mirror rather than replacing it: `scripts/capture-design.sh`, `scripts/capture-client.sh` and
  `scripts/compare-captures.sh`, in that order, with `frontend/AGENTS.md` § *Holding a screen against
  the design* as the rule.
- It stages nothing. `artifacts/` is ignored, and a change that puts a design file in the tree is the
  defect this arrangement is built to prevent.
- It does not run in the fork role. The project belongs to the owner and is reached through a server
  nobody else is connected to, so a session that finds no design server there has found the current
  state rather than a broken configuration: say so, and settle what the screen should do in the issue
  instead of designing one in the session.
