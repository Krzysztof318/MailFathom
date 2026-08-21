---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-21
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Take contributions under a licence agreement broad enough to relicense, and record acceptance in this repository rather than in a service

<!-- describes: CLA.md, .github/workflows/contributor-licence.yml -->

## Context and Problem Statement

MailFathom has exactly one author, so every right in the work is still held in one place. Apache-2.0 section 5 puts an inbound contribution under Apache-2.0 by the act of submitting it, which is what makes a pull request usable; it grants nothing wider. It carries no right to distribute that contribution under any other terms, and no statement that the contributor was entitled to submit it at all.

Both gaps close on the same event. The first external contribution merged without an agreement makes any later licensing decision — a different open-source licence, a commercial licence sold beside it, a move into a foundation — conditional on finding every past contributor and obtaining individual consent, which one unreachable or unwilling person is enough to defeat. The second gap has no closing event: nothing in the licence ever asks whether the code was the contributor's to give, and that is the one defect a follow-up commit cannot repair, because it has to come out of the history.

The question is whether to take an agreement now, while there is nobody to ask retroactively, and where the evidence of acceptance lives. It is not a question about MailFathom's own licence, which stays Apache-2.0 and is untouched here.

## Decision Drivers

- The window is open exactly until the first external contribution merges, and closes without notice.
- An agreement is asked of people who owe this project nothing, so its cost to them decides whether it is answered.
- Acceptance is evidence, and evidence is worth what its custody is worth.
- The contributor's identity is personal data, and this project classifies personal data as sensitive by default.
- MailFathom stays Apache-2.0. An agreement that reads as an announced licence change buys the cost of one without the decision.

## Considered Options

- Keep Apache-2.0 section 5 alone.
- Adopt a Developer Certificate of Origin.
- Adopt a contributor licence agreement, collected by `contributor-assistant/github-action`.
- Adopt a contributor licence agreement, collected by `cla-assistant.io`.
- Adopt a contributor licence agreement, collected by this repository's own workflow, recorded on a branch of this repository.

## Decision Outcome

Chosen option: "collected by this repository's own workflow, recorded on a branch of this repository", because it is the only option that closes both gaps and keeps the evidence in the custody of the party who will one day have to rely on it.

`CLA.md` grants a licence broad enough to sublicense and to publish under other terms, restates the patent grant so it survives such a change, and carries the representation of entitlement and the employer clause section 5 lacks. It transfers no ownership: the contributor keeps their copyright and their own rights are undiminished, which is what makes it an agreement rather than an assignment.

The agreement is assignable, and that clause is there for the same reason the agreement is. The rights are held today by a natural person; they may later be held by a company the owner forms or by an acquirer, and under Polish law a transfer of economic copyright is void unless made in writing. An agreement naming only the present holder would leave every contributor to be found again at exactly the moment that is most expensive — which is the failure this decision exists to prevent, one level up. Clause 2.4 therefore names the successor in advance and takes nothing further from the contributor: an assignee stands in the owner's place and can enlarge nothing that was granted.

Acceptance is one comment on the pull request the contributor already opened, from the account that authored it. The `Contributor licence` workflow reads the comment, appends an entry to `signatures.json` on the `cla-signatures` branch, and publishes a `license/cla` commit status. An author whose `author_association` is `OWNER`, `COLLABORATOR`, or `MEMBER`, or whose account is a bot, is passed without being asked — those are the accounts that already hold write access, and the third of them arises only if this repository ever moves under an organisation.

Three consequences of that shape are decisions in their own right:

**The record is a branch of this repository rather than a file on `main`.** A signature is evidence, not a change to review, and `main` admits no direct push under its own ruleset — routing every acceptance through a pull request would make the contributor's first act depend on a second one by the maintainer. An orphan branch is in the repository, travels with a clone, is versioned, and is readable by anyone, while carrying nothing that has to pass review. Its own ruleset admits the `Fathom license` App and nothing else, so the register is not editable by the party it is evidence against, including the owner.

**The workflow runs on `pull_request_target` and never touches the head of the branch.** That trigger is what supplies a write-capable token on a pull request from a fork, and it is also the one that would hand that token to the fork's author if any step checked out, built, or executed the head. Every step reads event metadata and the API. This is the property the workflow is reviewed against, not an implementation detail of it.

