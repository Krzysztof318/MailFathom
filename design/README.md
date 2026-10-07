# The design MailFathom's client is built from

What a MailFathom screen looks like is decided in this directory. It holds the design as a set of artboards written in
this repository — the template, the stylesheet, the behaviour, and the sample data of each in files of their own — and
it is where a client screen comes from: **the design decides, and the client is expected to look like what it shows.**

Everything here is tracked and reviewed like any other change, so every clone reads the same design, and the parity
loop that judges a client screen runs in any clone.

## What is here

| Path | What it is |
|---|---|
| `prototype.dc.html` | The main artboard — the whole signed-in client, every screen in one component |
| `sign-in.dc.html`, `client-states.dc.html`, `ai-blocks.dc.html`, `toasts.dc.html`, `notification-gesture.dc.html` | The other artboards, one each |
| `mail-search.html` | The mail search specimen, a static page with no runtime |
| `design.js` | The one global the other files share, `MailFathomDesign`: sample data under `data`, each artboard's component under `artboards` |
| `styles/` | One stylesheet per artboard, linked from the page's `<helmet>` |
| `logic/` | Each artboard's component. The prototype's is split into `markdown.js` (the Markdown renderer message bodies use), `mail-html.js` (the shell and the measuring script an original message is shown in), and `component.js` |
| `data/` | The sample data, one file per artboard or, for the prototype, one file per concern: `threads.js`, `mail-bodies.js`, `people.js`, `workspace.js`, `home.js`, `discover.js`, `cases.js`, `calendar.js`, `tasks.js`, `compose.js`, `documents.js`, `agent.js`, `notifications.js`, and `sample-text.js` |
| `runtime/support.js` | The generated runtime every `.dc.html` page boots on. An artboard is a component this runtime renders rather than a document a browser draws, so without it nothing renders |
| `state-inventory.md` | Every screen, the states it has, what reveals each one, and the states no rendered preview reaches. **This is what a screen is built from**; the artboards are what it describes |
| `parity.json` | Which artboard each screen in `frontend/design-parity/screens.json` is drawn on, the properties it takes, and what is pressed to reach it. `scripts/capture-design.sh` reads it |

## How a page is put together

A page opens in a browser as it is, from the file system or from any static server rooted at `design/`. It loads the
runtime from `runtime/support.js`, and React from the CDN that runtime names. Every merge to `main` publishes the
directory at <https://krzysztof318.github.io/MailFathom/design/>, where an index lists the pages.

Inside a page, `<x-dc>` holds the template exactly as the runtime reads it, with `{{ … }}` bindings and the `sc-if` and
`sc-for` directives, and the `data-dc-script` element under it does one thing: it asks the component registered in
`logic/` for the class the runtime renders, passing it the runtime's `DCLogic` and `React`. The component reads the
sample data from `MailFathomDesign.data` under the names its code uses, so changing a sample is an edit to a data file
and nothing else. Text the template shows as written rather than computes — today's date, the calendar's month — is
bound as `{{ sample.<name> }}` and lives in `data/prototype/sample-text.js`.

Sample data is made up, and stays that way: no real mail, no real person, no address or credential, nothing taken from
a signed-in client. This directory is public.

## Changing the design

A change to the design is a decision about the product, so it is made deliberately: in a change whose issue asks for it,
never as a side effect of a client task making the design agree with what it built. It edits the artboard and whatever
of its data, logic, and styles the change reaches, and in the same change:

- `state-inventory.md`, for every state it adds, removes, or gates differently — the inventory is what a screen is
  built from, so a state the artboard has and the inventory does not is a state nobody will build;
- `parity.json`, when a paired screen moves to another artboard or is reached by different presses.

A change that alters what an artboard looks like is checked by opening it in a browser at the compositions it reaches,
in both themes — `frontend/AGENTS.md` § *Holding a screen against the design* names the four sizes and the scripts.

Every `.html`, `.css`, and `.js` file here carries the licensing header except `runtime/support.js`, which a design tool
generated and which is replaced whole rather than edited; `AGENTS.md` states that exemption, and
`THIRD_PARTY_LICENSES.md` records the file.

## When the client and the design disagree

The design wins and the client changes. Three things turn that around — an accessibility obligation, a real platform
constraint, and something that cannot be built — and each of those is a correction to the design rather than a
difference to live with: name it precisely in the pull request and in an issue of its own, and let the design move
first. The same holds for a state the source gates and nothing draws, which is a gap in the design rather than a screen
to invent in the client.

`frontend/AGENTS.md` § *Where a screen comes from* and § *Holding a screen against the design* carry the rule and the
three scripts that hold a running screen against these files as images.
