---
name: prepare-release
description: Manual only. Invoked by the owner to cut a MailFathom release — composes the changelog, raises the declared version, settles the milestones and the next release's tracking issue, and states the order the two pull requests and the tag have to land in.
disable-model-invocation: true
license: Apache-2.0
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/Krzysztof318/MailFathom
---

# Prepare Release

**Manual invocation only.** Cutting a release is the owner's decision about when a version becomes real, not something to infer from a task looking release-shaped. `disable-model-invocation` in the frontmatter above is what enforces that: an agent cannot reach this skill on its own, and a release begins when the owner asks for one.

Prepares the two pull requests a release consists of and then hands the sequence back to the owner.

A release under [ADR 0004](../../../docs/decisions/0004-versioning-and-release-policy.md) is three steps, and only the
middle one is a single command: a reviewed pull request closes the changelog, an annotated tag on that merge commit is
what makes the release real, and a second pull request returns `main` to naming the *next* release. The tag has to land
between the two, and nothing inside a pull request can express that ordering — which is the whole reason this skill
prints it rather than automating it.

**This skill pushes no tag and merges nothing.** Tagging is the moment a release becomes real and stays a deliberate act
by the owner. Everything here is reversible by closing two pull requests and the issue tracking the release that did
not happen.

## What the version is

Read, never asked for. The version being released is the `VersionPrefix` declared in `Directory.Build.props`, because
that is the number the build already stamps into every assembly and every artifact; asking would let this skill name a
release the build would not produce.

```bash
scripts/read-declared-version.sh
```

The version being bumped *to* depends on which branch the release is cut from:

| Branch | Releases | Bumps to | Overridable |
| --- | --- | --- | --- |
| `main` | every major and minor | the next **minor** | the next major, on request |
| `release/<major>.<minor>.x` | every patch, and nothing else | the next **patch** | no |

**Never propose a patch from `main`.** By the time `0.2.0` needs a fix, `main` already carries everything intended for
`0.3.0`, and a patch released from it would ship that work under a number promising nothing had changed. A patch is cut
from the permanent `release/<major>.<minor>.x` branch, which the ADR's *patch flow* section describes.

## Refusals

Stop and report, without creating anything, when any of these holds:

- **The working tree is dirty.** The release describes a tree, and an uncommitted change is not in it.
- **Nothing has merged since the previous tag.** There is nothing to release. Re-publishing an identical tree under a
  new number makes the number mean less rather than more.
- **A tag `v<x.y.z>` already exists** for the version being released, locally or on the remote. A released version is
  never re-cut; the release workflow rejects it anyway, and finding out here costs nothing.
- **`CHANGELOG.md` already carries a `## [x.y.z]` section.** That version has been prepared, and possibly released.
- **The branch is not `main` or `release/<major>.<minor>.x`.** No other branch releases.

## Workflow

### 1. Establish the two versions and confirm the increment

```bash
git fetch --tags origin
git status --porcelain            # must be empty
git branch --show-current
scripts/read-declared-version.sh  # the version being released
git tag --list 'v*'               # must not contain the version being released
```

### 2. Read what merged since the previous tag

This is the whole substance of the release, and it is read now rather than accumulated as work went along.
`CHANGELOG.md` is written here and nowhere else: an entry added while implementing a task would be a claim about a
release nobody had yet decided to cut.

```bash
git log --merges --pretty='%h %s' "$(git describe --tags --abbrev=0)..HEAD"
gh pr list --state merged --search 'base:main merged:>=<date of previous tag>' --limit 100 \
  --json number,title,url,closingIssuesReferences
```

**The reader is somebody who runs MailFathom** — the person installing it and the administrator keeping it running,
reading before an upgrade to find out what is new for them, what was fixed, what breaks, and what they have to do about
it. It is not a reader of this repository, and the section is published where that reader is: the release notes are
this section, and the documentation site carries the file. That settles both halves of this step, what survives the
reading and how it is written.

Keep what a consumer of a release would notice — anything reaching the MCP tool contract, the configuration schema, the
database schema, or the deployment contract, plus a defect that was observable from outside and anything with a
security consequence. Drop the rest. A refactor, a test, a continuous-integration adjustment, a documentation edit, and
an internal rename earn no entry. Completeness is not the standard and pursuing it is what makes a changelog stop being
read: forty entries nobody finishes hide the four that decide an upgrade.

