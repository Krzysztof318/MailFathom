---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-10-17
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Merge through a queue that tests the tree a merge produces, stop a pull request only for a related change that landed, and reuse a verdict wherever the tree is one a run already tested

<!-- describes: .github/workflows/ci.yml, .github/workflows/coherence.yml, .github/workflows/contributor-licence.yml, .github/workflows/protected-paths.yml, .github/workflows/typo-check.yml, .github/coherence/** -->

## Context and Problem Statement

The `main` ruleset required a branch to be current with `main` before it merged. That rule exists so
that every merge is built and tested on the tree it produces, and it bought that guarantee by making
every author rebase whenever anything else merged. With several agent sessions and contributors
working at once, each merge forced a rebase, a `CI` run of about seven minutes, and a `Fathom review`
pass on every other open pull request, and approved pull requests spent their time chasing `main`
rather than merging. Of 200 merged pull requests over 28 days, 80 were forced into a rebase by the
rule and 58 of those 80 were related to what had landed; the other 22 rebased for nothing.

What the rule protected was two things at once: that two changes green on their own cannot break
`main` together, and that a change landing underneath a pull request — a page it should now agree
with, a rule it should now follow — is read before the pull request merges. How can both be kept
without asking an author to rebase for a change unrelated to theirs?

## Decision Drivers

- Every merge is built and tested on exactly the tree it lands, with no window in which two green
  changes meet for the first time on `main`.
- An unrelated merge costs an open pull request nothing: no rebase, no new run, no new review.
- A related merge still stops the pull request, and says which files to read together.
- A tree is built once. A verdict a run already earned on a tree is not earned again on the same tree.
- *Is `main` green* stays a recorded fact per commit, and a merge that bypassed every rule is still
  attributed to itself.

## Considered Options

- Keep the up-to-date rule.
- Drop the up-to-date rule and rely on the `push` run on `main` to catch a combined break.
- Replace it with a merge queue, a `Coherence` check for related changes, and verdict reuse.

## Decision Outcome

Chosen option: "Replace it with a merge queue, a `Coherence` check for related changes, and verdict
reuse", because it is the only option that tests the combined tree before it lands while letting an
unrelated merge pass a pull request by.

- The ruleset sets `strict_required_status_checks_policy` to `false`, requires `Coherence` beside the
  other four checks, and adds a `merge_queue` rule: squash, `ALLGREEN`, `min_entries_to_merge` 1 with
  no wait, `max_entries_to_build` and `max_entries_to_merge` 5, and a 30-minute check-response
  timeout. When the organization's concurrent-job limit binds, `max_entries_to_build` is lowered
  rather than the timeout raised.
- Every workflow publishing a required check runs on `merge_group`. `Required CI` builds and tests the
  group's tree in full. `Coherence` asks its question again against everything that landed under the
  entry, the entries ahead of it included. `Protected paths`, `Typo check`, and `license/cla` are
  properties of the pull request rather than of the combination, so each carries the verdict the
  pull request's head already holds onto the group's commit.
- `Required CI` on a merge group reuses the pull request's own successful run when the head already
  contains the group's base, because the group then squashes to exactly the head's tree. Containment
  is asked rather than tree equality alone, since it is what guarantees the pull request's run tested
  that tree rather than a merge with an older `main`.
- `Required CI` on a push to `main` reuses the verdict the merge group earned on the same commit,
  which is every commit the squash queue merged. A commit no green merge group carries — a merge
  that bypassed the queue — gets the full run.
- Locally, both gates keep refusing a branch that does not contain its base. Before publication a
  rebase costs nothing, and a current branch is the one whose queue entry is reused rather than
  rebuilt.

### Consequences

- Good, because an approved pull request merges without rebasing for an unrelated change, and the
  rebase, the `CI` run, and the review pass that rebase forced are gone with it.
- Good, because two changes that are green alone and broken together are stopped in the queue rather
  than reported on `main`.
- Good, because a pull request that was current and first in the queue costs one build, not two, and
  a queue merge's push to `main` costs none.
- Bad, because a pull request the queue removes has to be read again by its author, and the push that
  fixes it dismisses the approval like any other push.
- Bad, because the queue adds a required-check event every workflow has to answer, and a required
  check that does not answer it holds every pull request until the timeout. The contract suite
  asserts that each of the five workflows runs on `merge_group`.
- Neutral, because the reuse rests on GitHub moving `main` to the merge group's own commit. Where it
  does not, the push finds no verdict and runs in full, which is the run it had before.

## Pros and Cons of the Options

### Keep the up-to-date rule

- Good, because it needs nothing new.
- Bad, because every merge forces a rebase, a run, and a review on every open pull request, most of
  them for a change unrelated to theirs.

### Drop the up-to-date rule and rely on the `push` run on `main`

- Good, because it is a single setting.
- Bad, because a combined break is then found on `main` after it landed, and a related change landing
  underneath a pull request is never read before the merge.

### Replace it with a merge queue, `Coherence`, and verdict reuse

- Good, because it keeps both halves of what the up-to-date rule protected and drops its cost for
  every unrelated merge.
- Bad, because every required check has to answer a second event, and an author removed from the
  queue has to read why.
