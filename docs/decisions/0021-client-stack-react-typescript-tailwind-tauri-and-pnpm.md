---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-31
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Rebuild the client on React, TypeScript, and Tailwind CSS, ship a web bundle and a Tauri desktop application, and resolve the client's dependencies with pnpm

<!-- describes: frontend/** -->

## Context and Problem Statement

The Uno Platform client was withdrawn by [#1392](https://github.com/Krzysztof318/MailFathom/issues/1392), which left `frontend/src/` and `frontend/tests/` holding a placeholder README each and took ADRs `0018`, `0019`, and `0020` with it, all three having decided something about the application that no longer exists. The client is being rebuilt, and the stack it is rebuilt on is the one thing about that rebuild this repository has never recorded.

[#783](https://github.com/Krzysztof318/MailFathom/issues/783) asked the question in 2026 and named React with Tailwind CSS as the owner's leaning, to be confirmed or replaced rather than adopted by default. [#1382](https://github.com/Krzysztof318/MailFathom/issues/1382) then measured that leaning: a disposable proof of concept on a branch, built against a Claude Design mockup with mock data, deliberately outside every gate this repository runs. The decision has since been taken. What is missing is the record.

This is that record. It is not a re-opening of the choice, and it lands no toolchain, manifest, or source — the skeleton is separate work that builds against what is decided here.

## Decision Drivers

- **The client is a second stack in a .NET repository, and the cost of that is paid on every pull request rather than once.** The lock file, the licence register, the dependency-currency pass, and both verification scripts each grow a branch they do not have today, so a stack is chosen partly for how cheaply those branches can be written.
- **What the client renders is mail, which is personal data.** A browser is a new egress point for it — third-party assets, whatever a session stores client-side, whatever a screenshot shows — so a stack is judged on what it lets a build refuse as much as on what it lets a build include.
- **The client's own supply chain is the largest one this repository would carry.** A front-end dependency graph reaches further than the service's, and `NuGet.config` has already decided that where a dependency may come from is a reviewed decision rather than a machine's default.
- **Agentic development is how this client will actually be written.** That is a property of the stack rather than of the developer: a stack the models have seen a great deal of produces a working screen from a mockup, and one they have not produces a plausible screen that does not compile.
- **One source tree has to reach every head that ships**, because a client maintained as two applications is two clients, and the second one rots.
- **A head that is merely reachable must not read as supported.** The gap between *the tooling could build this* and *this project ships this* is where a support obligation gets acquired by accident.
- **The service's boundaries are not renegotiable by the client.** It reaches MailFathom over `/api/client` alone, which is a transport surface with its own listener and its own credentials, and a client that shows a message is a read path under [ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) exactly as an MCP caller is.

## Considered Options

Four axes, decided together because the answers constrain each other.

1. **The UI stack:** React with TypeScript and Tailwind CSS; a second .NET UI framework such as Avalonia, MAUI, or Blazor; a non-web cross-platform framework such as Flutter.
2. **The desktop shell:** Tauri; Electron; no desktop shell at all, the client being a web page a browser opens.
3. **Which heads ship:** the web bundle alone; the web bundle and a desktop application; every head the toolchain can produce.
4. **The package manager:** pnpm; npm; yarn; bun.

## Decision Outcome

Chosen option: **React with TypeScript and Tailwind CSS, shelled by Tauri, shipping a web bundle and a desktop application, with pnpm resolving the dependency graph** — because it is the combination the proof of concept in #1382 actually produced a working client on, and because every alternative on axis 1 asks this project to pay a novelty cost in exchange for a familiarity the withdrawn client already showed does not pay for itself.

### The application is React, TypeScript, and Tailwind CSS

TypeScript rather than JavaScript, throughout, including configuration and build files: a client whose types stop at the network boundary re-derives the shape of every `/api/client` response by hand at each call site, and that is the boundary where a mistake is a rendering bug over somebody's mail.

Tailwind CSS rather than a component library. The foundation ships without one deliberately — adopting one is its own decision with its own licence review, and the proof of concept reached the mockup without needing to take it.

### The desktop shell is Tauri

Tauri renders in the operating system's own WebView and links the application shell in Rust, which is what makes the desktop head the same source tree as the web head rather than a second application that resembles it.

### Two heads ship; the rest are reachable and supported by nothing

**MailFathom ships two client heads:**

- **A static web bundle**, served from the container image by `ClientApplicationFiles` behind the client surface's own listener. That composition already exists and is unchanged by this decision: the bundle is a directory of files the image carries, the entry document is what a present bundle is recognized by, and an image built without one refuses a deployment that switched the client on, by name.
- **A Tauri desktop application for Windows and Linux**, built from that same source tree.

**Android, iOS, and macOS are reachable from the same tree and supported by nothing.** The proof of concept built an Android APK, which is evidence that the tree is not structurally web-only and is not a commitment to a target. A supported head needs a signing identity, a distribution channel, a store relationship where one applies, and somewhere for a defect report against it to go, and none of those exists. Nothing in the source may branch on those platforms, and a build produced for one is somebody's own build rather than a release artifact.

### What Tauri costs, stated rather than discovered

- **A Rust toolchain joins the build.** Every machine and every runner that produces a desktop head needs it, which is a third toolchain in a repository that pins the .NET SDK in `global.json` and would now also pin Node and Rust. The web bundle needs none of it, which is why the two heads have different prerequisites rather than one.
- **The rendering engine is the operating system's**, which is the trade Tauri makes for its size: WebView2 on Windows and WebKitGTK on Linux. So the client renders in an engine the deployment supplies rather than one MailFathom ships, and a rendering defect can be a property of the host rather than of the release. That is the cost side of not shipping a browser; the benefit is that MailFathom does not distribute one, with everything that would add to the register and to the patch obligation.
- **The dependency graph reaches the licence register in two halves** — the npm closure the application resolves, and the crate closure the shell links — and the crate half is linked into a binary this project would distribute. [ADR 0016](0016-third-party-licence-obligations-per-artifact.md) already decides how that is reviewed: the unit is `(component, version, artifact)`, so the same component reaches a verdict once for the web bundle and once for the desktop application, and a component present but distributed nowhere is recorded as latent rather than as pending. Nothing about this decision waives the standing constraint that a conditioned component stays separately replaceable — a head that cannot satisfy it drops the component rather than the condition.

### pnpm, and what that decides

pnpm rather than npm, and the reason is the same one `NuGet.config` was written for. pnpm links a package into `node_modules` only where a manifest declares it, so a module nothing declared is not importable — the analogue of package source mapping, where a feed supplies nothing until its packages are named. npm's flattened tree makes every transitively resolved package importable from anywhere, which is a supply-chain property acquired by accident rather than chosen.

- **The lock file is `pnpm-lock.yaml`, committed, and generated rather than edited.** It fixes the transitive closure the manifest's ranges resolve to, exactly as the `packages.lock.json` files fix the closure `Directory.Packages.props` resolves to, and the two rules that hold there hold here: a change that moves a pin regenerates the lock file in the same reviewable change, and continuous integration installs in a frozen mode that fails rather than quietly rewriting the closure.
- **The store is content-addressable and shared across the machine**, with `node_modules` a tree of links into it. That is a disk and install-time property rather than a correctness one, and it is named here only so nobody reads a `node_modules` full of symbolic links as a broken install.
- **The registry the client resolves from is declared in the repository's own `.npmrc`**, for the reason `NuGet.config` clears its inherited source list: a registry configured on a developer's machine must not be able to supply a dependency the register never reviewed.
- **`scripts/update-dependencies.sh` reads five pin families today — `nuget`, `tools`, `sdk`, `actions`, and `images` — and none of them is this one.** Until a sixth is written, a client pin is surveyed by hand, and that is a gap in the survey rather than a pin that does not need reading. When it is written it inherits the rules the script already holds: it never edits `THIRD_PARTY_LICENSES.md`, and it regenerates the lock file by running pnpm rather than by rewriting the file.
- **`package.json`'s own `version` field is not the product version.** `<VersionPrefix>` in `Version.props` is the only application version number in this repository, and a client that ships beside the service reports the same one; whatever the client's manifest carries is inert and read by nothing.

### What the proof of concept did not settle

These are open questions rather than accepted risks. Each is answered by work, and none of them is answered by this record.

- **A real mailbox.** The list was measured at 200 messages and cost 1 593 DOM nodes, which needed no virtualization and says nothing about the 214 000-message mailbox the real client has to render. Whether the message list virtualizes is decided against a real timeline, not against this number.
- **`/api/client`.** Nothing in the proof of concept ran against it. Every screen was mock data, so the client's data layer, its error and cancellation behaviour, and its sign-in are unproven end to end.
- **A physical device.** An Android APK was produced; nothing was run on hardware. Touch behaviour, back navigation, and the keyboard are unmeasured, which is one of the reasons no mobile head is supported.
- **Everything a browser makes newly possible.** A content security policy, what a session stores client-side, and which assets the bundle is allowed to fetch are privacy decisions this stack makes reachable rather than answers.

### Consequences

- Good, because one source tree reaches every head, and the proof of concept found the platform-shaped part of it to be two CSS lines and `env(safe-area-inset-bottom)` rather than a branch anywhere in application code.
- Good, because the stack is the one agentic development is most effective on, which is the working mode this client is actually written in.
- Good, because the web head changes no deployment shape: it is files under a web root the image already carries, served by composition that exists.
- Neutral, because two heads have different prerequisites — the web bundle needs Node alone, the desktop application needs Rust as well — so a contributor who only touches the web head never installs the second toolchain.
- Neutral, because the mobile targets stay reachable, so deciding to support one later is a support and distribution decision rather than a rewrite.
- Bad, because the licence register grows the largest entry it carries, in two closures, one of which is linked into a distributed binary.
- Bad, because the desktop head renders in an engine the host supplies, so a rendering defect may be a property of the machine rather than of the release.
- Bad, because the dependency-currency survey is incomplete until it learns to read a sixth pin family, and a client pin is read by hand until then.

## Validation

- `docs/decisions/` is a protected path in `.github/workflows/protected-paths.yml`, so this record's own creation is gated on the owner authoring the change that carries it.
- The `describes:` marker above names `frontend/**`, which is what tells a later pull request under the client tree that it is being read against this decision. `scripts/review-obligations.sh` and `Fathom review` both resolve it.
- The `Frontend` job of `CI` asserts nothing today and gains its build, test, and formatting steps with the skeleton. The frozen-mode install is one of them, so a lock file out of step with the manifest fails the pull request rather than being repaired silently on a runner.
- `$check-docs-licenses` is what holds the register to the two closures above, per artifact, under ADR 0016's rules.

## Pros and Cons of the Options

### React with TypeScript and Tailwind CSS

- Good, because it is the stack the proof of concept reproduced a design on, so the evidence is a working client rather than a preference.
- Good, because a static build output is what the existing serving composition already expects.
- Neutral, because it introduces a second language and a second toolchain to a .NET repository, which was equally true of the withdrawn client.
- Bad, because the dependency graph is larger and moves faster than anything else this repository pins.

### A second .NET UI framework — Avalonia, MAUI, or Blazor

- Good, because it keeps one language, one SDK, and one lock-file mechanism, and the licence register grows by a graph shaped like the one it already holds.
- Neutral, because Blazor WebAssembly would produce a bundle the existing serving composition can carry, unlike the other two.
- Bad, because this is the shape that was just withdrawn. The Uno client was chosen for exactly this argument, and what it cost was a client nobody could iterate on quickly, in a niche the models write badly.
- Bad, because the desktop heads render in a framework-supplied engine, which is a larger artifact and a patch obligation this project would own rather than the host.

### Flutter or another non-web cross-platform framework

- Good, because one tree reaches desktop and mobile with a single rendering model, so a screen looks the same everywhere.
- Bad, because the web head stops being a web page: the output is a canvas-rendered application rather than a document, which is the property that made the withdrawn client hard to test, hard to make accessible, and hard to drive with ordinary browser tooling.
- Bad, because it adds a third language to the repository without removing either of the other two.

### Electron rather than Tauri

- Good, because the rendering engine ships with the application, so a defect is reproducible from the release alone.
- Neutral, because the application source would be identical; only the shell differs.
- Bad, because the artifact carries a whole browser, which this project would then be distributing, patching, and recording in the register.

### The web bundle alone, with no desktop shell

- Good, because it is the cheapest answer on every axis: no Rust toolchain, no crate closure in the register, no signing, and one artifact.
- Neutral, because the web bundle ships either way, so this is a decision about what to add rather than about what to build.
- Bad, because a mail client reached only through a browser tab is the thing an ordinary IMAP client already does better, and a desktop application is most of what distinguishes shipping a client from telling somebody to open one.
- Bad, because it forecloses the mobile heads the tree would otherwise keep reachable, since the shell is what makes them reachable at all.

### Every head the toolchain can produce

- Good, because a user on any platform gets a build.
- Bad, because a published artifact is a support obligation whatever its origin, and there is no signing identity, no distribution channel, and nowhere for a defect report against a mobile head to go.
- Bad, because it would put platform behaviour nobody has measured on hardware into a release, which is precisely what the proof of concept did not cover.

### npm, yarn, or bun rather than pnpm

- Good, because npm needs no decision at all: it is what a Node installation already has.
- Neutral, because all four produce a committed lock file and all four have a frozen install mode, so the continuous-integration half is the same whichever is chosen.
- Bad, because npm's flattened `node_modules` makes an undeclared transitive package importable, which is the property this repository deliberately closed on the .NET side.
- Bad, because bun's runtime and its resolver are moving faster than a supply-chain decision wants, and yarn's advantage over pnpm here is nothing this client needs.

## More Information

- [#1382](https://github.com/Krzysztof318/MailFathom/issues/1382) is the proof of concept this record is written from. It was deliberately outside every gate here — no pull request, no full verification, mock data — so it is evidence about the stack rather than about the client.
- [#783](https://github.com/Krzysztof318/MailFathom/issues/783) asked whether MailFathom ships a human-facing client and on what terms. Everything it asked apart from the stack has since been answered by facts on the ground rather than by argument: a client existed and is being rebuilt, the surface it consumes is `/api/client`, and the delivery is static assets served from the image. This record answers the remaining half.
- [ADR 0004](0004-versioning-and-release-policy.md) governs the version the client reports, and [ADR 0016](0016-third-party-licence-obligations-per-artifact.md) governs how its two dependency closures are reviewed.
- [ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) is unchanged by any of this: a client that shows a message is a read path, and the never-marks-read guarantee holds for a human reader exactly as it holds for an agent.
- Revisit this decision if a supported mobile head is wanted, if the system WebView on a supported desktop target turns out to be the wrong engine to depend on, or if the client's data layer against `/api/client` shows the rendering model to be the wrong one at a real mailbox's scale.