**Then write what survives in the reader's terms rather than in the terms it arrived in.** What this step has in front
of it is a list of pull requests, each titled by the person who made the change and in the vocabulary of the code it
touched, so an entry is a translation rather than a copy of one. State the behavior that is observable from outside,
what it means for an installation already running, and the action to take — the configuration key, the tool argument,
the default that moved, the failure that stops happening. Leave the mechanism out: the type introduced, the layer
restructured, the abstraction extracted, the package swapped, the test that proved it. Where a name from inside the
process has to appear because an operator matches on it — a logger category, a metric, a table — it appears as the
thing they update rather than as what changed internally.

An entry that resists this is usually an entry that should not exist. A change with nothing to say to that reader is a
change they cannot observe, and the correct entry for one is none.

#### A feature the release delivers only part of

That last sentence deletes the entry for a change too small to be noticed. This is the case it does not reach: a change
large enough to matter that is one part of a feature still being assembled. `docs/operations/issue-tracking.md` splits
such a feature across a parent issue's children, so it arrives here as several closed issues whose own titles name
pieces — and the instruction to drop the mechanism and state the capability then has exactly one capability-shaped
sentence within reach, which is the parent's. That sentence is a claim about the whole feature, and the release
delivered part of it. Nothing in a pull request says which case it is in, which is why the parents are read rather than
inferred.

Resolve each issue the merged pull requests closed against its parent. The sub-issue links are the source of truth for
the hierarchy — the `parent` label mirrors them for the board and can be stale — so the lookup follows the link, and
only GraphQL exposes it:

```bash
for issue in <the numbers closingIssuesReferences returned above>; do
  gh api graphql -f query="{ repository(owner: \"Krzysztof318\", name: \"MailFathom\") {
    issue(number: $issue) { number parent { number title state subIssuesSummary { total completed } } } } }" \
    --jq '.data.repository.issue | select(.parent != null)
          | "#\(.number)\t#\(.parent.number) \(.parent.state) \(.parent.subIssuesSummary.completed)/\(.parent.subIssuesSummary.total)\t\(.parent.title)"'
done
```

Read each closed issue against what that returns. Two cases decide how far the entry may reach:

- **It has no parent, or its parent has no child left open.** The feature is whole, and the entry names the capability
  exactly as everything above describes. Ask the same question of the level above when that parent is a sub-parent: the
  hierarchy is two deep at most, and a finished part of an unfinished feature is still a part.
- **Its parent still holds open children.** The release carries a part of the feature. The entry states what the reader
  can do *now* and nothing past it, and where nothing they can do moved, the entry is none — the same answer the
  paragraph above gives, arrived at for a different reason. **The parent's capability is never written as an entry**, in
  any tense: a sentence about what a release lays the ground for is read as a sentence about what it ships.

**A third case cuts across both rather than standing beside them: the issue carries an action for the operator.** A
break, a migration, a new required configuration key, a default that moved. That entry follows from the action alone
and is written whether or not anything can yet use the feature around it — which is what keeps this from being a rule
that only deletes, because the groundwork for a capability nobody can invoke is still a table somebody has to migrate
onto. Write the action, and no more of the feature than the case above allows.

**A parent left open is the expected shape here rather than a fault to correct.** A release never waits for one to
close, and `cross-milestone` beside `Queue: Parent` says that feature was always going to arrive in stages. Nothing in
this reading blocks the release or moves an issue either: step 3 is what carries whatever is still open into the next
milestone, and it does so for every open item rather than for a parent's children in particular.

**The withheld sentence is not lost, it is early.** The release that closes the last child is where the capability is
named, in the reader's terms and truthfully, and that is the release they would act on anyway. Writing it sooner buys
them nothing and costs the file the only thing it has, which is that an entry in it can be believed.

**The increment follows from what this reading found**, not the other way round: the highest increment any of the four
surfaces requires is the release's own. Raise the question with the owner when the entries and the version already
declared disagree — an unnecessary major costs one careful upgrade, an unmarked break costs an outage.

