---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-02
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Publish MailFathom under AGPL-3.0-only, take the `-only` arm rather than `-or-later`, and leave the third-party acceptance policy exactly where it was

<!-- describes: LICENSE, NOTICE, CLA.md, .editorconfig, THIRD_PARTY_LICENSES.md, backend/Directory.Build.props -->

## Context and Problem Statement

MailFathom was published under Apache-2.0 from its first release. The owner has decided it is published under the GNU Affero General Public License instead. Apache-2.0 lets anybody take the whole of this work, run it as a hosted mail service, and return nothing; the AGPL is the one widely-used licence that answers that case rather than the redistribution case, because section 13 reaches a user who never receives a copy at all and only talks to a running instance. MailFathom is exactly that shape — an IMAP-synchronizing service reached over MCP, an HTTP client endpoint, and an admin endpoint.

The decision itself is the owner's and is not what this record argues. What it settles is the three questions the decision leaves open, each of which has a defensible wrong answer that would be expensive to discover later: which arm of the licence, what happens to the attribution file Apache-2.0 section 4(d) was the reason for, and what the change does to the rule that every third-party dependency stays permissive.

That third question is the one worth writing an ADR for. A copyleft project consuming a copyleft dependency reads as harmless — the outbound licence would already carry the obligation — and it is not. `THIRD_PARTY_LICENSES.md` § *License acceptance policy* holds the dependencies to what **both** of MailFathom's contemplated distribution models allow, and the first of those is commercial closed-source distribution. That model does not follow from the outbound licence and is not weakened by it: what MailFathom grants for its own code is the owner's to change, because `CLA.md` and [ADR 0015](0015-contributor-licence-agreement-and-where-assent-is-recorded.md) keep it changeable, and what a dependency grants is not the owner's to change at all. One copyleft dependency ends the closed-source arm permanently, and no later decision undoes it.

## Decision Drivers

- The owner's decision is that MailFathom is AGPL. Everything below is what that costs and what it must not be allowed to cost.
- A future move to different terms — a commercial licence beside this one, or a strictly closed licence — has to stay available. It is available today only because every contribution arrives under `CLA.md` and every dependency is permissive, and only the second of those can be lost by an ordinary pull request.
- The licence has to be stated once and identically everywhere a machine or a person reads it: five files hold the record, 4043 files repeat the header, and four ecosystems index an SPDX identifier of their own.
- An operator has to be able to find out what section 13 asks of them from the project, at the point they decide to deploy, rather than from the licence text they never open.
- AGPL-3.0 has no equivalent of Apache-2.0 section 5, so what places an inbound contribution under the project's terms changes, and the documents that explained the old mechanism now describe something that does not exist.
- Nothing about the dependency graph is forced to move: Apache-2.0, MIT, BSD, ISC, and the PostgreSQL License are all one-way compatible into an AGPLv3 work, so no component is refused by the new grant and no row in the register changes its verdict.

## Considered Options

- `AGPL-3.0-or-later`, the arm the Free Software Foundation recommends, with the "any later version" clause in the file header.
- `AGPL-3.0-only`, granting under version 3 and no other.
- Relax the third-party acceptance policy to admit copyleft dependencies, on the reasoning that MailFathom is now copyleft itself.
- Keep the third-party acceptance policy unchanged and say in the register why the outbound licence does not move it.

## Decision Outcome

Chosen option: **`AGPL-3.0-only`, `NOTICE` kept and re-anchored, and the third-party acceptance policy unchanged and explicitly defended.**

**The identifier is `AGPL-3.0-only`.** "or later" grants recipients rights under a licence version nobody has written, which is precisely the discretion `CLA.md` exists to keep with the owner. The cost is real and small: a recipient cannot elect a future AGPLv4 whose terms might suit them better, and a project that is itself "or later" cannot combine this code without the owner's agreement. Both are answerable by the owner at the time, which is the point. `LICENSE` therefore carries the FSF's verbatim text from `https://www.gnu.org/licenses/agpl-3.0.txt` and the file header names version 3 without the "any later version" sentence, because that sentence is what makes a work "or later" and its absence is what makes it "only".

**`NOTICE` stays and is re-anchored on section 7(b).** It existed because Apache-2.0 section 4(d) asked a derivative distribution to preserve it. AGPLv3 has no `NOTICE` mechanism, but section 7(b) permits a licence to require that reasonable legal notices and author attributions be preserved, which is what the file already is. Dropping it would remove the one artifact that travels beside every published binary, into the container image and the `mfctl` publish output, and that carries the repository URL a recipient works back from — which section 13 makes more useful rather than less. It gains one line naming the licence, so a reader who meets it beside a binary is not left with an attribution and no terms. It stays informational: it adds no use restriction and states no term of its own.

**The third-party acceptance policy does not move.** It is read against the commercial closed-source distribution model, exactly as before, and AGPL stays on its prohibited list as a *dependency* licence whatever MailFathom itself is published under. This is written into `THIRD_PARTY_LICENSES.md` and `.agents/skills/check-docs-licenses/SKILL.md` as a paragraph that names and refuses the wrong reading, rather than left to be inferred from a policy that now looks stricter than the project's own grant. [ADR 0016](0016-third-party-licence-obligations-per-artifact.md) is unaffected in every rule it states: the unit of review is still `(component, version, artifact)`, a latent component is still a decided state, and a conditioned component is still separately replaceable.

