# Design Authoring Instructions

These instructions apply under `design/` in addition to the repository root instructions. [`README.md`](README.md) says
what the directory holds and what a change to it owes, and `$read-design` is how it is read; nothing here restates
either.

## Skills

**A design change is drawn with the taste skills vendored under `.agents/skills/`, never from a session's own
defaults.**

- Open with `$read-design`, then the taste skills: `design-taste-frontend` by default, `redesign-existing-projects` when
  reworking a screen that exists, `high-end-visual-design` for the detail of a surface, and the image-generation ones to
  explore a direction before anything is drawn.
- The design already drawn outranks a skill. A skill settles what the artboards have not decided, and its advice on
  frameworks, libraries, and builds is not followed: an artboard takes no dependency.
- Draw only what the client can ship under `frontend/src/AGENTS.md`.
- These skills are for `design/` only. A client task builds from the design and opens none of them.

## Fast, in balance

**The design is authored fast.** Nothing here ships, so what counts is how soon a screen can be looked at and judged,
and an artboard's code is the means to that rather than a deliverable.

- **Not owed:** the stacks' rules for structure, naming, and logic, deduplication, error and edge-case handling, unit
  tests, or tidying on the way past.
- **Still owed:** the screen itself at the full bar, a page that boots with every inventoried state reachable under the
  inventory's name, the split into template, styles, logic, and data, and everything `README.md` asks of a change.
- **The balance:** could the next session find what to change within a minute and change it without breaking the
  screen beside it? If so the code is good enough; if not, tidy as far as that and no further.

A review reads a change here against this bar: what the page shows and what it owes is a finding, how its code is
structured is not.
