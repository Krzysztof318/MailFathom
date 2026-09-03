---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-04
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Build Android every night as one debug-signed artifact left on its run rather than a head this project supports, keep its credential in the platform's own protected storage as a third answer under ADR 0023, and hold the no-platform-branch rule with the shell as its only seam

<!-- describes: frontend/src-tauri/**, frontend/src/Client.App/src/shellOperations/**, frontend/src/Client.App/src/signIn/credentialStore.ts, .github/workflows/nightly.yml -->

## Context and Problem Statement

[ADR 0021](0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md) chose the client's stack and, in the same record, wrote the sentence this one has to answer: **Android, iOS, and macOS are reachable from the same tree and supported by nothing.** It gave the reasoning — a supported head needs a signing identity, a distribution channel, a store relationship where one applies, and somewhere for a defect report against it to go — and drew two consequences that read as absolutes: nothing in the source may branch on those platforms, and a build produced for one is somebody's own build rather than a release artifact. It also named its own revision condition, which is a supported mobile head being wanted.

[#1612](https://github.com/Krzysztof318/MailFathom/issues/1612) sits between the two, and that is the whole difficulty. It is not the revision condition: there is still no signing identity, no store, no channel, and no support promise, and none of them is being acquired. What it does is smaller and real — the repository starts producing an Android artifact on a schedule, the client learns the phone, fold, and tablet compositions the design has drawn all along, the credential is kept in Android's own protected storage rather than in a page, and the system back gesture is answered. Each of those is a fact about the tree that 0021's sentence does not cover, and left unrecorded each would be read as the support position quietly eroding.

This record says what an Android head is here, so that a nightly cannot become a release by accumulation.

ADR 0021 is still at `status: proposed` and is therefore editable, so this is a decision to write a second record rather than a constraint. Two reasons. What changes is not the choice 0021 took — the stack, the two shipped heads, and the support position all stand exactly as written — so an edit would be a later decision folded into an earlier record's reasoning, leaving nothing saying it was taken later or against what. And 0021 is argued from the proof of concept in [#1382](https://github.com/Krzysztof318/MailFathom/issues/1382), which built an APK and ran it on no hardware; what is decided here is argued from a stage that puts the head on a device. Those are different evidence bases and belong in different records.

## Decision Drivers

- **The gap between *the tooling could build this* and *this project ships this* is where a support obligation is acquired by accident.** 0021 named that gap; a build running every night is precisely the thing most likely to close it without anybody deciding to, because an artifact that appears on a schedule looks like a product.
- **A nightly is not a release and has nothing to attach to.** The desktop bundles and the `mfctl` binaries already sit on their run on those terms, and [ADR 0004](0004-versioning-and-release-policy.md) already separates the two channels in triggers, registries, tags, and metadata. An Android artifact either joins the existing arrangement or invents a third one, and inventing a third one is how a channel acquires meaning nobody wrote down.
- **Debug signing is the property that makes the artifact unmistakably somebody's own build**, and it is enforced by the platform rather than by a sentence: a debug-signed APK cannot be published to a store and cannot be installed over a release-signed install. That is a stronger guarantee than any wording, and it is worth choosing deliberately rather than inheriting from a default.
- **A toolchain break found the night it lands costs a nightly; found later it costs whatever it is blocking.** The Android toolchain is the largest set of prerequisites in this repository — an SDK, an NDK, a JDK, four Rust targets, and a Gradle project — and it is the one nothing else exercises.
- **Nothing in the tree may branch on the platform**, per `frontend/src/AGENTS.md` § *The two heads*, and a third head is where that rule is actually tested. Two heads can agree by accident; three that differ in a credential store, a notification mechanism, and a hardware gesture cannot.
- **A phone kills a background application constantly**, which is what turns the credential question from a detail into a decision. The rule 0023 wrote for two heads resolves Android to the page's own storage, and on a phone that is a password typed several times a day.
- **What the client renders is mail, and a device is a place mail now rests.** An Android device backs application data off itself by default, which is an egress point neither head 0023 was written for has.

## Considered Options

Three axes, decided together because the answers constrain each other.

1. **What an Android head is here:** nothing, leaving 0021 as it stands; a nightly artifact this repository builds and hands to whoever wants it; a supported, released head.
2. **Where the artifact goes:** left on its run with the sibling nightly artifacts; attached to a release or a prerelease; published to a store or an update channel.
3. **How the credential question reaches a third head:** ADR 0023 covers it as written; ADR 0023 is amended by this record; ADR 0023 is superseded and retaken for three heads.

## Decision Outcome

Chosen option: **a nightly artifact left on its run, with ADR 0021's support position unchanged and ADR 0023 amended rather than replaced** — because it buys the one thing the middle position is actually for, which is that the toolchain is exercised and somebody can try the client on a phone, while leaving every property that would make it a supported head absent by construction rather than by intention.

This record stands beside ADR 0021 and supersedes none of it. It amends ADR 0023 in the two places named below and supersedes none of that either.

### What the Android head is

- **One APK per nightly run**, covering `arm64-v8a` and `x86_64`, with a checksum beside it, uploaded as a run artifact under whatever retention the desktop bundles already use. `arm64-v8a` is every phone and tablet somebody would run it on; `x86_64` is the emulator.
- **Signed with a debug key.** No release signing key, no keystore, and no store credential exists in this repository or in its secrets, and none is created for this.
- **Built from the same source tree as the other two heads**, over the same bundle, through `frontend/src-tauri/`. There is no Android source tree and no Android-only screen.
- **It reports the version the nightly channel resolved**, read the way every other artifact reads it, so the artifact's own version carries the `nightly.<run number>` identifier ADR 0004 defines. Nothing types a second number into a Gradle file.
- **It gates nothing.** The client's own gate has already run against the source by then, so a failure in the Android build fails that job and nothing else on the run — an Android toolchain break is worth finding without becoming a reason nobody gets a nightly image.

### What "unsupported" still means, now that an artifact exists

The word has to survive the artifact, so it is written out for each reader rather than left as an adjective.

**For somebody who installs it.** There is nothing to install *from*: no release page, no store listing, no update channel, and no download address that outlives a run's artifact retention. The build is debug-signed, so it cannot be updated over a release-signed install and cannot be published anywhere; it is a build to try, not a client to live in. Nothing about it is promised to work, to keep working, or to still be there next week, and no data kept on the device by one nightly is promised to be readable by the next. A person running it is running somebody's own build, and the only thing this project undertakes is that the build exists and says what it is.

**For somebody reporting a defect.** An issue about the Android head is welcome and is triaged as work on this tree like any other, against the milestone and the board that govern everything else. What it is not is a support request against a product, and two things follow that would not follow for a release: it carries no response expectation, and a defect that reproduces only on a device — a manufacturer's WebView, a vendor's power management, a launcher's gesture handling — is out of scope rather than a bug to chase, because chasing it is precisely the obligation this record declines to take. A defect in a composition the design draws is a client defect at any width and is in scope on every head.

**For a contributor.** The head is not exempt from anything that governs the tree. The source it builds from is the same source, so the client's lint, type check, unit suite, formatting, and build already cover it, and no rule in `frontend/AGENTS.md` or `frontend/src/AGENTS.md` is relaxed for it. What is exempt is the *build*: it runs on the nightly channel rather than in the pull-request gate, so a change that breaks the Android toolchain alone merges green and fails a nightly. That is the accepted cost of not putting an SDK, an NDK, a JDK, and a Gradle project into the critical path of every pull request. What is also absent is everything ADR 0004 attaches to a release: no changelog entry, no compatibility promise between nightlies, and no deprecation window.

### The no-platform-branch rule holds, and the shell is its only seam

Adding a third head changes nothing about the rule in `frontend/src/AGENTS.md` § *The two heads*. No component, hook, or screen asks which head it is running on, and no module is chosen by target.

- **Every composition comes from the two questions a screen may ask** — the width it has been given, and what the pointer can do. The bottom navigation, the single pane, the drawers, the touch sizing, and the swipe on a message row are all answers to those, which is why a narrow desktop window gets them too and why nothing about them is Android's.
- **The three genuine shell concerns resolve at the composition root**, through `Client.App/src/shellOperations/`, in the shape `linkOpener.ts` established: the application declares the operation it needs, one module decides which implementation satisfies it by whether a shell offered the command, and every component below receives it through context. Those three are **where the credential is kept**, **how a system notification is raised**, and **what the system back gesture does**. `Client.App/src/signIn/credentialStore.ts` is the credential one and still sits beside the sign-in screen, which predates the directory and is a move of its own rather than part of this.
- **The back gesture is the new member and belongs there for the same reason as the other two.** What the shell contributes is an event the application cannot observe for itself; what the application does with it — close the topmost dismissible layer if there is one, else navigate — is a rule about its own layer stack, and it is identical wherever the event comes from. A browser's history back is the same event with a different origin, which is what makes this a shell operation rather than a platform branch wearing a seam's clothes.

The seam is where a head difference is *permitted*, not where one is *invited*. An operation joins `shellOperations/` when the application genuinely cannot perform it — not to give Android something the other heads do not get, which this stage builds none of.

### Android is a third answer under ADR 0023, and this record amends it

[ADR 0023](0023-where-the-client-keeps-the-credential-it-signs-in-with.md) decided this question against a desktop keychain and a browser page. Most of what it decided is about the credential rather than about a head, and all of that holds here unchanged: what is kept is the finished `Basic` header value and one value; the user name is not kept a second time and nothing derived from it is kept; it is bound to the address it was given for and discarded where that address changed; sign-out, an `unauthenticated` refusal, and a changed address are what clear it, and an `unavailable` read does not; nothing outside `Client.Backend` composes or inspects it; and the sign-in screen says which of the two arrangements a person is getting, in a sentence, chosen by a value the store reported rather than by a platform.

Two parts of it were written for two heads and are amended here.

**The mechanism.** 0023 says the keychain is reached *through the `keyring` crate*, which is the desktop answer rather than the general one — the crate has no Android backend. The Android shell answers the same three operations — keep, read back, forget — over **`EncryptedSharedPreferences` on the Android Keystore**, which is the platform's own protected storage and keeps the key material where the operating system will not hand it to another application. That is a second implementation of an operation the application already declares, so it lands inside the seam above and nothing over that seam changes.

**The fallback.** This is where the amendment actually bites, and it is the reason a record was needed rather than a note. 0023's rule is that a shell offering no keychain keeps the credential for the run in `sessionStorage`, and it argues that outcome carefully for a browser tab and for a Linux machine with no Secret Service. Applied to Android unread, that same rule resolves a phone to the page's own storage — and a phone kills background applications constantly, so it is a password typed several times a day. That is not a stated outcome anybody chose; it is what a rule written for two heads does when a third arrives. **On the Android head, protected storage that cannot be reached — or a key the operating system has invalidated, as a screen-lock change does — keeps nothing and asks the person to sign in again.** It never falls back to the page's own storage, and it says so rather than failing obscurely.

One constraint is added that 0023 had no reason to state, because neither an operating-system keychain nor a browser tab is copied off the machine by default: **the entry is excluded from automatic backup and from any other off-device copy the platform would take.** A credential synchronised to a vendor's cloud is a copy of a password in a place nobody chose, and a `Basic` password has no expiry to limit what that costs.

0023 is not edited by this change and is not superseded by it. Its text stays the record of what was decided for two heads; this is where a reader goes for the third.

### iOS and macOS are exactly where ADR 0021 left them

Reachable from the same tree, supported by nothing — and now, additionally, **built by nothing**: no schedule, no job, and no artifact. Neither has a signing identity, and iOS additionally needs a paid programme membership, a store relationship, and a review process before an artifact reaches anybody at all, so the distance between reachable and distributable is larger there than the one this record crosses for Android.

Nothing here is a precedent that a reachable target acquires a nightly. Android gets one because a stage was taken to make the head work and to keep its toolchain honest; a nightly for another target is that same decision taken again, on its own evidence.

### What would make this a supported head

Stated as a checklist so that publishing one is an act somebody takes rather than a line a nightly crosses. Every item is required; none of them exists today.

1. **A release signing identity**, and a decided answer to where the key lives — which is not this repository and not its secrets in their present form.
2. **A distribution channel**, and where it is a store, the store relationship that goes with it: the listing, the policy review, the data-safety declaration, the content rating, and an account somebody owns.
3. **A defect channel the head is named in**, and somebody answering it — which is the item 0021 identified as missing and the one no amount of build automation supplies.
4. **An update story**: an artifact that installs over its predecessor, and a decided relationship between the version ADR 0004 governs and whatever monotonic version code the channel requires, which is a second number and therefore a second decision.
5. **Behaviour verified on hardware rather than on an emulator** — the touch behaviour, the back navigation, and the keyboard that #1382 left unmeasured, on more than one manufacturer's WebView.
6. **A privacy statement covering what the client keeps on a device**, and the declaration form the channel asks for, both consistent with what this repository's own privacy obligations already require.
7. **A changelog entry and a compatibility promise**, which is what makes an artifact a release under ADR 0004 rather than a build.
8. **The Android artifact reviewed as an artifact under [ADR 0016](0016-third-party-licence-obligations-per-artifact.md)**, whose unit is `(component, version, artifact)` — the APK is a third one beside the web bundle and the desktop application, and the Android toolchain's own components reach it.

Until every one of those exists, what this repository produces for Android is a nightly, and this record is what says so.

### Consequences

- Good, because the Android toolchain is exercised on a schedule, so a break in it is a nightly that failed rather than a discovery made under pressure at the moment somebody needs the head.
- Good, because somebody can try the client on a phone without building the tree, which is what makes the compositions in this stage reviewable by anybody but their author.
- Good, because the properties that would make this a supported head are absent by construction rather than by intention: a debug key cannot be published or updated over, and an artifact on a run expires on its own.
- Good, because the credential on the head most likely to be lost or stolen is in the platform's own protected storage rather than in a page, and is kept off every backup the platform would otherwise take.
- Neutral, because the no-branch rule needed no change to absorb a third head, which is evidence for the seam rather than a cost of it — the back gesture joins `shellOperations/` as one more module of a shape that directory already holds.
- Neutral, because ADR 0023 keeps its text and its status, so a reader following the credential question now reads two records rather than one.
- Bad, because an artifact that appears every night reads as a product to somebody who did not read this file, and no amount of wording fully removes that. The debug key is what actually holds the line.
- Bad, because the Android build gates nothing, so a change that breaks it alone merges and is found by a nightly — the accepted price of keeping an SDK, an NDK, a JDK, and Gradle out of every pull request.
- Bad, because the credential now has three implementations of one operation, in three languages, and the failure modes of the third are the platform's rather than this project's.
- Bad, because the licence register gains a third artifact's worth of review under ADR 0016, for a build nobody is promised.

## Validation

- `docs/decisions/` is a protected path in `.github/workflows/protected-paths.yml`, so this record's own creation is gated on the owner authoring the change that carries it.
- The `describes:` marker names the shell, the seam, the credential store, and the nightly workflow, which is what tells a later pull request under any of them that it is read against this decision. `scripts/review-obligations.sh` and `Fathom review` both resolve it.
- The absence of a signing key is enforced by [#1615](https://github.com/Krzysztof318/MailFathom/issues/1615)'s acceptance, which requires the artifact to be debug-signed and requires the workflow to state why — a reviewable assertion in a file rather than a claim in this one.
- The no-platform-branch rule is enforced where it already is: `frontend/src/AGENTS.md` states it, [#1612](https://github.com/Krzysztof318/MailFathom/issues/1612)'s acceptance requires no component, hook, or screen to branch on the head, and review is what checks it. No script decides it, which is why it is stated in both places rather than assumed.
- The credential rules are enforced by [#1619](https://github.com/Krzysztof318/MailFathom/issues/1619)'s acceptance — the entry excluded from backup, removal on sign-out reported rather than assumed, no silent fallback to a weaker store, and nothing above the composition root learning which head it is on — with unit tests beside the sources they cover.
- `$check-docs-licenses` and ADR 0016 hold the Android toolchain's components to `THIRD_PARTY_LICENSES.md`, per artifact.

## Pros and Cons of the Options

### Nothing changes, and ADR 0021 stands as written

- Good, because it is the only option with no new obligation of any kind, and the support position needs no explaining.
- Good, because it costs no build minutes and no toolchain in continuous integration.
- Neutral, because the compositions this stage builds are width-driven and would land regardless, so the client would still improve on a phone-sized window.
- Bad, because the head then rots: nothing builds it, and the first person to want it discovers a year of accumulated toolchain drift at the moment it matters.
- Bad, because it leaves the credential question unanswered on a head the tree still reaches, so somebody building an APK by hand gets 0023's browser path and a password on every kill, with nothing saying that was decided.

### A nightly artifact this repository builds and hands to whoever wants it

- Good, because it buys the toolchain check and the try-it-on-a-phone case, which are the two things actually wanted, at the cost of one job on a channel that already carries three like it.
- Good, because the terms are ones this repository has already written down for the desktop bundles and the command binaries, so the artifact needs no new vocabulary.
- Neutral, because it makes the Android head a thing people talk about, which is a change in expectation even where nothing is promised.
- Bad, because an artifact produced on a schedule is the shape a release has, and the distinction rests on a signing key and a paragraph.

### A supported, released head

- Good, because it is the only option that gives somebody an Android client they can rely on, which is what a mail client on a phone is for.
- Bad, because every item on the checklist above would have to be true first, and most of them are relationships and commitments rather than work: an identity, a store account, a review process, and somebody answering defect reports.
- Bad, because it would put behaviour nobody has measured across manufacturers' WebViews into a release, which is exactly what 0021 refused and what this stage does not change.

### Attaching the APK to a release or a prerelease

- Good, because it gives the artifact a durable address, so somebody can find last month's build rather than only last night's.
- Neutral, because it needs no store and no review.
- Bad, because a release asset *is* the distribution channel, and attaching an unsupported artifact to a supported release is the exact confusion this record exists to prevent — a person who trusts the release page has no way to know one asset is different.
- Bad, because ADR 0004 governs what a release contains, and adding an artifact carrying no compatibility promise to it would change what a MailFathom release means.

### Publishing to a store or an update channel

- Good, because it is the only way an ordinary person installs an Android application, so anything short of it reaches developers alone.
- Bad, because it requires the signing identity and the store relationship that do not exist, and it cannot be done with a debug key at all.
- Bad, because a store listing is a support promise made to the store as well as to a user, with policy obligations that outlive anybody's interest in maintaining the head.

### ADR 0023 covers Android as written

- Good, because it needs no record and no amendment, and the rule is genuinely general in form: a shell offering a keychain gets one, and everything else gets the page.
- Neutral, because the parts of 0023 about what is stored, what binds it, and what clears it hold on Android either way.
- Bad, because the rule resolves Android to the browser path, so the head with the most aggressive process lifetime gets the storage with the shortest, and a person signs in several times a day.
- Bad, because it would make a consequential outcome an accident of a rule's generality rather than a decision, which is the failure mode a record exists to catch.

### ADR 0023 superseded and the question retaken for three heads

- Good, because one record would then answer the whole question, and a reader would follow one file rather than two.
- Neutral, because the answers would be identical: nothing in 0023's reasoning about the desktop or the web head is disturbed by Android.
- Bad, because superseding a record whose reasoning still holds discards the argument along with the text, and the two amendments here are a mechanism and a fallback rather than a different decision.
- Bad, because 0023 is the record [#1419](https://github.com/Krzysztof318/MailFathom/issues/1419) and the existing credential store were written against, and replacing it would leave that work pointing at a superseded file for no gain.

## More Information

- [#1612](https://github.com/Krzysztof318/MailFathom/issues/1612) is the stage this record opens, and [#1613](https://github.com/Krzysztof318/MailFathom/issues/1613) is the issue it answers. [#1614](https://github.com/Krzysztof318/MailFathom/issues/1614) builds the head, [#1615](https://github.com/Krzysztof318/MailFathom/issues/1615) the nightly job, [#1616](https://github.com/Krzysztof318/MailFathom/issues/1616) the shell composition and the back gesture, [#1619](https://github.com/Krzysztof318/MailFathom/issues/1619) the credential store, and [#1620](https://github.com/Krzysztof318/MailFathom/issues/1620) the Android half of the system notification. This record decides; none of them is implemented by it.
- [ADR 0021](0021-client-stack-react-typescript-tailwind-tauri-and-pnpm.md) is the record this one stands beside. Its support position, its two shipped heads, and its no-branch consequence are unchanged; what it named as unmeasured — touch behaviour, back navigation, and the keyboard, none of which #1382 ran on hardware — is what this stage begins to measure.
- [ADR 0023](0023-where-the-client-keeps-the-credential-it-signs-in-with.md) is amended in the two places named above and superseded in none. [ADR 0016](0016-third-party-licence-obligations-per-artifact.md) governs how the Android artifact's closure is reviewed, and [ADR 0004](0004-versioning-and-release-policy.md) governs the version it reports and the channel separation it relies on.
- [#1382](https://github.com/Krzysztof318/MailFathom/issues/1382) is the proof of concept that produced an APK and ran it on nothing, which is why this record's evidence base is the stage rather than that branch.
- Revisit this decision when any item on the supported-head checklist is taken deliberately, since taking one without the rest is how the position erodes; if a second reachable target is proposed for a nightly, which is this decision taken again on its own evidence rather than a precedent this one sets; or if the Android build's failures turn out to be dominated by device-specific behaviour, which would mean the artifact is costing more than the toolchain check it was built for.