**The contributor licence agreement is not versioned up.** Its operative clauses are unchanged — clause 2.1 already granted the owner the right to publish a Contribution "under any licensing terms the Owner chooses, including terms that differ from the licence MailFathom carries when you submit it and including proprietary terms", which is the clause this change exercises rather than amends. What moved is the prose around it saying which licence is in force and that the AGPL has no section 5 of its own. Bumping `**Version 1.0**` would ask every past contributor to accept again and would establish nothing they have not already granted, while the acceptance register records the exact `CLA.md` blob each signature named, so no record is made to say something it did not.

**Section 13 is documented where an operator reads it** rather than only in `LICENSE`: `README.md`, `deploy/docker/README.md`, `deploy/helm/mailfathom/README.md`, and `docs/operations/container-image.md`. One page carries it for all three deployment shapes, because what triggers section 13 is modifying the software and serving it over a network rather than anything Kubernetes, Compose, or Quadlet does differently.

### Consequences

- Good, because a hosted competitor built on a modified MailFathom now owes its users the source of what they are talking to, which is the case Apache-2.0 left open and the reason the decision was taken.
- Good, because the freedom to relicense — including to a strictly closed licence — is intact and is now defended in prose at the two places a dependency is actually accepted, instead of resting on a policy whose reasoning had gone unstated.
- Good, because nothing in the dependency graph moves and no register row changes its verdict, so the change is provably confined to what MailFathom grants for its own code.
- Neutral, because the CLA becomes load-bearing rather than supplementary for inbound licensing. Its terms already covered it, and the `license/cla` check already gated a first contribution, so what changes is the explanation rather than the machinery.
- Neutral, because `NOTICE` is now preserved by permission rather than by requirement. Nothing depended on the distinction: the publish check that fails a build without it is the mechanism that actually kept it present.
- Bad, because a licence break lands on a released product. It is permitted below `1.0.0` under [ADR 0004](0004-versioning-and-release-policy.md), which reserves the right to break the deployment contract in a minor, and the operator's action is to read the new terms rather than to change a configuration — but a distributor who chose MailFathom for being permissive has to make a decision they did not have to make yesterday.
- Bad, because adoption narrows. Some organisations refuse AGPL software outright, and some will not link against it; that cost is the licence's purpose rather than a side effect, and it is the owner's to accept.
- Bad, because the diff is repository-wide by construction — 4043 header lines and every place an SPDX identifier is indexed — so it cannot be split and cannot be reviewed as a small change. A half-relicensed repository states two licences at once, which is worse than either.

## Validation

`scripts/test-agent-workflow.sh` reads the expected header out of `.editorconfig` and fails any of the 4043 files that disagrees with it, in each of the seven forms the header is written in, and asserts the `license:` value in every skill's frontmatter. `VerifyPublishedLicenseAndNotice` in `backend/src/Host/Host.csproj` fails the publish when `LICENSE` or `NOTICE` is missing from the output, which is what keeps the container image and the `mfctl` binaries carrying them. GitHub's own licence detection is the check on `LICENSE` being unmodified: an edit reports `NOASSERTION` rather than `AGPL-3.0`.

Nothing asserts the four SPDX identifiers — `PackageLicenseExpression`, the assembly metadata, the image label, and the chart annotation — or the prose, so `$check-docs-licenses` reads them, and its own § *The project's own license* is the list it reads them against.

## Pros and Cons of the Options

### `AGPL-3.0-or-later`

- Good, because it is the FSF's recommendation and lets recipients move to a future version without asking.
- Good, because it combines with the large body of "or later" copyleft software without a per-case decision.
- Bad, because it grants under terms that do not exist yet, which is the discretion `CLA.md` was written to keep.

### `AGPL-3.0-only`

- Good, because every right granted is a right somebody has read.
- Good, because a combination that needs a later version is a decision the owner takes at the time, with the facts.
- Bad, because a recipient cannot elect a later version themselves, and an "or later" project cannot take this code without asking.

### Relax the third-party acceptance policy

- Good, because it would remove a rule that now looks inconsistent with the project's own licence, and would widen the set of usable components.
- Bad, because it confuses two independent facts: the outbound licence is the owner's to change and a dependency's is not.
- Bad, because it is irreversible in the one direction that matters. A copyleft dependency ends the closed-source distribution model permanently, and discovering that after the fact means removing the dependency from history rather than from the tree.

### Keep the third-party acceptance policy and defend it in prose

- Good, because it keeps the closed-source arm available, which is the whole reason the CLA was taken in the first place.
- Good, because it names the wrong reading and refuses it, at the two places where a dependency is actually accepted.
- Neutral, because it costs a paragraph in the register and a paragraph in the gate skill, and nothing enforces either mechanically.

## More Information

[ADR 0015](0015-contributor-licence-agreement-and-where-assent-is-recorded.md) is what makes this decision possible; this is the first time the freedom it takes has been exercised, and it neither supersedes that record nor is superseded by it. [ADR 0016](0016-third-party-licence-obligations-per-artifact.md) governs how a third-party licence is reviewed and is unchanged. [ADR 0004](0004-versioning-and-release-policy.md) is what permits a break of the deployment contract below `1.0.0` and what obliges the change to be named in the release changelog against that surface.

Revisit this if a commercial licence is ever sold beside the AGPL one, which is the shape `CLA.md` clause 2.1 keeps available and which would make the dual arrangement rather than the single grant the thing to record.
