# Issue tracking and the roadmap board

<!-- describes: .github/ISSUE_TEMPLATE/**, .github/PULL_REQUEST_TEMPLATE.md -->

This page is the whole of how work is tracked here. `$start-task` reads it before opening or placing an issue and `$finish-change` before linking a pull request to one, which is every point at which the board is written.

Work is tracked as GitHub issues on the `MailFathom roadmap` project board (project number `4`, owner `Krzysztof318`), which is the owner's view of progress. The board reflects the repository; it never becomes a second source of truth. Where a specification under `specs/` governs a change, it remains authoritative for what that change must do, and the issue links to it instead of restating it.

**The repository is public and the board is not.** Project `4` is reachable only by the owner and by whoever the owner has granted access to, so every rule below that reads or writes it is a rule for a session that holds that access rather than for a role. The two are not the same question: the owner may grant a contributor read or write on the board without granting anything on the repository, and a clone of MailFathom made without write access reaches neither. So the access is probed rather than inferred — `{ user(login: "Krzysztof318") { projectV2(number: 4) { viewerCanUpdate } } }` answers it in one call, `true` meaning write, `false` meaning read, and a `null` project beside a `NOT_FOUND` error meaning neither. The project number belongs to its owner's namespace, so that login is how anyone addresses this board, including somebody who will turn out to have no access to it; GitHub then hides the project rather than refusing it, which is why the reply to *no permission* is worded as *does not exist* and why reading it as a mistyped number is the wrong conclusion. Without write, `gh project item-edit` fails rather than degrading, so it is not attempted and nothing here asks for it. The issues themselves are public and are where a contribution is discussed; what stays private is the owner's ordering of them. No public file links the board, for the same reason: a URL that answers `404` for everyone but one person is worse than no URL.

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

An open pull request moves both of the bottom two rows, and that is not the duplication this table forbids. `Status: In progress` is the lifecycle fact and `Queue: Next` is what puts the item in front of the owner; they stay separate answers because a project view filters fields with `AND` and can therefore read only one of them. Asking `Now` for what the owner queued *or* what is in flight is not expressible, so the two conditions have to meet in one field for either to be visible there at all.

## The issue that governs a change

- Every change starts from an issue. Identify it during `$start-task`, before editing files, and name it in the task brief. Read whatever governs the change first — a specification, the ADR context, or both — because an issue body is written from it.
- Each numbered specification under `specs/` has exactly one issue, titled `Spec NN — <specification title>`. Create the issue in the same change set that adds a new specification, so a specification never exists without a tracked unit of work.
- An issue names whatever governs it: the specification under `specs/`, the ADR under `docs/decisions/`, or the issue it follows from. A specification is how a large piece of the roadmap is decomposed into reviewable units, not what entitles work to exist, so an issue no specification backs — a feature as readily as maintenance, an ADR consequence, or a defect — is opened on exactly the same terms. Where nothing governs it, its own context is the governing text and there is nothing further to declare: that nothing is linked is already visible to anyone reading it.
- Do not open a second issue for work an existing issue already covers. Extend the existing issue when scope grows and record why.

## Issue content

- Every issue body carries two or three user stories and a condensed acceptance list. A specification issue additionally opens with a header block naming the roadmap group, the draft delivery stage, a link to the specification file, the issues it depends on, and the estimated change size.
- Do not copy specification text into an issue. The specification is the contract, and a duplicated copy goes stale silently.
- Express dependencies as issue references so the board shows them as links. Specification dependencies always point backwards to lower-numbered specifications.
- Nothing on the board schedules work. The owner works alone at irregular times, so order is recorded and timing is not. There is no date, deadline, day-estimate, sprint, or capacity field, and none is to be added: the two the board once carried accumulated no value on any item across its whole history, which is what a field for a question nobody asks looks like. Do not read `Size` as one either — it estimates a diff, not a duration.
- Use a parent issue only where it carries something no other field can. A parent standing over the issues a release needs answers the question the milestone already answers, and one standing over a theme does what `Area` does, so both create a second hierarchy next to the roadmap and neither is worth having. What earns a parent is one feature large enough that it had to be split into several issues, whose parts then have an order between them: which piece gates the rest, which two can run in either order, and what has to be true across all of them before the feature is done. The milestone cannot say that, `Area` cannot say it, and dependency references say it only to somebody who opens every child. `#332` is the worked example — one mailbox credential, four issues, one gate. Where the feature is small enough to be one issue, or where its parts have no order, the references each body already carries are enough, and a parent adds a place to keep up to date instead. Opening a parent also settles which milestone the parent itself takes, which is a decision rather than a step, and the rules under **Milestones** are where it is made.
- A parent carries the `parent` label, applied in the same pass that links its children. Nothing else makes one findable from the board: the documented view qualifiers carry nothing that asks *which issues have children*, and the one sub-issue qualifier among them — `parent-issue:OWNER/REPO#NUMBER` — lists the children of a parent whose number the reader already knew. The label is not what makes an issue a parent, though. The sub-issue links are, they remain the only source of truth for the hierarchy, and where the two disagree the links are right and the label is stale. That is also why the label adds no fifth mechanism beside the four questions above: it answers none of them, and mirrors a structure GitHub already records for the one reader that cannot see it, which is a board view. Link each child by its `id` rather than by its number, which is the part of the call worth reading twice:

  ```bash
  child_id=$(gh api repos/Krzysztof318/MailFathom/issues/<child-number> --jq .id)
  gh api repos/Krzysztof318/MailFathom/issues/<parent-number>/sub_issues -F sub_issue_id="$child_id"
  ```

  `-F` is what sends the id as a number, which is the type the endpoint takes; `-f` would send the same digits as a string.
- A parent's title begins `[P] `. The label answers the same question, but only where labels are rendered: an issue list, a search result, a notification, and a reference from another body all show a title on its own, and a parent read there as an ordinary issue is picked up as work instead of as the thing that groups it. The prefix is applied when the parent is opened, in the same pass as the label and the child links, and it is the whole of the convention — no other kind of issue carries a title prefix, so `[P]` never has to be told apart from a second one.
- The hierarchy is at most two levels deep: a parent, a sub-parent beneath it, and the issues that do the work beneath that. The middle level exists for a feature large enough that one of its own parts split again, and nothing smaller earns one — a feature whose parts are ordinary issues has a parent and no sub-parent, which is the normal shape and stays it. A sub-parent is a parent under every rule on this page: the `parent` label, the `[P]` prefix, the `Queue` rule under **Board fields**, and the milestone rule above all read on it exactly as they read on the parent above it. It is a child in one respect only, which is that it is linked under that parent by the same call. Nothing nests below a sub-parent's children, and a feature that appears to need a third level is two features that each need a parent.

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

The remaining labels are flags, applied only when they are true: `blocked` when an issue waits on something outside itself, `security` when a change needs a security review before it merges, `parent` on an issue whose sub-issues deliver one feature between them, `cross-milestone` on a parent issue whose children are spread across releases, for the reason the **Milestones** rules give, and `good first issue` or `help wanted` on work the project would rather someone else took. `shipped` is historical, marking the six issues written retrospectively for work that predates the roadmap; never apply it to new work.

### `agent:claimed`

`agent:claimed` says a session has this issue in hand. It is the one label that describes the session rather than the work, and it is applied at the moment work on the issue genuinely begins — the worktree is being made for it, the implementation is starting — by `$start-task`, at the step named there. Reading an issue is not taking it, so triage does not apply it, and neither does planning, estimating, or answering a question about one; a marker that meant *somebody looked at this* would be worth nothing to the reader it exists for.

It is never removed. A session that ended is not a reason to clear it, and it stays through the close, so it reads as *a session has had this in hand* rather than as *a session is running now*. That is the weaker of the two claims and deliberately so: a label meaning the stronger one would be wrong from every session that stopped without clearing it, and nothing here would notice, whereas the weaker claim cannot go stale because what it records already happened.

It answers none of the four questions above, which is what lets it stand beside them rather than duplicating one. `Status` is the lifecycle fact, the built-in workflows own it, and it moves when a pull request does; this moves hours earlier, when the work starts. The board is also private, so `Status` answers *where is this* for one reader while the label answers *is anyone on this* on a public issue list, without opening the issue and without the board. Nothing reads it in return — no workflow, script, or skill branches on it — so applying it by hand starts nothing and removing it stops nothing.

Writing a label is write access to this repository, so this belongs to the owner's checkout with the `type:*` label and the milestone. An agent working from a fork does not apply it and nothing is missing when it does not: the session that eventually picks the issue up is the one that claims it.

## Milestones

A milestone answers which release an issue ships in, and nothing else. Its name is a version number, so a milestone is never opened for a feature, a theme, or a date, and a body of work that spans releases is a parent issue rather than a milestone of its own. An issue with no milestone is deliberately outside the current release rather than merely unsorted, which is what makes the absence of one meaningful. The parent issue below is the one exception, and it carries the `cross-milestone` label so that it reads as one.

There is exactly one open milestone at a time, and it declares no scope in advance: what ships in it is whatever is placed in it. That is the opposite of how `0.1.0 — first public release`, the only one written the other way, was, and the difference decides how a new issue is placed. A milestone that describes its own contents can be tested against — an issue either is or is not something that release cannot ship without — and one that accumulates cannot, because the description is the placement rather than a rule for it. Which version is open is read from the milestone list rather than stated here, because a page naming it would be wrong from the moment the next release is cut.

So a new issue takes no milestone by default, and that stays a decision rather than an omission: the absence means deliberately outside the release, as it does everywhere else on this page but the parent issue below. Placing an issue in the open milestone is what defines the release, so it is the owner's call.

**The owner's call can be standing rather than per issue.** They may decide that new work of some kind — code and documentation, say — goes into the open milestone until they say otherwise, and an agent then assigns it without asking, because the decision has already been taken and asking again re-litigates it. What an agent must never do is infer such a decision from the shape of the work, from what a neighbouring issue carries, or from the milestone being open. Absent a standing decision the default above holds and the milestone stays empty.

**A parent issue takes its own milestone from whether its children fit in one release.** That is the owner's decision, made when the parent is opened rather than read off the children afterwards, and it is the same decision as how the feature will be delivered: in one release, or in stages across several. Where every child ships in the same release, the parent carries that milestone too and the `Release <version>` view reads whole; `#332` is the worked example. Where the children are spread across releases, the parent carries no milestone, because naming the first release would say the whole feature ships there and naming the last would drop it out of the release already delivering half of it.

That empty milestone is the one place on this page where an absence does not mean *outside the release*, so the parent carries the `cross-milestone` label to say which of the two meanings it has. Nothing else takes that label, and it never appears without `parent` beside it, because only a parent can carry it: each child still carries the milestone of the release that ships it, and an ordinary issue without one still means what it always meant. An agent never chooses between the two shapes and never infers one from the children — it asks, exactly as it does for the milestone itself, and a standing decision about milestones does not settle this one, because which releases a feature is delivered over is a separate call from whether new work goes into the open release at all.

Do not open a further milestone beside the open one; the next is created when that one closes. That happens in one place — `$prepare-release`, which creates the next milestone if it does not already exist, opens the issue tracking that release in it, moves whatever is still open in the one being released into it, and closes the one being released. Two milestones open at once would make *which release is this in* a question with two plausible answers, and the release is the event that settles it, so the two acts are one step rather than two decisions taken weeks apart. What is still open when a release is cut is scope the owner is deciding about, so it moves rather than being closed on their behalf; an item they would rather drop is closed as `not planned` on its own issue. The issue tracking the release is the one thing that does not move: it is open and carries that milestone at the moment the move happens, so it is exactly what a query for what to move returns, and it closes there once the version-bump pull request merges — after the tag, because a release is finished when `main` names the next version rather than when the changelog merged. A milestone therefore never exists without the issue that closes it: the same step opens the next milestone and the issue tracking the release it stands for.

## Board fields

The board carries three single-select fields beyond `Status`. Set `Area` on every issue. Set `Queue` on every open issue. Set `Size` when the issue is opened, from the scope its own body describes.

- **`Area`** groups every item by the part of the system it belongs to. Nine values, and each is a place rather than a phase, which is what lets the grouping survive the release that produced the work in it:

  | `Area` | What belongs to it |
  |---|---|
  | Configuration & secrets | Configuration binding and validation, secret references, cryptographic material, listeners |
  | Mail synchronization | IMAP sessions, folders, flags, transport security, and writes to the remote mailbox |
  | Storage & retention | Persistence, schema, the content store, retention, and deletion |
  | Retrieval & embeddings | Chunking, vectors, indexes, ranking, and search |
  | Agents & answering | Chat providers, the agent, `ask_mail`, and the bounds on what leaves the process |
  | Automation | Rules, the job model, executions, and classification |
  | MCP surface | Tools, the protocol, and transport authentication |
  | Platform | Repository tooling, CI, verification, dependencies, and telemetry |
  | Release | Packaging, distribution, versioning, and user documentation — never which release ships it, because that is the milestone's question |

  Two of those boundaries are decisions rather than descriptions. Retrieval and answering are separate because the two parent issues that own them draw that line already, so a parent's children never land in two areas. And there is no security area, because `security` is a label: encoding one state in two places is exactly the duplication **Four questions, four mechanisms** exists to prevent.

  The values are deliberately not the roadmap groups from `specs/README.md`. Those decompose the gap to one release, so they name the phase that delivered a piece of work rather than the part of the system it lives in, and they age out the moment those specifications ship — which is what happened, and what this field was corrected from.
- **`Queue`** is the ordering signal, and a new issue takes one of the values below `Next` without asking. `Later` is the default: accepted scope not yet started. `Needs decision` says this issue waits on an answer rather than on effort; name the `type:decision` issue that produces the answer, or state that none exists yet. `Parked` records a review outcome or a side question that carries no commitment to act — something the project decided about and may return to, which is why it never stands in for work the project has declined or for an issue nobody has read yet. `Parent` belongs to a parent issue whose children span releases: the one that carries `cross-milestone` and no milestone. Such a parent is never worked directly and is never finished on its own, so each of the other four values would say something untrue about it — `Later` that it is scope nobody has started while its children are in flight, `Needs decision` that an answer is missing, `Parked` that the project is not committed to it, and `Next` that the owner is working on it, which also spends one of the five slots below on a container. A parent whose children all ship in one release is not this case and takes the ordinary values, because it does complete with that release and reads correctly in the view that holds it. `Next` means in the owner's field of view now, and it has three writers. The owner sets it to mean ready to start, and at most five **open** issues hold it that way; the cap is what keeps the value a decision rather than a copy of everything already accepted. `$finish-change` sets it as well, on the issue its pull request closes, and `$prepare-release` on the issue tracking the release it just opened two pull requests for — that skill never invokes `$finish-change`, so the write is its own — both so that work already in flight is legible in the view the owner reads instead of only in the pull request list. Those sit outside the cap: an agent opening a pull request is not choosing what to start next and must never spend one of the five slots that decision uses. A closed issue keeps whatever `Queue` value it had and stops counting, which is why neither kind has to be cleared on merge and why every view that reads `Queue` filters `is:open`.
- **`Size`** measures the pull request in changed lines, additions plus deletions, including tests and documentation. The ranges are contiguous and leave no gap: `S` under 1000, `M` from 1000 to 2499, `L` from 2500 to 4999, `XL` from 5000 up and to be split before it starts. Read a specification's own line estimate through a factor of five, because that is what the nine merged specification pull requests measured — a median of 5.0 against the estimate, ranging from 2.6 to 7.3, never below. A specification that says 600 lines is an `L`. `L` is the normal size of a specification here, so an `XL` is a genuine warning rather than a large-sounding label.

  The value is set when the issue is opened, as an estimate, and corrected against the diff the pull request actually produced. An estimate that turns out wrong is what makes the next one better, whereas an empty field says nothing and cannot be wrong — so the field is filled from the acceptance list the body already carries rather than left for a planning pass that never happens separately here. A `Size` that was never revised after the merge is the ordinary case and needs no action; one that was revised two steps is worth a sentence on the issue saying what the estimate missed.

  A parent issue takes `XL` and keeps it. Its size is the sum of its children rather than a diff of its own, and that sum is what puts it over the threshold, so the value reads as *this is delivered in pieces* on exactly the issues that already are. The warning `XL` carries elsewhere — split this before starting — is answered on a parent by the children themselves, which is what makes it the one place the value is not a problem to solve.

The built-in workflows set `Status` and nothing else, so a newly opened issue reaches the board with no `Area`, no `Queue`, and no `Size`. Setting all three is part of opening the issue:

```bash
gh project field-list 4 --owner Krzysztof318 --format json   # field ids and option ids
gh project item-list  4 --owner Krzysztof318 --format json   # item id for the issue
gh project item-edit --project-id <project-id> --id <item-id> \
  --field-id <field-id> --single-select-option-id <option-id>
```

Each field is a separate call, so one can land while another fails. A project view filters fields with `AND` and cannot ask for a missing `Area` *or* a missing `Queue` in one expression, which is why the `Triage` view catches only the untouched case. Audit all three after placing an issue, and whenever the board is worth trusting:

```bash
gh project item-list 4 --owner Krzysztof318 --format json --limit 400 \
  | jq -r '.items[] | select(.status != "Done")
           | select(.area == null or .queue == null or .size == null)
           | "\(.content.number) area=\(.area) queue=\(.queue) size=\(.size)"'
```

A missing `Area` is also visible without running anything: the `Roadmap` view groups by `Area`, so an unplaced item sits in its own group at the end of the board.

## Views

A view holds no state. Every one of them is a filter over fields that already exist, which is why the set below adds nothing to the four mechanisms and why no view can be left out of date by an agent forgetting a step.

| View | Filter | What it answers |
|---|---|---|
| `Now` | `queue:Next -status:Done` | what is in front of the owner |
| `Roadmap` | `is:open -queue:Parked` | everything the project intends to build, grouped by `Area` |
| `Backlog` | `is:open queue:Parked` | what it has considered and not committed to |
| `Release <version>` | `milestone:"<version>"` | one release each |
| `Parent features` | `is:open label:parent` | the features, with `Sub-issues progress` as a column |
| `Decisions` | `is:open label:"type:decision"` | the answers the owner owes |
| `Triage` | `is:open no:queue` | the inbox for issues the project did not open |
| `All` | `-status:Done` | everything open, unfiltered, for when a query is easier than a view |

`Now` groups by `Status`, and that grouping is what separates the field's two writers without a second field: what the owner queued waits in `Todo`, and what a pull request carried in sits in `In progress`, because the same event that set `Queue` also moved `Status` there. An approved pull request moves on to `Ready to merge`, so the view reads left to right as start it, finish it, merge it — and the column an item sits in says which of those the owner is being asked for.

**`Roadmap` and `Backlog` are two readings of `Queue`, not two mechanisms.** The line between them falls at `Parked` and nowhere else: `Later`, `Needs decision`, and `Parent` are all on the roadmap, because an issue waiting on an answer is fully intended and merely blocked, and a feature delivered over several releases is the roadmap rather than an exception to it. That is also what keeps the word *roadmap* honest — after the filter, the view holds only work the project means to do.

`Parent` also keeps `Now` correct without a filter of its own. The view asks for `queue:Next`, so a value that is not `Next` is already outside it, and no exclusion had to be added for a case the field now names; that is the whole reason the value exists on `Queue` rather than as a label or a sixth view.

`Parent features` is in table layout so that `Sub-issues progress` reads as a column beside `Area` and the milestone. It is the way into the parents, because the qualifiers a view filters on ask what an item carries rather than what hangs beneath it, and it filters `is:open` for the ordinary reason that a parent whose children are all delivered is closed with them. A sub-parent appears there beside the parent above it, since it carries the same label, and its own progress column is what makes the middle level worth reading.

`Triage` catches an arrival that carries no board fields, because none of the rules here reached its author; **Issues and pull requests from outside the project** is what empties it. An item the project itself opened never belongs there, because an agent sets `Queue` as part of opening an issue. Every view that reads `Queue` filters `is:open`, so no `Next` value outlives its issue and a closed one never occupies one of the owner's five slots.

A view's filter and layout are writable through the GraphQL API, in two calls — `gh project` cannot create one, and `createProjectV2View` takes no filter, so it lands on the `updateProjectV2View` that follows. Its **grouping is not writable at all**: `ProjectV2ViewConfigurationInput` carries visible fields and nothing else, so a view that has to group by `Area` is grouped by hand in the interface once and then left alone.

## Issues and pull requests from outside the project

An issue the project did not open arrives with no `type:*` label, no `Area`, no `Queue`, and no milestone, because none of the rules above reached its author. That is the expected shape of an arrival rather than a defect in it, and it is not corrected by inventing values at a glance.

The absence of a `type:*` label is what marks an issue untriaged, because an agent always sets one. Triage is therefore a state a reader can see without a field, a label, or a board column existing to announce it, which is why none was added: the four questions still have four mechanisms, and *has anyone read this* is answered by whether the first of them was ever asked.

Triage is one pass over the issue and it is not implementation. Read it, then either place it or end it:

- **Place it.** Assign exactly one `type:*` label, an `Area`, a `Queue`, and a milestone if the rules above assign one, by the same rules that govern an issue the project opened. `Later` is the value a placed arrival takes, and triage never assigns `Next`: that choice stays the owner's whoever opened the issue, and the other way into it is a pull request that does not exist yet. What the reporter asked for does not decide the label: a report that names a defect is `type:defect` even when it was written as a feature request.
- **End it.** Close it as `not planned` and state the reason on the issue. `Parked` is not that, for the reason the `Queue` rules give.

A question is not a unit of work and does not become one by arriving as an issue. Move it to Discussions and close the issue with a link, rather than giving it a `type:*` label so the board has somewhere to put it. Discussions carries `Q&A` for questions, `Ideas` for proposals that are not yet scope, and `Announcements` for what the project says; a discussion that turns out to be work is converted to an issue and then triaged like any other.

A pull request the project did not open is read in a fixed order, so a change is refused for the cheapest reason first: the required checks, then `Protected paths`, which refuses a change from anyone but the owner to `.github/`, `.config/`, `.agents/`, `.claude/`, or `docs/decisions/`, to an `.editorconfig`, `.gitattributes`, `.worktreeinclude`, `AGENTS.md`, or `CLAUDE.md` at any depth, or to the repository-root `CHANGELOG.md`, `Directory.Build.props`, `LICENSE`, `NOTICE`, `NuGet.config`, or `global.json` — and which names the paths it found either way, so an allowed change says which of them it moved. Only then comes the code-owner review the `main` ruleset requires. Nothing precedes those, and in particular no acknowledgement gate does: section 5 of Apache-2.0 puts a contribution under the project's license by the act of submitting it, so a check asking a contributor to state that it does adds a step to every first contribution and establishes nothing the license did not already establish. `CONTRIBUTING.md` says so where a contributor reads it. `Fathom review` runs on a fork only when a maintainer applies the `fathom-review` label — a fork's own pushes never start one — so a contributor waiting on that verdict is waiting on a decision rather than on a queue. A pull request whose author has stopped answering is closed with a comment saying so, and the issue it addressed keeps its own `Queue` value. Nothing does that automatically: at this project's volume, machinery that closes a contribution nobody read would cost more than the stale pull requests it removes.

## Linking a pull request to its issue

- Every pull request body contains `Closes #<issue>` for the issue it completes, so merging closes the issue and the board moves the item to `Done`.
- A release is the one unit of work that is two pull requests, and both carry the tracking issue in their titles. Only the version-bump one carries the `Closes` line, because the release is finished when `main` names the next version rather than when the changelog merged; the changelog pull request references the issue without closing it. `$prepare-release` opens both and is where that shape is stated, and it writes the `Queue: Next` below itself rather than through `$finish-change`, which it never invokes.
- Add the reference when the pull request is created. `$finish-change` treats a pull request without an issue reference as an incomplete gate.
- `gh pr edit` fails against this repository with a Projects-classic GraphQL error and silently drops the edit. Patch a pull request body through the REST API instead:

  ```bash
  gh api repos/<owner>/<repo>/pulls/<number> -X PATCH -f body="$(cat body.md)"
  ```

- Once the pull request exists, set `Queue: Next` on the issue it closes, through the same `gh project item-edit` call that placed the issue. Do this for every pull request, whether the issue was opened for this task or had been sitting in `Later` for weeks, and treat a value that did not land as an incomplete gate rather than as a detail to fix later. Nothing else writes the field afterwards: the issue keeps `Next` until the merge closes it out of every view that reads `Queue`.
- That write never overwrites `Queue: Parent`. A pull request closes the issue that does the work rather than the parent grouping it, so a `Closes` reference pointing at a parent is a defect to correct in the pull request body rather than a value to write over — which is the one case where not setting `Next` is the correct outcome rather than a gate that failed.

Writing it from the skill is not a shortcut past the automation; it is the only place the write can happen. The board's built-in workflows set `Status` and nothing else, so no project automation reaches a custom single-select field. A GitHub Actions workflow could, but this board belongs to a user rather than to an organization, and nothing scoped can write to a user's Projects v2 — a GitHub App has no permission that covers one, and a fine-grained token has no `Projects` scope at the account level. Only a classic token with the `project` scope can, and that scope is account-wide: storing one as a repository secret would give every workflow run write access to all of the owner's projects, to save a step in a skill that already holds such a token and already talks to this board. The cost is that a pull request opened outside `$finish-change` moves nothing, which for a repository whose pull requests are all opened by agents is a smaller gap than the credential would be.

## Status transitions

- The board's `Status` field has `Todo`, `In progress`, `Ready to merge`, and `Done`.
- The board's built-in workflows own every transition: `Auto-add to project` places a newly opened issue on the board and `Auto-add sub-issues to project` places one opened beneath a parent, `Item added to project` puts either in `Todo`, `Pull request linked to issue` moves it to `In progress`, `Code review approved` moves it to `Ready to merge`, `Code changes requested` moves it back to `In progress`, and `Pull request merged`, `Auto-close issue`, and `Item closed` carry it to `Done`. Those are the names the board itself uses, which is what a reader checking whether one is enabled will look for. Do not set those statuses by hand; a manual status that contradicts the automation hides the real state.
- `Ready to merge` is a review verdict rather than a fourth phase of the work, which is why `Code review approved` and `Code changes requested` are a pair: approval moves an item into it and a later review requesting changes moves it back out, so the value always reflects the newest verdict rather than the first one. It is what separates a pull request waiting on the owner's merge from one waiting on the owner's reading, and neither of them from work still being written — a distinction `Queue` cannot draw, because every one of those items is legitimately `Next`.
- `Status` records what has happened and `Queue` records what is intended, which is why neither substitutes for the other. Work that stalls keeps whatever `Status` the automation gave it and moves to `Later` or `Parked` in `Queue`.
- Automation does not add an issue that is already closed when it is created. Add a retrospective `shipped` issue to the board explicitly and set it to `Done`.
- When work stops without merging, say so on the issue and leave the status to the automation rather than moving the card.