### 3. Settle the milestones

The milestone is the release's gate, so it is closed as part of cutting the release rather than left to be tidied
afterwards. Nothing here is inferred: **what is still open in the milestone is scope the owner is deciding about**, so
ask before moving anything unless they already said what to do with it.

1. **Create the next milestone if it does not exist.** Its name is the version being bumped to, per the table under
   **What the version is**. `docs/operations/issue-tracking.md` forbids opening a further milestone beside the open
   one — the next is created *when that one closes*, which is this step and nowhere else, so creating it here
   satisfies that rule rather than bending it.
2. **Open the next release's tracking issue in it, unless one already exists.** `Cut and publish the <next> release`
   is what the next run of this skill closes, and this is the moment it has a milestone to belong to — a milestone
   created without it is a release nothing tracks until somebody remembers. It is a no-op when the issue exists, which
   is what makes running this step against a milestone opened before this rule existed safe.
3. **Move what is still open into it, except the issue tracking this release.** A release cut over an open milestone
   item releases the gap; moving the item says the work is still accepted and names the release it now belongs to. An
   item the owner would rather drop is closed as `not planned` on its own issue instead, which is their call and not
   one to make on their behalf. **The tracking issue is the one exception and it is not incidental**: it is open at
   this point in the sequence and it is in this milestone, so a query for what to move returns it, and moving it would
   file the release under the release *after* it. The issue step 2 just opened is already in the *next* milestone, so
   the same query never sees it.
4. **Close the milestone being released.**

```bash
gh api 'repos/Krzysztof318/MailFathom/milestones?state=all' --jq '.[] | "\(.number) \(.title) \(.state)"'
gh api -X POST repos/Krzysztof318/MailFathom/milestones -f title='<next>'          # only when it does not exist

# The next release's tracking issue. Search the milestone first: an issue opened by a previous run must not be opened
# a second time, and a second one would be two issues closing one release.
gh issue list --repo Krzysztof318/MailFathom --milestone '<next>' --state all \
  --search 'Cut and publish in:title' --json number,title
gh issue create --repo Krzysztof318/MailFathom --title 'Cut and publish the <next> release' \
  --label type:workflow --milestone '<next>' --body-file <body>    # only when the search found none

# What to move: open issues in the milestone being released, minus the tracking issue. `/issues` returns pull
# requests as well, so both exclusions are the query's rather than the reader's to remember.
gh api "repos/Krzysztof318/MailFathom/issues?milestone=<old>&state=open&per_page=100" \
  --jq ".[] | select(.pull_request == null) | select(.number != <tracking issue>) | .number"

gh api -X PATCH "repos/Krzysztof318/MailFathom/issues/<n>" -F milestone=<new-number>
gh api -X PATCH "repos/Krzysztof318/MailFathom/milestones/<old>" -f state=closed
```

The new issue is placed like any other, through the calls `docs/operations/issue-tracking.md` § *Board fields* holds:
`Area: Release`, `Queue: Later` — it names a release nobody is cutting yet, and step 6 is what moves it to `Next` when
its pull requests exist — and `Size: S`, because both diffs together are a changelog section, three prose files, one
property, and the lock files.

#### What the tracking issue says

The body is short by design. What the release *carries* is read at the moment it is cut, in step 2, and written in
`CHANGELOG.md` by the pull request in step 4; an issue opened a release earlier cannot know it, and a second list
written later would be a claim about the release that nothing keeps true. So the issue states what is fixed about
every release — the ordering, what gets published, and what follows the tag — and links the changelog for the rest:

