---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-22
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Review a third-party licence against the artifact that would carry the component, treat a resolved-but-undistributed component as decided rather than pending, and let no build defeat a condition the licence attaches

<!-- describes: THIRD_PARTY_LICENSES.md, .agents/skills/check-docs-licenses/SKILL.md -->

## Context and Problem Statement

`THIRD_PARTY_LICENSES.md` records, for every component this repository pins, bundles, runs, or calls, the terms verified for it and a compatibility decision against MailFathom's two distribution models. It answers *may this be used*, in three bands: permissive and allowed, conditioned and reviewed before use, strong copyleft and refused without the owner's approval. It has never answered the two questions that decide what using a conditioned component actually costs — *in which artifact*, and *what must the build not do to it*.

Both gaps stopped being theoretical when the client stack arrived. `Uno.WinUI.Runtime.Skia.X11`, itself Apache-2.0 and the only supported way to run the Skia desktop head on Linux, declares `LibVLCSharp` as an unconditional dependency in its own package metadata, and LibVLCSharp is LGPL-2.1-or-later. The register could state that the licence had been read and could not state what the reading concluded, because the conclusion depends on an artifact nobody has built and on packaging choices nobody has made. So it recorded *reviewed, not yet decided*, which reads as an unfinished review of a component when it is in fact a finished review of a component that obliges nothing yet — and which leaves the decision to be taken by whoever first runs a packaging command, at the moment they are least able to take it.

That shape belongs to no particular licence and to no particular vendor. Every conditioned term already in the register behaves the same way: the Unicode License's notice condition, BSD-3-Clause's verbatim reproduction of three clauses and a disclaimer, a dual licence whose arms have to be chosen between, a file-scope copyleft such as the MPL, a service whose field-of-use restriction follows the artifact rather than the source. Each of them obliges nothing until something is distributed, obliges differently depending on which artifact does the distributing, and can have its condition quietly defeated by an ordinary build decision — trimming, assembly merging, a single-file bundle, ahead-of-time compilation — taken by somebody who was optimizing a startup time and had no reason to be reading a licence.

The question this record settles is therefore general: what is the unit a licence review decides about, what does a decision have to say when the component is present but distributed nowhere, and which build outcomes are refused in advance so that a condition cannot be lost by accident.

## Decision Drivers

- MailFathom must stay distributable both as a commercial closed-source product and as this open-source project, and a condition discovered after an artifact is published cannot be un-published.
- The same licence obliges differently per artifact. A container image, a Helm chart, a self-contained binary, a browser bundle, and a desktop bundle are five distributions, not one repository.
- A condition survives only if the build cannot silently break it, and the builds most likely to break it are the ones taken for unrelated reasons.
- A component reached transitively cannot be swapped by choosing a different package, so the levers available to answer a finding differ from the ones a direct dependency offers.
- A review that ends in *not yet decided* postpones the decision to the worst possible moment: a release already built, on a question nobody has the evidence for any more.
- The register is worth what its rows assert. A row that means *we looked* rather than *we concluded* devalues every other row on the page.

## Considered Options

- Keep the acceptance policy as it is and decide each conditioned component when an artifact that carries it is first packaged.
- Refuse every conditioned licence outright, and accept only the permissive band.
- Review each component against each artifact that would carry it, record a resolved-but-undistributed component as a decided latent state naming the event that makes it live, and hold the build to a standing constraint that keeps a conditioned component separately replaceable.

## Decision Outcome

Chosen option: "review each component against each artifact, record latency as a decided state, and constrain the build", because it is the only option that lets a conditioned component be used at all without leaving the actual decision to whoever runs a packaging command months later. The first two options are the two failure modes: one defers every decision to the moment it is most expensive, the other pays for certainty by giving up capabilities — a media control, a text-shaping engine, a codec — that no permissive alternative supplies.

Seven rules follow, and they apply to every third-party licence in the register rather than to the case that prompted them.

