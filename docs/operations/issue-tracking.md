# Issue tracking and the roadmap board

<!-- describes: .github/ISSUE_TEMPLATE/**, .github/PULL_REQUEST_TEMPLATE.md -->

This page is the whole of how work is tracked here. `$start-task` reads it before opening or placing an issue and `$finish-change` before linking a pull request to one, which is every point at which the board is written.

Work is tracked as GitHub issues on the `MailFathom roadmap` project board (project number `4`, owner `Krzysztof318`), which is the owner's view of progress. The board reflects the repository; it never becomes a second source of truth. `specs/` remains authoritative for what a change must do, and an issue links to its specification instead of restating it.

**The repository is public and the board is not.** Only the owner can reach project `4`, so every rule below that reads or writes it is a rule for an agent working in the owner's checkout. From a fork, `gh project item-list` and `gh project item-edit` fail on permission rather than degrading, and there is nothing to fall back to — so a fork's agent does not attempt them, and nothing here asks it to. The issues themselves are public and are where a contribution is discussed; what stays private is the owner's ordering of them. No public file links the board, for the same reason: a URL that answers `404` for everyone but one person is worse than no URL.

This repository is worked by agents. Issues are opened, filled in, labeled, placed on the board, and closed by an agent rather than by a person, so the conventions below are the whole mechanism rather than a description of one. Nothing here is tidied up afterwards by hand: an issue that arrives without its label and its board fields simply stops being visible in the views the owner reads. Apply every rule on this page as part of opening the issue, decide the values from the rules given, and state in the task brief what was set. Ask the owner only where a rule below says the choice is theirs.

Every rule below is therefore written from the position that an agent opened the issue. A public repository also receives issues and pull requests nobody here opened, which none of those rules reached; **Issues and pull requests from outside the project** governs those.

## Four questions, four mechanisms

Each question has exactly one owner, and no mechanism answers a question another one already answers. Adding a second mechanism for a question that already has one is the failure mode this structure exists to prevent.

| Question | Mechanism | Decided by |
|---|---|---|
| What kind of work is this? | A `type:*` label | the rules under **Labels** |
| Which release does it ship in? | The milestone | the rules under **Milestones** |
| Where is it in its lifecycle? | The board's `Status` field | the built-in board workflows, never by hand |
| What is being worked next, and what is deliberately not being worked? | The board's `Queue` field | the rules under **Board fields**; the owner chooses `Next`, and `$finish-change` also writes it when a pull request opens |

An open pull request moves both of the bottom two rows, and that is not the duplication this table forbids. `Status: In Progress` is the lifecycle fact and `Queue: Next` is what puts the item in front of the owner; they stay separate answers because a project view filters fields with `AND` and can therefore read only one of them. Asking `Now` for what the owner queued *or* what is in flight is not expressible, so the two conditions have to meet in one field for either to be visible there at all.

## The issue that governs a change

- Every change starts from an issue. Identify it during `$start-task`, before editing files, and name it in the task brief. Read the governing specification and ADR context first, because an issue body is written from them.
- Each numbered specification under `specs/` has exactly one issue, titled `Spec NN — <specification title>`. Create the issue in the same change set that adds a new specification, so a specification never exists without a tracked unit of work.
- Work that is not a numbered specification — maintenance, an ADR consequence, a defect — also gets an issue. State in its body that no `specs/` file backs it and name the ADR or the reason instead.
- Do not open a second issue for work an existing issue already covers. Extend the existing issue when scope grows and record why.

## Issue content

- Write issues in English, matching `specs/` and the rest of the repository.
- Every issue body carries two or three user stories and a condensed acceptance list. A specification issue additionally opens with a header block naming the roadmap group, the draft delivery stage, a link to the specification file, the issues it depends on, and the estimated change size.
- Do not copy specification text into an issue. The specification is the contract, and a duplicated copy goes stale silently.
- Express dependencies as issue references so the board shows them as links. Specification dependencies always point backwards to lower-numbered specifications.
- Nothing on the board schedules work. The owner works alone at irregular times, so order is recorded and timing is not. The board carries a one-week `Week` field, kept deliberately informational: no rule reads it, no view filters on it, and an issue is complete without it. Never make it load-bearing, never add a deadline, day estimate, sprint, or capacity field beside it, and do not read `Size` as one — it estimates a diff, not a duration.
- Use a parent issue only where it carries something no other field can. A parent standing over the issues a release needs answers the question the milestone already answers, and one standing over a theme does what `Track` does, so both create a second hierarchy next to the roadmap and neither is worth having. What earns a parent is one feature large enough that it had to be split into several issues, whose parts then have an order between them: which piece gates the rest, which two can run in either order, and what has to be true across all of them before the feature is done. The milestone cannot say that, `Track` cannot say it, and dependency references say it only to somebody who opens every child. `#332` is the worked example — one mailbox credential, four issues, one gate. Where the feature is small enough to be one issue, or where its parts have no order, the references each body already carries are enough, and a parent adds a place to keep up to date instead. Opening a parent also settles which milestone the parent itself takes, which is a decision rather than a step, and the rules under **Milestones** are where it is made.

## Labels

Every issue carries exactly one `type:*` label and nothing else is required. The type names what the work produces, which is a property of the work itself, so it is chosen when the issue is opened and then left alone; it does not track progress and it never changes because circumstances did.

Several changes match more than one description — a defect in database wiring, a documentation-only change to this contract — so the table is a precedence list, not a menu. Read it top to bottom and take the first row that fits.

| Label | Use it for |
|---|---|
| `type:spec` | Work backed by a numbered specification under `specs/` |
| `type:decision` | Work whose deliverable *is* a decision: an ADR, a policy, or a measurement that settles a question |
| `type:defect` | Something already implemented behaves incorrectly, whatever part of the system it lives in |
| `type:docs` | Documentation only, under `docs/`, `README`, or `specs/` prose. `AGENTS.md` and `.agents/skills/` are the workflow contract, not documentation, and belong to the next row |
| `type:workflow` | Repository tooling, CI, verification scripts, the release process, and this workflow contract |
| `type:infra` | Orchestration, database wiring, telemetry, build and packaging plumbing |
| `type:feature` | Any remaining production-code change: a feature, a refactor, or hardening |

`type:decision` marks work only the owner can settle, and the `Decisions` view is read as a queue of that debt. It says what the issue *produces*, so it belongs on the issue that decides, not on the issues waiting for the answer — those keep the type of what they will eventually build and say they are waiting through `Queue: Needs decision`. Encoding one state in both places would leave the type stale the moment the decision landed.

The remaining labels are flags, applied only when they are true: `blocked` when an issue waits on something outside itself, `security` when a change needs a security review before it merges, `cross-milestone` on a parent issue whose children are spread across releases, for the reason the **Milestones** rules give, and `good first issue` or `help wanted` on work the project would rather someone else took. `shipped` is historical, marking the six issues written retrospectively for work that predates the roadmap; never apply it to new work.

## Milestones

A milestone answers which release an issue ships in, and nothing else. Its name is a version number, so a milestone is never opened for a feature, a theme, or a date, and a body of work that spans releases is a parent issue rather than a milestone of its own. An issue with no milestone is deliberately outside the current release rather than merely unsorted, which is what makes the absence of one meaningful.

`0.2.0 — second release` is the open milestone, and it declares no scope in advance: what ships in it is whatever is placed in it. That is the opposite of how `0.1.0 — first public release`, now closed, was written, and the difference decides how a new issue is placed. A milestone that describes its own contents can be tested against — an issue either is or is not something that release cannot ship without — and one that accumulates cannot, because the description is the placement rather than a rule for it.

So a new issue takes no milestone by default, and that stays a decision rather than an omission: the absence means deliberately outside the release, as it does everywhere else on this page but the parent issue below. Placing an issue in `0.2.0` is what defines the release, so it is the owner's call.

**The owner's call can be standing rather than per issue.** They may decide that new work of some kind — code and documentation, say — goes into the open milestone until they say otherwise, and an agent then assigns it without asking, because the decision has already been taken and asking again re-litigates it. What an agent must never do is infer such a decision from the shape of the work, from what a neighbouring issue carries, or from the milestone being open. Absent a standing decision the default above holds and the milestone stays empty.

**A parent issue takes its own milestone from whether its children fit in one release.** That is the owner's decision, made when the parent is opened rather than read off the children afterwards, and it is the same decision as how the feature will be delivered: in one release, or in stages across several. Where every child ships in the same release, the parent carries that milestone too and the `Release <version>` view reads whole; `#332` is the worked example. Where the children are spread across releases, the parent carries no milestone, because naming the first release would say the whole feature ships there and naming the last would drop it out of the release already delivering half of it.

That empty milestone is the one place on this page where an absence does not mean *outside the release*, so the parent carries the `cross-milestone` label to say which of the two meanings it has. Nothing else takes that label: each child still carries the milestone of the release that ships it, and an ordinary issue without one still means what it always meant. An agent never chooses between the two shapes and never infers one from the children — it asks, exactly as it does for the milestone itself, and a standing decision about milestones does not settle this one, because which releases a feature is delivered over is a separate call from whether new work goes into the open release at all.

Do not open a further milestone beside the open one; the next is created when that one closes.

## Board fields

The board carries three single-select fields beyond `Status`, plus an informational one. Set `Track` on every issue. Set `Queue` on every open issue. Leave `Size` empty until the work is planned, and leave `Week` alone entirely.

- **`Track`** groups every item by the area of the system it belongs to, including work no specification backs. `A` through `E` are the roadmap groups from `specs/README.md`. `Release` is release-process and distribution work — licensing, versioning policy, branching, contributor entry points, packaging, publication — and it says nothing about which release ships it, because that is the milestone's question. `Platform` is repository tooling and cross-cutting concerns no roadmap group owns. `Future capabilities` is beyond the current roadmap segment.
- **`Queue`** is the ordering signal, and a new issue takes one of its three lower values without asking. `Later` is the default: accepted scope not yet started. `Needs decision` says this issue waits on an answer rather than on effort; name the `type:decision` issue that produces the answer, or state that none exists yet. `Parked` records a review outcome or a side question that carries no commitment to act — something the project decided about and may return to, which is why it never stands in for work the project has declined or for an issue nobody has read yet. `Next` means in the owner's field of view now, and it has two writers. The owner sets it to mean ready to start, and at most five **open** issues hold it that way; the cap is what keeps the value a decision rather than a copy of everything already accepted. `$finish-change` sets it as well, on the issue its pull request closes, so that work already in flight is legible in the view the owner reads instead of only in the pull request list. Those sit outside the cap: an agent opening a pull request is not choosing what to start next and must never spend one of the five slots that decision uses. A closed issue keeps whatever `Queue` value it had and stops counting, which is why neither kind has to be cleared on merge and why every view that reads `Queue` filters `is:open`.
- **`Size`** measures the pull request in changed lines, additions plus deletions, including tests and documentation. The ranges are contiguous and leave no gap: `S` under 1000, `M` from 1000 to 2499, `L` from 2500 to 4999, `XL` from 5000 up and to be split before it starts. Read a specification's own line estimate through a factor of five, because that is what the nine merged specification pull requests measured — a median of 5.0 against the estimate, ranging from 2.6 to 7.3, never below. A specification that says 600 lines is an `L`. `L` is the normal size of a specification here, so an `XL` is a genuine warning rather than a large-sounding label.
- **`Week`** is informational and unused. It exists because a one-week grid is occasionally worth glancing at, not because anything depends on it. Do not set it, do not filter on it, and do not let a rule come to rest on it.

The built-in workflows set `Status` and nothing else, so a newly opened issue reaches the board with no `Track` and no `Queue`. Setting both is part of opening the issue:

```bash
gh project field-list 4 --owner Krzysztof318 --format json   # field ids and option ids
gh project item-list  4 --owner Krzysztof318 --format json   # item id for the issue
gh project item-edit --project-id <project-id> --id <item-id> \
  --field-id <field-id> --single-select-option-id <option-id>
```

Each field is a separate call, so one can land while another fails. A project view filters fields with `AND` and cannot ask for a missing `Track` *or* a missing `Queue` in one expression, which is why the `Triage` view catches only the untouched case. Audit both after placing an issue, and whenever the board is worth trusting:

```bash
gh project item-list 4 --owner Krzysztof318 --format json --limit 200 \
  | jq -r '.items[] | select(.status != "Done") | select(.track == null or .queue == null)
           | "\(.content.number) track=\(.track) queue=\(.queue)"'
```

A missing `Track` is also visible without running anything: the `Roadmap` view groups by `Track`, so an unplaced item sits in its own group at the end of the board.

## Views

`Now` is open issues with `Queue: Next`, grouped by `Status`, and it is the view the owner works from. That grouping is what separates the field's two writers without a second field: what the owner queued waits in `Todo`, and what a pull request carried in sits in `In Progress`, because the same event that set `Queue` also moved `Status` there. `Roadmap` is everything open grouped by `Track`. A `Release <version>` view carries one milestone each, so the release being worked and the one being planned are read separately rather than filtered apart by hand. `Decisions` is the open `type:decision` issues — the answers the owner owes, not the work waiting on them. `Triage` lists open items with no `Queue` value, which is the inbox for issues the project did not open: an arrival carries no board fields because none of the rules here reached its author, and **Issues and pull requests from outside the project** is what empties it. An item the project itself opened never belongs there, because an agent sets `Queue` as part of opening an issue. Every view that reads `Queue` filters `is:open`, so no `Next` value outlives its issue and a closed one never occupies one of the owner's five slots.

## Issues and pull requests from outside the project

An issue the project did not open arrives with no `type:*` label, no `Track`, no `Queue`, and no milestone, because none of the rules above reached its author. That is the expected shape of an arrival rather than a defect in it, and it is not corrected by inventing values at a glance.

The absence of a `type:*` label is what marks an issue untriaged, because an agent always sets one. Triage is therefore a state a reader can see without a field, a label, or a board column existing to announce it, which is why none was added: the four questions still have four mechanisms, and *has anyone read this* is answered by whether the first of them was ever asked.

Triage is one pass over the issue and it is not implementation. Read it, then either place it or end it:

- **Place it.** Assign exactly one `type:*` label, a `Track`, a `Queue`, and a milestone if the rules above assign one, by the same rules that govern an issue the project opened. `Later` is the value a placed arrival takes, and triage never assigns `Next`: that choice stays the owner's whoever opened the issue, and the other way into it is a pull request that does not exist yet. What the reporter asked for does not decide the label: a report that names a defect is `type:defect` even when it was written as a feature request.
- **End it.** Close it as `not planned` and state the reason on the issue. `Parked` is not that, for the reason the `Queue` rules give.

A question is not a unit of work and does not become one by arriving as an issue. Move it to Discussions and close the issue with a link, rather than giving it a `type:*` label so the board has somewhere to put it. Discussions carries `Q&A` for questions, `Ideas` for proposals that are not yet scope, and `Announcements` for what the project says; a discussion that turns out to be work is converted to an issue and then triaged like any other.

A pull request the project did not open is read in a fixed order, so a change is refused for the cheapest reason first: the required checks, then `Protected paths`, which refuses a change from anyone but the owner to `.github/`, `.config/`, `.agents/`, `.claude/`, or `docs/decisions/`, to an `.editorconfig`, `.gitattributes`, `.worktreeinclude`, `AGENTS.md`, or `CLAUDE.md` at any depth, or to the repository-root `CHANGELOG.md`, `Directory.Build.props`, `LICENSE`, `NOTICE`, `NuGet.config`, or `global.json` — and which names the paths it found either way, so an allowed change says which of them it moved. Only then comes the code-owner review the `main` ruleset requires. Nothing precedes those, and in particular no acknowledgement gate does: section 5 of Apache-2.0 puts a contribution under the project's license by the act of submitting it, so a check asking a contributor to state that it does adds a step to every first contribution and establishes nothing the license did not already establish. `CONTRIBUTING.md` says so where a contributor reads it. `Fathom review` runs on a fork only when a maintainer applies the `fathom-review` label — a fork's own pushes never start one — so a contributor waiting on that verdict is waiting on a decision rather than on a queue. A pull request whose author has stopped answering is closed with a comment saying so, and the issue it addressed keeps its own `Queue` value. Nothing does that automatically: at this project's volume, machinery that closes a contribution nobody read would cost more than the stale pull requests it removes.

## Linking a pull request to its issue

- Every pull request body contains `Closes #<issue>` for the issue it completes, so merging closes the issue and the board moves the item to `Done`.
- Add the reference when the pull request is created. `$finish-change` treats a pull request without an issue reference as an incomplete gate.
- `gh pr edit` fails against this repository with a Projects-classic GraphQL error and silently drops the edit. Patch a pull request body through the REST API instead:

  ```bash
  gh api repos/<owner>/<repo>/pulls/<number> -X PATCH -f body="$(cat body.md)"
  ```

- Once the pull request exists, set `Queue: Next` on the issue it closes, through the same `gh project item-edit` call that placed the issue. Do this for every pull request, whether the issue was opened for this task or had been sitting in `Later` for weeks, and treat a value that did not land as an incomplete gate rather than as a detail to fix later. Nothing else writes the field afterwards: the issue keeps `Next` until the merge closes it out of every view that reads `Queue`.

Writing it from the skill is not a shortcut past the automation; it is the only place the write can happen. The board's built-in workflows set `Status` and nothing else, so no project automation reaches a custom single-select field. A GitHub Actions workflow could, but this board belongs to a user rather than to an organization, and nothing scoped can write to a user's Projects v2 — a GitHub App has no permission that covers one, and a fine-grained token has no `Projects` scope at the account level. Only a classic token with the `project` scope can, and that scope is account-wide: storing one as a repository secret would give every workflow run write access to all of the owner's projects, to save a step in a skill that already holds such a token and already talks to this board. The cost is that a pull request opened outside `$finish-change` moves nothing, which for a repository whose pull requests are all opened by agents is a smaller gap than the credential would be.

## Status transitions

- The board's `Status` field has `Todo`, `In Progress`, and `Done`.
- The board's built-in workflows own every transition: `Auto-add to project` places a newly opened issue on the board, `Item added to project` puts it in `Todo`, `Pull request linked to issue` moves it to `In Progress`, and `Pull request merged`, `Auto-close issue`, and `Item closed` carry it to `Done`. Do not set those statuses by hand; a manual status that contradicts the automation hides the real state.
- `Status` records what has happened and `Queue` records what is intended, which is why neither substitutes for the other. Work that stalls keeps whatever `Status` the automation gave it and moves to `Later` or `Parked` in `Queue`.
- Automation does not add an issue that is already closed when it is created. Add a retrospective `shipped` issue to the board explicitly and set it to `Done`.
- When work stops without merging, say so on the issue and leave the status to the automation rather than moving the card.