```markdown
## Context

**No numbered specification backs this issue.** It is the release itself: cutting, tagging, and publishing `x.y.z`
from the work placed in the `x.y.z` milestone. #<the previous release's issue> was the same issue for the release
before it.

The procedure is decided and recorded:
[ADR 0004](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md)
settles what the number means and where it comes from, `docs/operations/release-procedure.md` records the sequence, and
`$prepare-release` prepares the two pull requests and prints the order they and the tag between them have to land in.

## What this release carries

Whatever is placed in this milestone. It is read from what merged since the previous tag at the moment the release is
cut, and stated in the `CHANGELOG.md` section the release pull request writes; this issue does not restate it.

## Acceptance

- The changelog pull request `[#<issue>] Prepare release x.y.z` merges first; **its merge commit is what is tagged and
  published**, so the tagged tree carries the released changelog and the prose files naming the release they ship in.
- An annotated `vx.y.z` tag is pushed on that merge commit, by the owner. Nothing else pushes it.
- The multi-architecture image reaches **both** GHCR and Docker Hub under one digest, tagged `x.y.z`, with `latest`
  moved onto that digest in both; the packaged chart states `appVersion: x.y.z`; the GitHub release carries
  `mailfathom-schema-x.y.z.sql` with its checksum and the `mfctl` binaries with theirs; the release notes are the
  changelog section rather than a list of pull-request titles.
- `SECURITY.md` names `x.y` as the supported line, and the line it replaces moves down a row.
- The version-bump pull request `[#<issue>] Bump main version to <next>` merges after the tag and closes this issue.
- The published image and chart are installed and verified against a real deployment before the release is announced.
```

**An open milestone item is not a precondition of any of that.** The move above carries what is still open into the
next milestone as part of cutting the release, so the issue does not repeat it as something to do first: a release
waiting on a cleared milestone would be waiting on a step the release itself performs.

**That exception is why the milestone is closed holding one open issue.** The tracking issue closes on the version-bump
merge, which is the last of the three steps this skill prints and therefore long after this, so a closed milestone with
one open item is the expected shape here rather than an oversight, and it resolves itself.

This step belongs to the owner's checkout alone. In the fork role a milestone write returns a permission error, which
is the correct outcome rather than a partial one: nobody but the owner cuts a release of this repository.

### 4. Open the changelog pull request

**Its title is `[#<issue>] Prepare release x.y.z`**, naming the issue that tracks the release. **It carries no `Closes`
line**, because the release is not finished when this merges: the tag is not pushed and `main` still names the version
being released. The version-bump pull request closes the issue, for the reason step 5 gives.

On a branch off the release branch, and touching nothing else:

- add `## [x.y.z] - YYYY-MM-DD` above the previous section, using the release date in UTC, with the entries from step 2
  grouped into the six Keep a Changelog categories and each referencing the pull request or issue that carried it;
- open the section with a short paragraph in the reader's own terms: what this release is for, whether the database
  schema moves, and whether anything the previous release promised is withdrawn. That is what somebody deciding whether
  to upgrade reads, and it is the one part no list of entries can state. It names no capability the parent reading in
  step 2 found still being assembled — a summarizing paragraph is where a half-delivered feature is easiest to promise
  and where the qualification reads worst;
- **add no `Unreleased` section.** The file carries none by design: it says what released versions shipped, and a
  heading standing above the newest one either says nothing or claims something about a release nobody has cut. What
  has merged since the newest section is what a nightly build carries, and the file's own preamble says so;
- open a breaking entry with `**Breaking (<surface>)**` and state the operator's action, not only the fact;
- say, when the database schema moved, whether a migration must be applied, whether it applies while the previous
  version still runs, and whether the release deploys over the previous release's data at all;
- update the link references at the foot of the file so the new section resolves;
- leave `VersionPrefix` alone. It already reads `x.y.z`, which is what makes the tagged tree self-consistent;
- **bring the files that name a version in prose onto `x.y.z`**, per the list below;
- **sweep the tree for prose that describes the release state**, per the pass after it.

Nothing else belongs in this diff. **This is the pull request whose merge commit is tagged and published**, so it is
both the last point at which the release's contents are read as a whole and the thing the published artifact is built
from; an unrelated change in it is a change nobody reviewed as part of the release. `CHANGELOG.md` is a protected path,
which is what makes an edit to it outside this flow visible.

#### The files that name a version in prose

`<VersionPrefix>` is the only place a version is written for the *build*. Three files additionally name one in prose,
where nothing derives it and nothing checks it, so they are read here by name rather than left to be noticed:

| File | What to bring onto `x.y.z` |
| --- | --- |
| `README.md` | The **Project status** paragraph — which release is current and what it ships — and the **Where the artifacts are published** table whenever a release starts or stops attaching one |
| `docs/users/README.md` | The **The state of the release** section — which release is current, and what a page is allowed to describe as already downloadable |
| `SECURITY.md` | The **Supported versions** table. `x.y` becomes the supported line and the one it replaces moves down a row, per ADR 0004's rule that only the newest released minor is patched by default |

**They belong in this pull request rather than the bump one, and the reason is what the whole ordering rests on:** this
diff's merge commit is what gets tagged, so it is the tree an operator reads at `v<x.y.z>`. A `SECURITY.md` corrected
after the tag names the previous line in the artifact people actually download, and `docs/` at a tag is read far more
often than `docs/` on `main`. The bump pull request cannot carry them for the same reason it cannot carry the changelog.

Three files each is a decision rather than an accident, and it is the ceiling rather than a starting point. **A page
that quotes a version because a reader substitutes one writes `<version>` and stays off this list**: the image
references and the `mailfathom-schema-<version>.sql` filename in `docs/users/installation.md`,
`docs/operations/database-schema.md`, `docs/operations/deployment-compose.md`, and
`docs/operations/deployment-kubernetes.md` were literal until they were placeholders, which is four more files a
release had to touch and four more places it could miss. A command quotes the placeholder — `--file
'mailfathom-schema-<version>.sql'` — so a line pasted unedited fails with a missing file rather than with a shell
redirection. Only prose asserting *which release is current* has to name one, and that is what the three rows above
are. Reach for the placeholder first and this table second.

Nothing gates this, deliberately: no check can tell prose describing the release from prose quoting a version as an
example, and one that tried would be satisfied by a search-and-replace through `docs/`. The list above is short and
fixed instead, and a file joining it is an edit to this table — but a file that can take a placeholder instead should
take one, and never join it at all.

#### The release-state sweep

The list above catches a file that writes the version *number*. It cannot catch the other half, which is prose
asserting where the project stands relative to a release without naming one — "no versioned artifact exists yet", "a
release will attach it", "the first release is milestone `0.1.0`". A sentence like that is invisible to any search for
`x.y.z`, goes stale at the moment of the tag rather than gradually, and is read by exactly the people a release is
for. So it is swept for here rather than added to a list, because the sentences that will be wrong next time are not
the ones that were wrong last time.

The sweep is one reading pass over what the search below turns up. It is not a search-and-replace and its output is
not a list of edits:

```bash
git grep -nEi \
  'not (yet )?(been )?(released|published|distributed)|no (versioned |published |binary )?(artifact|release|image|chart) (exists|yet)|first release is|has not had|until (the )?(first )?release|is (still )?pre-?release|unreleased|no release (has|yet)|a release will' \
  -- ':(glob)**/*.md' ':(glob)**/*.yaml' ':(glob)**/*.yml' \
     ':(exclude)CHANGELOG.md' ':(exclude)docs/decisions/**' ':(exclude).agents/skills/prepare-release/**'
```

It is written to return a handful of lines rather than a page of them, because a pass that reports ninety hits is a
pass nobody reads to the end. Widen it when a release turns up a stale sentence it missed, and record what the new
alternative is for.

One shape is deliberately left to the table above rather than added here: prose naming a version as the threshold a
capability arrives at — "from `0.2.0` each release attaches". Catching it needs a pattern anchored on a version number,
and every such pattern also matches the package versions filling `THIRD_PARTY_LICENSES.md` and the specifications,
which is a page of noise to catch a sentence the table can simply name. A version threshold is a file to list, not a
phrase to search for.

Read every hit against the tree being tagged and settle it one of three ways:

- **Stale.** The sentence describes a state this release ends. Correct it in this pull request.
- **Still true, and about a *later* release.** A page saying a capability arrives with the next version is accurate and
  stays. Confirm the version it names is still the right one — a feature deferred out of this release has to name where
  it went.
- **Not about the release at all.** The pattern is deliberately wide, so it matches specification prose, an ADR's
  reasoning, and an example. Leave it.

The exclusions are deliberate. `CHANGELOG.md` is the one file whose historical entries *should* read as claims about
past releases, and rewriting one would be falsifying a record. `docs/decisions/` is excluded because an accepted ADR is
closed: it records what was true when the decision was taken, and is replaced rather than brought up to date. This
skill excludes itself because the patterns above are quoted in it.

Three files reach further than `docs/` and are worth reading with the release in mind whether or not the search names
them: `THIRD_PARTY_LICENSES.md` states whether redistribution obligations are outstanding, which the *first* release of
a line changes; `CONTRIBUTING.md` and the root `README.md` both characterize the project's maturity to somebody
deciding whether to depend on it.

Report the count and the disposition — swept, corrected, left — in the pull request body, so a reviewer can see the
pass happened without re-running it. A release that corrects nothing is an ordinary outcome and is stated as one.

### 5. Open the version-bump pull request

**Its title is `[#<issue>] Bump main version to <next>`, naming the same issue the changelog pull request does, and it
carries `Closes #<issue>`.** One release is one unit of work, and it is finished when `main` names the next version
rather than when the changelog merged — a tracking issue closed before the tag says the release happened while the two
steps it exists to track are still outstanding. The pair therefore reads as `[#398] Prepare release 0.3.0` and
`[#398] Bump main version to 0.4.0` in the pull-request list, which says which one is the release and which one follows
it without either being opened.

On a second branch off the release branch, raise `<VersionPrefix>` in `Directory.Build.props` to the next version from
the table above. That property is the only place a version is *written*: the image's tags and labels arrive as build
arguments, and the chart's `version` and `appVersion` are both supplied at package time from the same declaration.

**It is not the only place one is recorded.** Every `packages.lock.json` writes the version of each `MailFathom.*`
project it references — `"MailFathom.Domain": "[x.y.z, )"` — so raising the declaration leaves those files naming a
version the tree no longer has. They are part of this diff, and of no other: this is the pull request where the number
they record stops being true.

```bash
dotnet restore MailFathom.slnx --force-evaluate
git diff -U0 -- '**/packages.lock.json' | grep -E '^[+-][^+-]' | grep -v 'MailFathom\.'
```

**The second command has to print nothing.** A bump moves project versions and nothing else, so any other line is a
transitive resolution that moved for its own reasons — a package published since the last regeneration — and a
dependency change hidden inside a version bump is a dependency change nobody reviewed. Revert that file and raise it as
its own pull request. Where one turns up and the release should not wait for it, correct the versions in place instead
— the edit is mechanical, and what it writes is that same diff without the lines that did not belong:

```bash
git grep -lE '"MailFathom\.[A-Za-z]+": "\[' -- '**/packages.lock.json' \
  | xargs sed --in-place --regexp-extended 's/("MailFathom\.[A-Za-z]+": ")\[[0-9]+\.[0-9]+\.[0-9]+, \)/\1[<next>, )/'
```

Either way, confirm that nothing still names another version. This is checked against `<next>` rather than against the
version just released, because a lock file records whatever was current when it was last regenerated, which is not
necessarily one release back:

```bash
git grep -hoE '"MailFathom\.[A-Za-z]+": "\[[0-9]+\.[0-9]+\.[0-9]+' -- '**/packages.lock.json' | grep -v '<next>'
```

`AppHost` and `IntegrationTests` carry no lock file and are not given one here. Root `AGENTS.md` records why: the
Aspire SDK picks part of their graph from the host platform's runtime identifier.

**Nothing gates any of this, which is what makes it a step rather than a check.** Locked-mode restore does not compare
a project reference's version, so a lock file naming a version that has been released and superseded restores green in
every workflow and in both verification scripts. The cost lands elsewhere and later: `--force-evaluate` writes the
truth whenever it is next run, so a pull request moving one central pin arrives carrying every skipped bump beside it,
and its reviewer has to tell that drift apart from the transitive closure the change actually moved.

**Do not touch `deploy/helm/mailfathom/Chart.yaml`.** Its `version` is a `0.0.0` placeholder and it declares no
`appVersion` at all; the release run supplies both, as one number equal to the application's. A chart version counting
edits to the chart directory would need raising on every release anyway — a packaged chart embeds its `appVersion`, so
each release produces chart content that differs from the last, and a published chart version is immutable — and it
would leave an operator mapping two numbers onto one artifact.

**Do not bring the prose files here either.** They name the release that has just been *published*, and this pull
request merges after the tag — so a `README.md` corrected here is a `README.md` that was wrong in the tagged tree.
Step 4 owns them.

### 6. Open both, and cross-reference them

Each body names the other by number, so neither is merged alone by accident. Both name the issue that tracks this
release in their titles, and the version-bump one carries the `Closes #<issue>` line, so the issue stays open across
the tag and closes when the release is actually finished.

**Then set `Queue: Next` on the tracking issue.** `docs/operations/issue-tracking.md` § *Linking a pull request to its
issue* makes that write part of opening a pull request, and it is this skill's to perform: `$finish-change` is what
writes it everywhere else, and no step here invokes it. Without the write the release is the one piece of work in
flight that the board's `Now` view cannot see, for exactly the weeks it is being cut. It sits outside the owner's
five-slot cap, as every write triggered by a pull request does.

```bash
gh project field-list 4 --owner Krzysztof318 --format json   # field ids and option ids
gh project item-list  4 --owner Krzysztof318 --format json   # item id for the tracking issue
gh project item-edit --project-id <project-id> --id <item-id> \
  --field-id <Queue field-id> --single-select-option-id <Next option-id>
```

Confirm the value landed, as that page requires: nothing else writes the field, so one that did not land is an
incomplete step rather than a detail. Where the board probe returned read access this write does not exist; say so
rather than reporting it as skipped.

### 7. Print the ordering, and stop

Report exactly this to the owner, with the numbers filled in:

```text
Release x.y.z is prepared. Merge in this order — the tag has to land between the two.

1. Merge the changelog pull request (#A).
   Its merge commit is what gets tagged and published, so the tagged tree contains the released changelog rather
   than describing it afterwards.

2. Push the annotated tag on that merge commit:
       git tag --annotate vx.y.z --message 'MailFathom x.y.z'
       git push origin vx.y.z
   This is what triggers the release workflow. Before publishing anything it asserts the tag against VersionPrefix,
   against the highest existing tag on the same major.minor line, and against the changelog section for this version.
   It then publishes the image under vx.y.z to both registries as one manifest list under one digest, moves `latest`
   onto that digest — `latest` follows the newest release and never a nightly — and publishes the Helm chart against
   the digest the image publication produced, at the same version.

3. Merge the version-bump pull request (#B).
   After the tag, so main returns to naming the next release rather than the one just published. This is what
   closes the tracking issue (#C): the release is finished here rather than at the changelog merge.

The next milestone <next> is open and carries its own tracking issue (#D), which the next release closes.
```

## When a step fails

- **The tag is rejected.** Either step 1 did not merge, so the tagged commit's `VersionPrefix` is not `x.y.z`, or the
  two disagree for another reason. Check out the tagged commit and compare `scripts/read-declared-version.sh` against
  the tag. Do not force the tag; delete it, fix the disagreement, and tag again.
- **The tag names a version that already exists on its line.** The bump pull request from a previous release never
  merged, which is visible before the tag as well: that release's tracking issue is still open. Merge it, then re-cut.
- **Publication fails partway.** Re-run the `Release` workflow on the same tag rather than rebuilding anything. It
  reconciles: a version both registries already carry from this commit is left alone, and one only a single registry
  carries is copied across by digest, so the artifact that reaches the second registry is the one the first published.
  A rebuild would produce a second artifact for one version, which is what the immutability assertion exists to
  prevent. `docs/operations/release-procedure.md` records the whole sequence and what each failure means.
- **The release is abandoned before the tag.** Close both pull requests. Nothing was published and no tag exists, so
  there is nothing to undo. The tracking issue stays open and keeps its milestone: the release it names has not
  happened, and the run that eventually cuts it is what closes it. The milestone work of step 3 is not undone either —
  the next milestone and its own tracking issue already exist, and a later run finds both and creates neither.
- **The release is abandoned after the tag.** It is not abandoned; it is released. Cut a patch from the release branch.

`docs/operations/release-procedure.md` records the same sequence for a reader who does not have this skill.