**The unit of review is the triple `(component, version, artifact)`.** "The repository" is not an artifact and carries no distribution obligation of its own: a licence condition on redistribution attaches to the thing handed to somebody else. A component that reaches more than one artifact is reviewed once per artifact it reaches, because the artifacts differ in what they contain, how they are built, and who receives them — the container image, the Helm chart, the `mfctl` binaries, and the schema artifact are already four different answers to the same question, and each client head adds a fifth shape. The register's exposure sections are the existing expression of this rule; this makes the artifact rather than the section the thing a verdict is written against.

**A component that resolves but reaches no artifact is latent, and latent is a decided state.** Its row says so in those terms, names the event that would make it live, and states what would then be owed. It never says *not yet decided*, because that phrase describes the reviewer rather than the component. The distinction is worth the words: a latent component obliges nothing today and its row is complete today, while a pending review is a defect in the register.

**A conditioned component stays separately replaceable, and no build step may take that away.** This is the standing constraint, and it is stated as build outcomes rather than as a licence summary because it is the build that breaks it:

- The component is not modified, so it is used rather than derived from. A patched copy is a different review.
- Its compiled form stays a separate, named unit in the artifact — its own assembly, its own shared library, its own file — replaceable by a recipient with an interface-compatible build of their own.
- Nothing merges it into MailFathom's own compiled output, trims or rewrites it, links it statically, or compiles it ahead of time into a single image with MailFathom's code.
- A bundle a recipient cannot open to replace the file counts as taking replaceability away, whatever the bundle is called.

Where an artifact shape genuinely requires one of those — a trimmed browser bundle, a Native AOT single binary — the component is dropped from *that* artifact rather than the condition being waived for it. Losing a capability in one head is a product decision the owner can take; publishing an artifact that breaks a licence condition is not a decision at all.

**Notices are discharged by a bundle generated from the artifact's own resolved graph, at build time.** `THIRD_PARTY_LICENSES.md` names that bundle in its four-document table and names the issue that owns producing it. No notice obligation is satisfied by a file somebody remembered to update, because the register has already shown how quickly a transitive version moves underneath one.

**Transitive arrival changes the levers, not the obligation.** A component nothing here references obliges exactly what a referenced one obliges, and the difference is only in what can be done about it: exclude its assets from the artifact, replace the parent that pulls it, drop the head that needs the parent, or — where upstream sells one — take the other arm of a dual licence. Naming the levers in the row is part of the decision, so that a later reader is choosing among known options rather than rediscovering them.

**Strong copyleft, network copyleft, and source-available terms stay refused in every artifact.** The one permitted shape is unchanged and is the shape the register already records for the Claude Code and CodeQL command-line tools: a program that is executed on a build agent or a developer machine and that no artifact carries. Execution is not distribution, and that is the whole of why those rows are allowed.

**Moving a component between bands is the owner's act.** The record of it is a register row; when the reasoning generalizes beyond the one component, it is a record here as well. Neither replaces the other: the row is the evidence about a version, and the record is the rule.

### Consequences

- Good, because a conditioned component can be adopted with the cost of adopting it written down at adoption time, rather than discovered by whoever first builds a release.
- Good, because the build constraint is checkable by reading a project file, which is where the decisions that would break it are actually taken.
- Good, because a latent component gets a complete row, so the register stops carrying entries that mean *we looked* among entries that mean *we concluded*.
- Good, because naming the levers per row means a later reversal is a choice among options already understood, not a fresh investigation under release pressure.
- Neutral, because it changes no verdict already recorded: every permissive row stays exactly as it is, and the three bands are unchanged.
- Neutral, because per-artifact review costs more only where a component is conditioned, which today is a small minority of the register.
- Bad, because the constraint forecloses build options — assembly merging, aggressive trimming, Native AOT — for any artifact carrying a conditioned component, and forecloses them before anybody has asked for them.
- Bad, because a component can end up present in one head and absent in another, which is a difference a reader of the project file has to be told about rather than one they can infer.

## Validation

`$check-docs-licenses` runs as the mandatory completion gate on every change and is where a new or moved dependency is caught; its verdict is recorded even when it is `n/a`. The register's own operational rules require a row whenever a dependency, service, protocol SDK, container image, generated asset, or externally sourced sample is introduced, upgraded, replaced, or removed, and `scripts/review-obligations.sh` reports the register as an obligation when a trigger moves — the same index `Fathom review` reads on a pull request, so a missing row surfaces in review rather than at release.

