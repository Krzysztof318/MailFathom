---
name: mf-sync-design
description: Use to bring the repository's design mirror up to the design project — it reads what moved, records the manifest, and opens a pull request when anything changed.
license: AGPL-3.0-only
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/Krzysztof318/MailFathom
---

# Sync Design

`design/` is the repository's copy of the design project every client screen is built from, and this skill is the only
thing that writes it. The project itself is read through the `claude-design` MCP server, which the maintainer's account
is connected to and nobody else's is, so the mirror is how that source of truth reaches a public tree at all: a
contributor reads `design/`, and a maintainer's session runs this to keep `design/` honest.

It writes the repository and never the project. Not a file, not a screen, not a one-word fix to a label that is
provably wrong — what makes a source of truth one is that a single person writes it, by hand, in the editor. A
disagreement between the design and the client is resolved by changing the client and handing the owner the exact
correction; `$read-design` holds that rule and this skill inherits it whole.

**It does not run in the fork role.** The server answers one account, so a session that finds no `claude-design` server
has found the current state rather than a broken configuration. Say so and stop: the mirror already in the tree is what
a fork reads, and `$read-design` is the skill for reading it.

## Where it runs

A refresh is a change to the repository like any other, so it takes a branch and a worktree of its own and never rides
along inside a task's.

Steps 1 and 2 below come first all the same, before any of that: they write nothing, and what they answer is whether
there is a change to make at all. An unchanged project is the common case and it ends the run with no branch, no issue
and no pull request. Only once the plan has named what moved does this become a task like any other — `$start-task`,
with that plan output as the description the issue is written from, which is also what makes the issue say what moved
rather than that a refresh happened.

## Refreshing the mirror

Four steps, and an unchanged project stops at the second having written nothing at all.

1. **Resolve the project.** `mcp__claude-design__list_projects` returns it. Nothing in the repository names its address,
   and nothing a session writes may — not a commit, an issue, a pull request, a log, or a file.

   Its tools are deferred: `ToolSearch` with
   `select:mcp__claude-design__list_projects,mcp__claude-design__list_files,mcp__claude-design__read_file`
   loads the schemas first.

2. **List, and compare.** One `list_files` at `depth: -1` returns every file's path, size and opaque `etag` with no
   content read at all. Write that JSON to a scratch directory of your own — never into `design/`, which holds the
   mirror and nothing a session invents — and:

   ```bash
   bash scripts/design-mirror.sh plan "$WORK_DIR/listing.json"
   ```

   It prints one line when nothing moved, and otherwise names what appeared, what changed, what went, and which screen
   sources have to be read. **An unchanged project ends the run here**: report the stamp, open no issue, open no pull
   request, and leave the branch unmade. There is nothing to commit and a pull request that changes nothing is worse
   than no pull request.

   The comparison is the server's own `etag`, never a hash computed here: computing one would mean reading the file the
   check exists to avoid reading.

3. **Read only what moved.** For each path the plan named, `read_file` it. Two shapes, and the cheap one is the common
   one:

   - **A result too large to return inline** is written to a file by the harness, which names the path in its message.
     Those bytes never pass through the model at all, so this is the honest way to copy a large file. A file past the
     server's 256 KiB per-call cap comes back as several windows — read it with `offset` and `limit`, then name every
     saved result in order:

     ```bash
     bash scripts/design-mirror.sh extract "design/files/<path>" <saved> [<saved>...]
     ```

   - **A small result comes back inline**, HTML-entity-escaped so it cannot close its own wrapper. Write the escaped
     body to the mirrored path exactly as it arrived — copying, never re-typing — and then:

     ```bash
     bash scripts/design-mirror.sh decode "design/files/<path>"
     ```

   The mirror copies the files named in the list at the top of `scripts/design-mirror.sh` — the screen sources, and
   `support.js`, the generated runtime that boots them, without which an artboard renders nothing at all, which is what
   `scripts/capture-design.sh` needs it for. **Nothing else is read, and a file the project has gained is not added to
   that list by a session.** `plan` reports it as having appeared and says so in as many words; whether a public
   repository carries it is the owner's decision, and the answer arrives as an edit to that list rather than as a file
   that turned up in a refresh. The project's assets and its thumbnail are the standing case for leaving one out: they
   are images rather than text, several megabytes of them would be committed and re-committed at every refresh, and a
   screen is built from the source rather than from them. The manifest covers **every** file, so one appearing or
   disappearing is still visible in the diff.

4. **Record.** `bash scripts/design-mirror.sh record "$WORK_DIR/listing.json"` writes the manifest and checks every
   mirrored file against the byte count the project states. A mismatch is a transcription that dropped or doubled a
   window, and it is a failure rather than a warning. It prints the new stamp.

## What a moved screen source still owes

The mirror is three files that have to agree, and `record` only proves the first of them.

- **`design/state-inventory.md`** is what a screen is actually built from — every state, what reveals it, and the ones
  no preview reaches. Re-extract it for whatever moved and put the new stamp at the top. An inventory whose stamp
  disagrees with `bash scripts/design-mirror.sh stamp` is describing an older design, which is the one failure this
  whole arrangement exists to make visible, and landing a refresh without it is how the tree starts lying to every
  reader who trusts it.
- **`design/parity.json`** names, for each screen in `frontend/design-parity/screens.json`, the artboard it is drawn on,
  the component properties it takes, and what is pressed to reach it. It records the stamp it was written against and
  `scripts/capture-design.sh` refuses one that disagrees with the mirror's, so a refresh that moved an artboard is not
  finished until this file has been read against what moved. `$read-design` § *The design half of the parity pairing*
  holds its shape.

Neither is derivable from the diff, and both are why a refresh is a session's work rather than a script's.

## Opening the pull request

Finish it the way every change here finishes: `$review-change`, then `$check-docs-licenses`, then `$finish-change`.
Nothing about a design refresh is exempt from a gate, and two of them are the reason this lands as a pull request rather
than as a push — `scripts/test-agent-workflow.sh` proves the mirror carries no licensing header and that the manifest
matches the files beside it, and the typo check reads the whole tree.

The pull request body says what moved, in the plan's own words: which screen sources changed, which appeared, which
went, and what the inventory now says that it did not. Name the stamp it moves from and the stamp it moves to — that
pair is what a reader checks a later capture against. Name no address: the project's own link belongs in a reply to the
owner and nowhere else, and neither a `serve_url` nor an identifier lifted out of one reaches the repository at all.

## What this skill does not do

- It never writes to the design project. `write_files`, `copy_files` and `delete_files` are not called, whatever the
  task's acceptance says and however small the change looks.
- It takes no screenshots and runs no parity pass. Holding a running screen against the design is its own step and
  reads the refreshed mirror rather than replacing it: `scripts/capture-design.sh`, `scripts/capture-client.sh` and
  `scripts/compare-captures.sh`, with `frontend/AGENTS.md` § *Holding a screen against the design* as the rule.
- It changes no client code. A screen that now disagrees with the design is a defect to raise, and the issue that
  raises it is a different issue from the one this pull request closes.
