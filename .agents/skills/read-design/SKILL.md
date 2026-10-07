---
name: read-design
description: Use before building, changing, or reviewing any client screen, or the design itself — it points at the state inventory a screen is built from and at the artboard files behind it.
license: AGPL-3.0-only
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/Krzysztof318/MailFathom
---

# Read Design

The client is built UX-first, and what a screen looks like is settled in `design/` — artboards written in this
repository, each split into a template, a stylesheet, a component, and the sample data it shows, beside a state
inventory naming every state each one has and the pairing the parity loop reads. This skill is how that is **read**,
and what a change to it owes.

Everything under `design/` is tracked, so a contributor reads exactly what a maintainer's session reads, and
`scripts/capture-design.sh` serves the same files in either clone. `design/README.md` is the page for the directory.

## The source is the inventory, not a preview

A rendered preview shows the one state it was clicked into. The source states every screen, every variant and every
reaction at once, and most of a screen's states — the empty one, the failing one, the one that only exists under
`pointer: coarse`, the one no click reaches at all — are invisible in a picture. A screen built from a screenshot is
built from a partial reading of a complete document.

So the reading order is: **`design/state-inventory.md` first**, the artboard when the inventory is not specific enough,
and a rendered page last and only to settle something the source genuinely does not say.

A state's name in the inventory is the flag name in the source, so one `grep` finds both the gate in the template and
the line that decides it:

```bash
grep -n 'showThreadPane' design/prototype.dc.html design/logic/prototype/component.js
```

What a screen shows is in `design/data/` — the prototype's by concern (`threads.js`, `people.js`, `calendar.js`, …),
every other artboard's in one file named after it. The template in a `.dc.html` page, its stylesheet under `styles/`,
and its component under `logic/` are the rest of one artboard.

## Opening a page

A page opens as it is, from the file system or from any static server rooted at `design/`; it boots on
`design/runtime/support.js` and fetches React from a CDN, so a browser without network access renders nothing. Open it
in `playwright-cli` at the composition that matters — `frontend/AGENTS.md` § *Holding a screen against the design* names
the four sizes and which of them take a coarse pointer. An artboard takes its component properties (`theme`, `layout`,
`state`, …) from the `data-props` attribute of its `data-dc-script` element, and a running page sets them with
`window.__dcSetProps(window.__dcRootName(), { … })`.

## The design half of the parity pairing

`design/parity.json` holds which artboard each screen in `frontend/design-parity/screens.json` is drawn on, the
component properties it takes, and what is pressed to reach the state that manifest names. It is what
`scripts/capture-design.sh` reads, and it lives beside the artboards rather than beside the client manifest because
every one of those is a fact about the design — the manifest in `frontend/` describes the client and carries none.

```json
{
  "screens": {
    "<screen id from the client manifest>": {
      "file": "<artboard page under design/>",
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
name that is also the tail of another control's name needs.

## What the inventory holds

Every screen, the states it has, what reveals each one, and the controls that react — written so it is read instead of
the prototype. Its last section is the one that has to be there: **the states the source has and a preview does not
reach.** A gate whose condition is a constant, a state behind a component property, an empty state no sample record
produces, a composition only a width reaches. Each of those is a screen somebody would otherwise not know they owed, and
a screenshot reports none of them.

A state the source gates but nothing draws is not a screen to invent in the client. It is a gap in the design, and it is
closed in `design/` first, in a change whose issue asks for it.

## When the task is the design itself

A change to the design is a decision about the product, made in a change whose issue asks for it — never as a side
effect of a client task making the design agree with what it built. In the same change it updates:

- `design/state-inventory.md`, for every state it adds, removes, or gates differently;
- `design/parity.json`, when a paired screen moves to another artboard or is reached by different presses.

It keeps the split the directory is built on: sample content goes in `data/`, behaviour in `logic/`, styles in
`styles/`, and the page holds the template alone. Sample data is made up — no real mail, person, address, or credential,
and nothing from a signed-in client — because this directory is public. A changed artboard is checked by opening it at
the compositions it reaches, in both themes, before the change is finished. Every file carries the licensing header
except `design/runtime/support.js`, which is generated and replaced whole rather than edited.

## What this skill does not do

- It takes no screenshots of its own. Holding a running screen against the design is its own step:
  `scripts/capture-design.sh`, `scripts/capture-client.sh` and `scripts/compare-captures.sh`, in that order, with
  `frontend/AGENTS.md` § *Holding a screen against the design* as the rule.
- It never edits the design to match the client. Where the client is right for one of the three reasons
  `design/README.md` names, the correction is named in the pull request and in an issue of its own, and the design moves
  first.
