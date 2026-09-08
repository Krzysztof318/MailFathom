---
name: read-design
description: Use before building, changing, or reviewing any client screen — it points at the state inventory a screen is built from and says when the mirror it stands on is out of date.
license: AGPL-3.0-only
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/Krzysztof318/MailFathom
---

# Read Design

The client is built UX-first, and what a screen looks like is settled in a design project the maintainer owns. That
project is not public and will not become one, so what stands in for it here is `design/` — the project's screen
sources copied byte for byte, the manifest of its own etags, the state inventory extracted from them, and the pairing
the parity loop reads. This skill is how that is **read**.

The project itself is never written to: not a file, not a screen, not a one-word fix to a label that is provably wrong.
A disagreement between the design and the code is resolved by changing the code and handing the owner the exact
correction — the design is theirs to edit, by hand, because what makes a source of truth one is that a single person
writes it. `$mf-sync-design` is the one skill that touches the mirror, and it only ever copies the project into the tree.

## The source is the inventory, not a preview

A rendered preview shows the one state it was clicked into. The source states every screen, every variant and every
reaction at once, and most of a screen's states — the empty one, the failing one, the one that only exists under
`pointer: coarse`, the one no click reaches at all — are invisible in a picture. A screen built from a screenshot is
built from a partial reading of a complete document.

So the reading order is: **`design/state-inventory.md` first**, the mirrored source under `design/files/` when the
inventory is not specific enough, and a preview last and only to settle something the source genuinely does not say.

Everything under `design/` is tracked, which is what makes this skill one anybody can run. A contributor with no access
to the design project reads exactly what a maintainer's session reads, and `scripts/capture-design.sh` serves the same
mirror in either clone.

## Whether the mirror still describes the current design

The inventory names the etag set it was extracted from, so it says for itself whether it is current:

```bash
bash scripts/design-mirror.sh stamp
```

An answer that disagrees with the **Mirror stamp** at the top of `design/state-inventory.md` means the two landed out of
step, and it is a defect in the tree rather than something to read around — say so rather than trusting either file.

Whether the *project* has moved since is a separate question, and only a session connected to the `claude-design` MCP
server can ask it. That is `$mf-sync-design`, which lists the project, reads whatever moved, and opens a pull request
carrying it. This skill never reaches that server, so a screen built here is built against the mirror the branch holds,
and the stamp is what says which design that was.

## The design half of the parity pairing

`design/parity.json` sits beside the inventory and holds the other thing the mirror is read for: which artboard each
screen in `frontend/design-parity/screens.json` is drawn on, the component properties it takes, and what is pressed to
reach the state that manifest names. It is what `scripts/capture-design.sh` reads, and it lives here rather than beside
the client manifest because every one of those is a fact about the design — the manifest in `frontend/` describes the
client and carries none.

It records the stamp it was written against, and the capture script refuses a stamp that disagrees with the mirror's:

```json
{
  "stamp": "<the stamp the mirror stands on>",
  "screens": {
    "<screen id from the client manifest>": {
      "file": "<mirrored artboard>",
      "properties": { "theme": null },
      "steps": [{ "click": { "text": "<what is pressed>" } }]
    }
  }
}
```

`theme` is set to `null` rather than left out, because an artboard declaring a default theme would otherwise be captured
in it whatever the browser was told — and both sides of a pair are captured under one `prefers-color-scheme`. Beyond
that, a step names an element by its `text`, or by `role` and `name` where the artboard has an accessibility tree; `nth`
picks one of several, and `exact` holds a `name` to the whole accessible name rather than a substring of one — which a
name that is also the tail of another control's name needs, *Ustawienia* being a menu item and the end of *Konto i
ustawienia*, the control that opens the menu it is in.

## What the inventory holds

Every screen, the states it has, what reveals each one, and the controls that react — written so it is read instead of
the prototype. Its last section is the one that has to be there: **the states the source has and a preview does not
reach.** A gate whose condition is a constant, a state behind a component property, an empty state no mock record
produces, a composition only a width reaches. Each of those is a screen somebody would otherwise not know they owed, and
a screenshot reports none of them.

A state the source gates but nothing draws is not a screen to invent. It is a gap in the design, and it goes to the
owner as a correction to make in the project — never into the client as a guess.

## What this skill does not do

- It never writes to the design project, and it never writes the mirror either. Both belong to `$mf-sync-design`, and the
  first of them belongs to nobody's session at all.
- It takes no screenshots. Holding a running screen against the design is its own step and reads this mirror rather
  than replacing it: `scripts/capture-design.sh`, `scripts/capture-client.sh` and `scripts/compare-captures.sh`, in
  that order, with `frontend/AGENTS.md` § *Holding a screen against the design* as the rule.
- It stages nothing of its own. The mirror is already committed; a client change that finds it stale reports that
  rather than refreshing it mid-task, because a refresh is a pull request of its own.