**Concurrent acceptances are resolved by compare-and-swap rather than by serialising the workflow.** Two pull requests can accept in the same minute, and each run reads the register before it writes one. The ref is updated with `force=false`, so a register that moved under a run produces a rejected call rather than a lost signature, and the run reads again — five bounded attempts with jittered backoff. A GitHub Actions concurrency group was rejected for the opposite reason it looks right for: with `cancel-in-progress: false` the platform keeps one run queued and cancels older pending ones, which would silently discard an acceptance.

### Consequences

- Good, because the licensing decision stays available: MailFathom can be published under other terms later without tracing contributors, and nothing about that possibility is asserted or announced today.
- Good, because every contributor states the code was theirs to give, which is the gap that has no closing event and is not about relicensing at all.
- Good, because the evidence is held by this project, in the open, in a form a later reader can audit — including which exact bytes of the agreement were accepted, since an entry records the git object id of `CLA.md` beside its version number.
- Good, because no third-party service learns who contributes here, and no service row joins `THIRD_PARTY_LICENSES.md` for a record this project has a reason to keep itself.
- Neutral, because the agreement covers work already submitted by the accepting contributor, which matters for nobody today and is what makes the timing of this decision cheap.
- Neutral, because `license/cla` is published as an ordinary commit status and is not made a required check here; making it one is a separate decision, taken once the workflow has run against a real pull request.
- Bad, because a first-time contributor is asked for something, and some will not answer. That cost is real and is why the request comment says what the agreement is for rather than presenting it as a formality.
- Bad, because the register branch is maintained by machinery this repository owns, so a failure in it is this project's to diagnose rather than a vendor's.

## Validation

`scripts/test-agent-workflow.sh` asserts the licence header on the workflow file, as it does for every workflow. Beyond that the decision is validated by the pull request it is asked on: an external pull request that carries a green `license/cla` and an entry on `cla-signatures` naming its author is the whole of the evidence that the mechanism works, and the first one is what proves it. The properties that cannot be observed from an outcome — that no step touches the head of a fork's branch, and that the register is written by compare-and-swap — are review obligations on any change to the workflow, stated in the file itself.

## Pros and Cons of the Options

### Keep Apache-2.0 section 5 alone

The current mechanism, described in `CONTRIBUTING.md` before this decision.

- Good, because it asks nothing of a contributor and is the lowest-friction inbound arrangement that exists.
- Good, because it is already true and needs no machinery.
- Bad, because it forecloses every later licensing decision on the first merged external contribution, permanently and without any event marking it.
- Bad, because it says which licence a contribution arrives under and never that the contributor was entitled to submit it.

### Adopt a Developer Certificate of Origin

A sign-off line in the commit message asserting the right to submit.

- Good, because it is the lowest-cost way to obtain the statement of entitlement, and the Linux kernel's use of it is the reason it is credible.
- Good, because it adds no document a contributor has to read.
- Bad, because it grants nothing. A DCO asserts provenance and confers no licence beyond the project's own, so the relicensing gap stays exactly where it was — which is the gap that closes irreversibly.

### Adopt a contributor licence agreement, collected by `contributor-assistant/github-action`

CLA Assistant Lite, the action most projects reach for.

- Good, because it stores signatures in the repository rather than in a service, which is the property this decision wanted.
- Good, because it is a solved problem somebody else solved.
- Bad, because it is archived and read-only. Its last release predates the archive by eighteen months and its own README directs readers to fork it, so adopting it is adopting an unmaintained dependency for a mechanism that has to keep working for as long as the project takes contributions.

### Adopt a contributor licence agreement, collected by `cla-assistant.io`

The hosted CLA Assistant, actively maintained.

- Good, because it is maintained, widely used, and needs no workflow of this project's own.
- Good, because its own source is Apache-2.0, so the terms of the tool are not in question.
- Bad, because the signature record lives in a third-party database. The evidence this project would one day rely on would be in somebody else's custody, subject to their availability and their retention.
- Bad, because it needs OAuth access to this repository and learns the identity of everyone who contributes, which puts personal data outside GitHub for a record that had no reason to leave it.

## More Information

`CLA.md` is the agreement itself and `CONTRIBUTING.md` § *Licensing your contribution* is what a contributor reads first. The `Fathom license` GitHub App holds three permissions and no more — `Pull requests: Read and write`, `Commit statuses: Read and write`, and `Contents: Read and write` — and its private key reaches the workflow as a signing key exchanged for an hour-long installation token, exactly as the reviewer App's does in `Fathom review`.

This decision is revisited if MailFathom's own licence changes, because an agreement written to keep a decision open reads differently once the decision has been taken, and a contributor is entitled to see it say so.
