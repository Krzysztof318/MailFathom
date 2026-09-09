# The design MailFathom's client is built from

What a MailFathom screen looks like is settled in a design project the maintainer draws by hand, and this directory is
that project copied into the repository. It is where a client screen comes from: **the design decides, and the client is
expected to look like what it shows.**

The project is read through an MCP server one account is connected to, and it is not public. That is the whole reason
this directory exists — without it, what a screen should look like would be readable by one person, and every client
change from anybody else would be a guess. Here it is readable by everyone who can clone the repository, and the parity
loop that judges a client screen runs in any clone.

## What is here

| Path | What it is |
|---|---|
| `files/` | The screen sources named in `scripts/design-mirror.sh`, byte for byte, and `support.js`, the generated runtime that boots them. An artboard is a component that runtime renders rather than a document a browser draws, so a mirror without it renders nothing |
| `manifest.json` | Every file the project holds — path, size, and the server's own opaque `etag` — including everything deliberately not copied. It is what a refresh compares against, and what makes a file appearing or disappearing visible |
| `state-inventory.md` | Every screen, the states it has, what reveals each one, and the states no rendered preview reaches. **This is what a screen is built from**; the sources under `files/` are what it was extracted from |
| `parity.json` | Which artboard each screen in `frontend/design-parity/screens.json` is drawn on, the properties it takes, and what is pressed to reach it. `scripts/capture-design.sh` reads it |

Both `state-inventory.md` and `parity.json` record the **stamp** — a digest of the etags the screen sources stood at when
they were written. `bash scripts/design-mirror.sh stamp` prints the mirror's own, and a disagreement means one of them
is describing an older design.

**What is copied is a named list rather than a pattern**, and that list lives in `scripts/design-mirror.sh`. This
repository is public and the project is not, so which of its files reach a public tree is a decision somebody takes by
editing that list — a file the project gains is reported by a refresh as having appeared and is then left alone. The
images are the standing case: megabytes no screen is built from. The manifest still records every file, so one arriving
or leaving is visible without any of them being committed.

## How it is kept current

`$mf-sync-design` is the only thing that writes this directory. It lists the project, reads whatever moved, records the
manifest, re-extracts the inventory and the pairing, and opens a pull request. It needs the design server, so it runs in
the maintainer's clone; everywhere else this directory is read-only in practice as well as by rule.

Nothing here is edited by hand, in any clone. A file under `files/` is a copy whose byte count is checked against what
the project states, so an edit fails the next refresh rather than surviving it — which is also why these files carry no
licensing header, the one exemption in the repository that rests on the bytes rather than on who wrote the file. The
reason is stronger than mechanics:
what makes a source of truth one is that a single person writes it, and correcting the design here would leave the
project and the repository disagreeing about what the product looks like.

## When the client and the design disagree

The design wins and the client changes. Three things turn that around — an accessibility obligation, a real platform
constraint, and something that cannot be built — and each of those is a correction to make in the project rather than a
difference to live with: name it precisely, raise it, and let the design move first. The same holds for a state the
source gates and nothing draws, which is a gap in the design rather than a screen to invent.

`frontend/AGENTS.md` § *Where a screen comes from* and § *Holding a screen against the design* carry the rule and the
three scripts that hold a running screen against these files as images.