The build constraint is validated by reading the project files of the artifact being packaged, because that is where it would be broken: a merging step, a trimming property, a single-file property, or an ahead-of-time property on a head whose graph carries a conditioned component. It is deliberately not automated today — no artifact in the repository currently carries a conditioned component, so a check would assert nothing — and the point at which it becomes worth a script is the point at which the first one does.

The notice half is validated by the bundle `THIRD_PARTY_LICENSES.md` names in its four-document table, which that table still records as not produced. Its own acceptance requires a build to fail when the bundle is missing from an artifact, so the check arrives with the bundle rather than being a second thing to remember.

## Pros and Cons of the Options

### Keep the acceptance policy as it is and decide when packaging

The three bands stay prose in the register, and a conditioned component keeps a row saying it was reviewed. Whoever first packages an artifact carrying it resolves what it obliges.

- Good, because it costs nothing to adopt and nothing changes.
- Neutral, because it is what happened up to now, and up to now every conditioned component reached no artifact, so nothing was ever actually deferred in practice.
- Bad, because the decision lands on somebody running a release command, which is the moment with the least evidence, the most time pressure, and the strongest incentive to conclude that it is fine.
- Bad, because nothing stops an unrelated build change — a trimming property added for startup time — from defeating a licence condition between the review and the packaging.
- Bad, because a row meaning *not yet decided* is indistinguishable from a row somebody forgot to finish.

### Refuse every conditioned licence outright

Only the permissive band is accepted. Anything with a condition beyond notice preservation is refused at the point it appears in a graph.

- Good, because it is the simplest rule to apply and the cheapest to verify.
- Good, because it removes any possibility of a build step defeating a condition, since no condition is ever accepted.
- Neutral, because most of the register would be unaffected: the permissive band is where nearly every row already sits.
- Bad, because it decides product capability by licence band. A framework's supported platform head, a text-shaping engine, or a codec has no permissive equivalent, and refusing the licence is refusing the capability.
- Bad, because a conditioned component reached transitively would force dropping the whole parent, which is a large change made to avoid an obligation that is frequently cheap.
- Bad, because it would refuse terms this project has already accepted after review and is right to have accepted, including the Unicode License the ICU data arrives under.

### Review per artifact, record latency as decided, constrain the build

The chosen option.

- Good, because the decision is taken while the evidence is in front of the person taking it, and written where the next reader finds it.
- Good, because the constraint is stated as build outcomes, so it is refutable by reading a project file rather than by interpreting a licence.
- Good, because it keeps the option of dropping a component from one artifact while keeping it in another, which is the proportionate answer when only one head's build shape conflicts.
- Neutral, because it adds an obligation to name the triggering event and the available levers in a row, which is a sentence rather than a section.
- Bad, because it asks a reviewer to know which artifacts a component reaches, which is a restore graph per head rather than a single list.
- Bad, because the constraint is enforced by review rather than by a check, and will stay that way until an artifact carries a conditioned component.

## More Information

The first application of this record is LibVLCSharp 3.7.0, whose row in `THIRD_PARTY_LICENSES.md` carries the verdict, the LGPL section 6 obligations that fall on a packaged desktop head, and the levers that were available and not taken. Issue [#1091](https://github.com/Krzysztof318/MailFathom/issues/1091) holds the evidence the verdict was read against.

Revisit this record if an artifact is ever built that cannot satisfy the separate-replaceability constraint for a component it genuinely needs — a Native AOT head, or a browser bundle whose linking model leaves no separate unit. That is the case the constraint was written to refuse, and refusing it is the intended outcome; a decision to allow it is a different decision and belongs in a record that supersedes this one.

Related: [ADR 0004](0004-versioning-and-release-policy.md) defines the release the artifacts are cut from, and [ADR 0015](0015-contributor-licence-agreement-and-where-assent-is-recorded.md) covers the inbound direction — what this project may do with contributions — which is the mirror of the outbound question decided here.
