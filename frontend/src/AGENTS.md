# Client Source Development Instructions

These instructions apply under `frontend/src/` in addition to the repository root instructions. Everything the root
file states holds here — the English rule, the branch rule, the licensing header, the documentation obligations, and
the privacy classification of anything that touches mail — and nothing below repeats one of them.

What does *not* reach here is `backend/src/AGENTS.md`. Those are the conventions of a service: API and failure design,
dependency injection over a generic host, outbound HTTP client lifetimes, asynchronous return types at a use-case
boundary. A view is none of those things, and the directory cascade never loads that file for a change under
`frontend/`, so this one carries the client's half in full rather than by reference.

The client's own tests are governed by `frontend/tests/AGENTS.md`, which points back here for everything below:
a test in this stack is C# compiled by the same build with the same analyzers, so the conventions on this page hold
there exactly as they hold here.

## What this application is

MailFathom's client is a mail application with the model in the middle of it. Not a chat window over a mailbox and not
a mail reader with an assistant panel bolted to one side: the model leads the person through their correspondence, and
the mailbox is what it leads them through. Every screen is judged against that — a feature that would be equally at
home in an ordinary mail client is usually the wrong shape for this one.

It is written on Uno Platform and targets Windows, Linux, macOS, iOS, Android, and WebAssembly from one codebase. Two
heads exist today: `net10.0-desktop`, the Skia desktop head that runs on Windows, Linux, and macOS from that one
target framework, and `net10.0-browserwasm`, the browser head. A third target framework, plain `net10.0`, builds no
head at all — it is the reference target the unit suite references the application through. The mobile heads are
absent rather than impossible: adding `net10.0-android` and `net10.0-ios` to `TargetFrameworks` is what opens them,
and no code here may assume they never will be.

**It has to feel native on each of them.** That is a stronger obligation than "it runs everywhere", and it is the one
most likely to be lost by accident:

- The Android system back gesture and back key move back through the application's own history. Uno's navigation
  handles that when a screen is reached through a route; a screen reached by swapping content by hand is a screen the
  back key leaves the application from.
- A press gives feedback where the platform gives feedback, and a control the platform draws is left drawing itself.
  Reach for a `ControlTemplate` when the interaction genuinely differs, not to make one platform look like another.
- A layout holds its shape from a phone through a resized browser window to a wide desktop one. State that with
  `VisualStateManager` breakpoints or the Toolkit's responsive markup, never by branching on the running platform.
- Content stays clear of a notch, a rounded corner, a system bar, and a soft keyboard. `utu:SafeArea.Insets` is what
  says so; a hard-coded margin is what breaks on the next device.
- Nothing blocks the thread that draws. A `.Result`, a `.Wait()`, or a `Thread.Sleep` in a handler is a frozen window
  on desktop and a frozen tab in the browser, and `.config/BannedSymbols.txt` refuses the last of those outright.

## MVUX is the state model

State is MVUX — Uno's own `Uno.Extensions.Reactive` — rather than a pattern invented on top of it. A model is a
`partial record`, it exposes feeds and states, and the generator writes the bindable type a XAML binding actually
reaches. Do not hand-write a view model, do not implement `INotifyPropertyChanged`, and do not add another MVVM
framework beside it.

- **`IFeed<T>` for what is read and `IState<T>` for what is written.** A feed carries three axes — data, error, and
  progress — so a screen can say it is loading and say what failed without either being modelled by hand. Use
  `IListFeed<T>` and `IListState<T>` for collections rather than a feed of a list.
- **What belongs in a model**: the state a screen is composed of, the transformations between it and what a service
  returns, and the methods a command is generated from. What does not: an HTTP call written inline, a
  `DispatcherQueue`, a `Window`, any `Microsoft.UI.Xaml` type at all, and anything that reads a control. A model that
  cannot be constructed in a unit test without a visual tree has taken on a view's work.
- **Records are immutable, and MVUX depends on that.** A new value is produced with a `with` expression; a property is
  never mutated in place. Give an entity that appears in a list a key so item identity survives a refresh — MVUX
  compares by key equality, and without one a list rebuilds every row on every message.
- **Display a feed through `FeedView`** rather than by awaiting it in code-behind. It renders the three axes, which is
  what makes a loading state and an error state the default rather than something each screen remembers to add.
- **Awaiting a feed inside a model is normal** (`var value = await SomeState;`); awaiting one inside a view is a sign
  the work belongs in the model.

## XAML first

The UI is XAML. That is where a reader looks for a screen, it is what the Uno tooling and hot reload understand, and it
is what keeps layout out of C#.

- **Code-behind** holds `InitializeComponent`, and the handlers that genuinely belong to a control's own behaviour —
  focus, a keyboard shortcut, an animation started from a visual state. It holds no application logic, no service call,
  and no state a model could hold instead.
- **C# markup** is correct where the markup is *computed* rather than authored: a template built from a shape that only
  exists at run time. Prefer a `DataTemplateSelector` or a bound property first; a screen written in C# because the
  author preferred C# is the case this rule exists to refuse.
- **A `UserControl` is the unit of reuse.** Extract one as soon as a fragment appears twice, and give it its own
  bindable properties rather than reaching into its parent's `DataContext`.

## Styles, brushes, and templates

Uno Material is the design system, so a colour, a font, and a control style already exist and are already
theme-aware — a screen that states its own is a screen that stops following the system theme.

- **`Styles/ColorPaletteOverride.xaml` is the palette**, and it is the only file where a colour value is written. Every
  brush the application uses resolves from it through Material's own keys.
- **Never write a colour literal in a page or a control.** `Background="#1C1B1F"` is a defect: use
  `{ThemeResource BackgroundBrush}` or the semantic brush that names the role. The same holds for a font family and a
  font size — reach for the typography styles rather than a number.
- **A style that overrides a control's own goes in a resource dictionary named after the control**, under `Styles/`, and
  is merged in `App.xaml`. A page-local `<Page.Resources>` is for what only that page has.
- **Prefer the existing style to a new one, and a lightweight-styling override to a full `ControlTemplate`.** A copied
  template stops receiving the design system's fixes.

## Awaiting, and the context an await captures

The server's rule is the opposite of this one, and it is opposite because the framework is. ASP.NET Core installs no
synchronization context at all, so `ConfigureAwait(false)` there changes nothing and only adds a call to every line —
which is why `backend/src/AGENTS.md` refuses it as a blanket habit. A UI framework does install one: the thread that
draws carries a context bound to its dispatcher, an `await` on that thread captures it, and the continuation is put
back on the thread the visual tree may be touched from. So the question here is never *style*. It is whether this
particular continuation has to come back.

- **Write `ConfigureAwait(false)` wherever the continuation does not touch the UI**, which is nearly everything a model
  does. Uno runs a model off the UI thread by design — commands and state updates from a generated bindable type
  always run on a background thread, and the generated layer marshals to the dispatcher when a binding needs it — so a
  model's awaits have no context worth capturing, and saying so is what keeps the continuation off a dispatcher queue
  it never needed. The same holds for an HTTP call, a serialization step, and anything reached through a service.
- **Never write it where the continuation touches the visual tree.** A code-behind handler, a focus or keyboard
  handler, an animation started from a visual state, and anything that reads or writes a `DependencyObject` all run on
  the UI thread, and the context they capture is the whole of what puts the continuation back on it. Discarding it
  leaves the next line touching a control from the wrong thread, which is a run-time failure rather than a slow path.
- **Do not answer a discarded context with a dispatcher call.** Marshalling back by hand to undo a
  `ConfigureAwait(false)` costs a queue hop and leaves two statements saying what one omission said; the continuation
  that needs the UI thread is the one that keeps its context.
- **This is judged by reading rather than by running.** The browser head is single-threaded, so a discarded context
  costs nothing there and every continuation lands on the one thread there is — a defect introduced this way is
  invisible in the browser and fails on desktop. Nothing checks it either: `frontend/Directory.Build.props` states why
  `Microsoft.VisualStudio.Threading.Analyzers` is absent from this stack, and the rule it would apply — `VSTHRD111`,
  which asks for `ConfigureAwait` on every await — is the blanket habit this section refuses in both directions.

## Project and build layout

```
frontend/
  MailFathom.Client.slnx     the client solution; the backend solution never references a project here
  Directory.Build.props      the client's build contract, importing the root Version.props
  Directory.Packages.props   the client's own pins; Uno's own packages come from the SDK
  src/Client/                the application — one Uno single project, every head inside it
  src/Client.Backend/        everything that reaches the service — plain net10.0, and no Uno package
  tests/Client.UnitTests/    the unit suite over both, governed by frontend/tests/AGENTS.md
```

- **Two projects, and what separates them is the compiler rather than a folder.** `src/Client/` is the application:
  the heads, the XAML, the models, the styles. `src/Client.Backend/` is everything that talks to the service, and it
  targets plain `net10.0` with no Uno package and no WinUI assembly in its graph — so `using Microsoft.UI.Xaml` written
  there does not compile, and neither does a `DependencyObject` in a service call. That is the rule this page states
  about a model taking on a view's work, made into a reference graph rather than something a reviewer has to remember.
  The reference goes one way and only one way: `Client` references `Client.Backend`, never the reverse.
- **The browser head is the one place the application project sets `AllowUnsafeBlocks`**, and it is set for a generator
  rather than for any code written here. `[JSImport]` marshals through pointers, so the interop source generator emits
  unsafe code and refuses to run without the property — `SYSLIB1074` says so by name. Nothing under
  `Platforms/WebAssembly/` contains an `unsafe` block of its own, which is why the property is conditioned on that one
  target framework instead of declared for the project.
- **The two stacks share no build file.** `frontend/Directory.Build.props` and `frontend/Directory.Packages.props` are
  this stack's own, and a property that has to hold in both is written in both rather than lifted to the root — with
  one exception, `Version.props`, which both import so the client and the service report one number.
- **The Uno version is a single pin**: `msbuild-sdks` in the repository-root `global.json`. Every Uno package version
  follows from it, which is why almost nothing Uno publishes appears in `Directory.Packages.props`. A capability is
  asked for through `UnoFeatures` in the project file rather than by adding a package reference.
- **Every project here carries a lock file except the application one**, and restore runs in locked mode
  wherever it is gated. Regenerate deliberately with `dotnet restore frontend/MailFathom.Client.slnx --force-evaluate`
  in the change that moves a pin, and read the transitive diff. The exclusion is not a gap to close: the .NET SDK puts
  `Microsoft.NET.ILLink.Tasks`, `Microsoft.NET.Sdk.WebAssembly.Pack`, and — in a Debug build —
  `Microsoft.DotNet.HotReload.WebAssembly.Browser` into a WebAssembly target's restore graph at versions that follow
  the installed SDK, which `global.json` deliberately lets roll forward, so a lock file naming them records the machine
  that wrote it and fails the next locked restore. `frontend/Directory.Build.props` carries the whole reasoning, and
  `backend/Directory.Build.props` carries the same decision for the two projects holding the Aspire graph.
- **What a published head is optimized with is decided per target framework**, in `Client.csproj`, and never for the
  project as a whole. The browser head publishes trimmed and enables Uno's XAML resource trimming with it, so a control
  the XAML never names loses its style and fails when the screen loads — the answer is an IL Linker descriptor keeping
  it, never turning the pass off. The desktop head is neither trimmed nor compiled ahead of time, which is the licence
  condition below rather than a preference; and Native AOT for the browser head does not exist at all, because that
  head is Mono. [Publishing the client](../../docs/operations/client-publishing.md) is the page, and none of
  `PublishTrimmed`, `PublishAot`, `PublishReadyToRun`, or `PublishSingleFile` is a property to add without reading it.
- **Nothing under `frontend/` reaches into `backend/`**, and `backend/MailFathom.slnx` names no project here. What the
  two stacks share is the required check and the two gates that decide from the changed paths which of them to run;
  `scripts/verify-fast.sh` and `scripts/verify-full.sh` restore, build, and test this solution when the change reaches
  it, and neither of them so much as loads it when the change stays in `backend/`. Loading it needs the `wasm-tools`
  workload the client build needs, so the flow fails on a machine set up for the service alone rather than skipping
  quietly, and `docs/operations/local-development.md` names the workload beside the client's own commands.
- **That rule is about the build, and the Aspire app host is not one.** `backend/src/AppHost/Program.cs` starts the
  browser head as a resource named `mailfathom-client`, from a path and a command line: no backend project references
  one here, no project here enters `backend/MailFathom.slnx`, and MSBuild is never told the two are related — a clean
  build of the backend solution mentions nothing under `frontend/`. What the app host holds is a directory it starts a
  process in, which is a run-time arrangement rather than a compile-time one, and the rule above is unchanged by it.
- **Formatting comes in the two halves it comes in everywhere here.** `scripts/verify-fast.sh` runs the repairing
  `dotnet format` pass over the client C# files a branch changed; `scripts/verify-full.sh` and the `Verify formatting`
  step of `CI`'s `Frontend` job run the verifying pass over the whole solution. Never invoke `dotnet format` by hand,
  for the reason the repository never does: both halves already run where they belong.

## `MediaPlayerElement` is available, and turning it on is a licence decision rather than a flag

**The desktop head carries no copyleft component.** `Uno.WinUI.Runtime.Skia.X11` declares `LibVLCSharp` —
LGPL-2.1-or-later — as an unconditional dependency in its own package metadata, and it used to reach every
`net10.0-desktop` publish as `LibVLCSharp.dll` beside the application. Nothing in that package uses it:
`Uno.UI.Runtime.Skia.X11.dll` names the assembly in none of its references, because the control it belongs to lives in
a package of its own. So `Client.csproj` names `LibVLCSharp` directly with `ExcludeAssets="all"` on the desktop target
framework, which drops it from the publish and from `MailFathom.Client.deps.json` without removing anything the
application can reach. `THIRD_PARTY_LICENSES.md` carries the verdict and what was measured to reach it, and
[ADR 0016](../../docs/decisions/0016-third-party-licence-obligations-per-artifact.md) the rule that decides what an
artifact owes for what it ships.

**`MediaPlayerElement` may still be used**, and it now costs a review rather than a line. It needs
`MediaPlayerElement;` added to `UnoFeatures`, which is not there today. Reach for it when a screen genuinely plays
media — a video or audio attachment is the obvious case — rather than as a general-purpose surface. What arrives with
it is not one package:

- **`Uno.WinUI.MediaPlayer.Skia.X11`, which needs `LibVLCSharp`** and declares it for itself. The exclusion above
  therefore has to come out in the same change: leaving it in place publishes the control's own assembly without the
  library it calls, and the failure arrives at run time as
  `FileNotFoundException: Could not load file or assembly 'LibVLCSharp'` when playback is first reached, rather than as
  anything the build said. The LGPL section 6 obligations come back with it — the notice, the licence text, the source
  offer in the release notes, and the packaging properties that keep the assembly separately replaceable.
- **`VideoLAN.LibVLC.Windows`, which is the native VLC plugin set** and which the feature adds unconditionally, Linux
  publishes included. It is a **separate licence review** and is not covered by the one that admitted the managed
  binding: VideoLAN publishes its software under GNU GPL v2 *or* LGPL rather than under one licence, so what a native
  bundle carries has to be read before it is added, not after. It is also most of the size — a `linux-x64` publish goes
  from 154 MB to 436 MB with the feature on.

**The four packaging properties stay unset, and no longer for a licence reason.** `PublishTrimmed`, `PublishAot`,
`PublishReadyToRun`, and `PublishSingleFile` are set nowhere in this stack. What used to forbid them was
`LibVLCSharp.dll`'s replaceability; with the assembly gone, whether they are worth enabling is an open measurement
that [#1226](https://github.com/Krzysztof318/MailFathom/issues/1226) owns and
[publishing the client](../../docs/operations/client-publishing.md) records — this file states neither, because the
posture lives on that page. What this file owns is the one thing that makes them licence conditions again, which is
turning `MediaPlayerElement` on.

## Uno Platform Studio's tooling is in this graph, and no artifact may carry it

The section above is about a copyleft licence a packaged artifact would have to satisfy. One more component here
carries a condition, and it runs the other way: it may not be packaged at all. Uno Platform Studio's tooling —
`Uno.Sdk.Extras`, `Uno.UI.HotDesign`, `Uno.UI.App.Mcp`, and `Uno.Settings.DevServer` — is not open source. One carries
an end-user licence agreement forbidding distribution to a third party, and the other three declare no licence at all.
The SDK adds them whatever this project file says, `THIRD_PARTY_LICENSES.md` carries the verdict — they stay — and that
verdict rests on a build fact rather than on an intention: **no artifact may carry any of the four.**

What keeps them out is in the build rather than in a property anyone has to remember. Two of the four have no `lib` to
reference — one is MSBuild targets, the other a tools directory — and the SDK drops the designer's and the App MCP's
assets when `Optimize` is `true`, which is what a Release build sets. So the rule is the one that follows from that: a
head is packaged for distribution from a Release build. A published Debug build of any head, or a head that turns
`Optimize` off in Release, ships somebody else's proprietary assemblies.

**The SDK decides that while restoring, not while publishing**, which is the part a build that produces an artifact has
to say out loud. `Uno.Implicit.Packages.ProjectSystem.targets` reads `Optimize` when it composes the package graph, so
a restore that was not told the configuration is a Debug restore whose graph a later Release publish then packages —
which is exactly how the container image first shipped `Uno.UI.HotDesign.*` and `OpenAI.dll`. Every build that produces
an artifact therefore states `-p:Configuration=Release` on the **restore** as well as on the publish, and states
`-p:UnoDisableHotDesign=true -p:UnoDisableMCPSupport=true` beside it so the result does not rest on the SDK continuing
to read that one property. `deploy/docker/Dockerfile`'s client stage and
`.github/workflows/build-desktop-client.yml` are the two places that do, and both are the whole reason those two
properties are absent from `Client.csproj`: the tooling they remove is the tooling this stack is worked with, so they
belong to a publish rather than to the project file.

## Running the client

`dotnet run --project frontend/src/Client/Client.csproj --framework <head>` is how a head starts, and the framework has
to be named: this project declares three, and `dotnet run` refuses a multi-targeted project without `--framework`
whichever launch profile is given. `net10.0-desktop` is the head to start from an IDE or by hand; `net10.0-browserwasm`
is the one an orchestration starts.

The ordinary local start is therefore the Aspire app host, which brings up the database, the migrated schema, the
service, and this client together — [running locally with Aspire](../../docs/operations/local-development.md#the-client-resource)
records what the client resource costs, what it needs installed, and how to run the orchestration without it. Starting
the browser head needs the `wasm-tools` workload; the Uno SDK is pinned in the repository-root `global.json` and
restored like any other.

**Which deployment the client reaches is a person's decision, and `DeploymentChoice` is the whole of it.** Four things
can hold an answer and they are read in one order, highest first:

1. **What somebody chose**, kept in `IDeploymentChoiceStore` — `ApplicationData.Current.LocalSettings` behind it, which
   is a per-user preferences store on a desktop and the browser's own storage for the page's origin in the browser
   head. It is read first because it is the most recent thing anybody actually decided, and it is what makes starting
   the client again opening it rather than configuring it.
2. **What the build stated**, through `BuildStatedDeploymentAddress`.
3. **What the head knows for itself**, through `IDeploymentAddressSource` — `ConfiguredDeploymentAddress` on an
   installed head, reading the `Deployment` keys **by name** out of the embedded `appsettings.json`;
   `PageOriginDeploymentAddress` on the browser head, reading `globalThis.location.origin` through `[JSImport]`,
   because MailFathom serves the bundle from the same origin as the surface it calls. By name rather than bound onto
   the record, because binding is reflection over properties and the browser head is trimmed, which is the reason this
   stack source-generates every serializer it uses.
4. **Nobody**, which is an ordinary state rather than a failure. `App.OnLaunched` opens `ConnectPage` instead of the
   application, and a person says where their MailFathom is. What used to happen here — a head refusing to start
   because nothing was configured — is gone: a window that cannot explain itself was only ever the least bad answer
   while there was no screen to ask on.

**A stated address that cannot be honoured still fails loudly.** Unreadable text in `appsettings.json` fails in
`ConfiguredDeploymentAddress`, naming the setting; an address the rule below refuses fails in `DeploymentChoice.Restore`,
naming the address. Only *absence* is answered by asking. Two answers are held to a weaker standard, and both for the
same reason — nobody wrote them, so nobody can go and correct them. A kept choice that no longer passes the rule is
forgotten rather than fatal. A page origin that does not pass it is no answer at all: `PageOriginDeploymentAddress`
judges it and reports nothing, so a bundle served over clear text from something that is not this machine opens the
screen that asks instead of failing at launch over a fact its reader could do nothing about.

**One rule judges every address, wherever it came from.** `DeploymentAddressRule.Judge` is it, and it is applied before
an address is stored, before one is probed, and again when the client is pointed at one: an absolute `http` or `https`
address, an origin and nothing beneath it, and clear text only to this machine, because every request carries the
signed-in credential. A value written with no scheme is read as HTTPS by `DeploymentAddressText`, which the screen and
the configuration reader share, since the alternative would turn an omission into a credential on the wire.
`DeploymentAddressRule.Describe` is the other half of owning the rule: every refusal is raised as an exception by
somebody and an exception message is read into a log, so the message that refuses an address for carrying embedded
credentials is exactly the one most likely to name a secret. It names the scheme and the authority and nothing else —
composed rather than taken from `GetLeftPart(UriPartial.Authority)`, which keeps the user information — and names
nothing at all of a value that is not an absolute address.

**Nothing is kept until something has answered.** `DeploymentProbe` asks the candidate for `/api/client/session` on a
transport carrying no credential — the address is a machine nobody has vouched for yet — and believes it only when the
answer is MailFathom's own document naming MailFathom. A deployment that refuses an unauthenticated caller is still a
deployment, because that is what a correctly configured one does; the probe reports that as reached-but-guarded rather
than as not MailFathom. This is what turns a typing mistake into a sentence on the screen a person is still looking at
rather than an authentication failure after they have entered a password.

**Pointing the client elsewhere ends the session.** `DeploymentAddress` holds the current address and drops the access
token when it moves, because a credential belongs to an owner on one deployment and means nothing on another. It is
also why `DeploymentClient`, `DeploymentSignIn`, and `DeploymentProbe` ask `IHttpClientFactory` for a transport per
exchange instead of holding one: a captured transport keeps the base address it was created with, and the client would
go on reaching the deployment somebody had just left.

**The build-stated source exists for the case none of the head answers fits.** A head an orchestration started is served
by a development server on a socket of its own while the service listens on another, so the origin it was fetched from
is a file server and there is no installation to have written anything. The channel is the build, because a browser
reads no process environment and has no file beside it — `Client.csproj` writes the value of the
`MailFathomDeploymentAddress` property into a runtime host configuration option, the WebAssembly SDK carries it into the
boot document the page fetches, and `AppContext` is what reads it back, with no reflection for the trimmer to remove.
The desktop head takes the same property and reads the same key out of its own `runtimeconfig.json`. A build that states
nothing writes no option, which is every published artifact:
[the client resource](../../docs/operations/local-development.md#the-client-resource) is the one thing that states it.

Add a head, and what it owes is an implementation of `IDeploymentAddressSource` — not a branch on the running platform,
and not a second mechanism invented in a screen.

## Reaching the backend

The client is an HTTP client of `backend/src/Host` and nothing more. It speaks to the endpoints that host exposes; it
never references a backend project, never links a backend source file, and never receives a type defined under
`backend/src/`.

**All of it lives in `src/Client.Backend/`**, and a screen reaches the service only through what that assembly
publishes. Nothing under `src/Client/` opens an `HttpClient`, holds a token, or writes a route — the boundary above is
what keeps that from being a rule somebody has to enforce by reading.

- **The contract is the wire format**, so the client declares its own types for what it sends and receives, named for
  what the client does with them. Two records that happen to have the same fields on both sides of an HTTP call are not
  duplication — they are one contract stated at each end, and coupling them would put a domain type in a view. The same
  holds for the OAuth code: `Client.Backend` carries its own proof key, anti-forgery value, and metadata addresses
  rather than sharing `backend/src/Common/OAuth/`, for exactly the reason nothing here reaches into `backend/`.
- **Serialization is source-generated.** The browser head is trimmed, and a reflection-based `ReadFromJsonAsync` is
  removed by the trimmer rather than reported; `.config/BannedSymbols.txt` already refuses those overloads. One
  `JsonSerializerContext` covers every document the client reads.
- **Where the deployment is, is nobody's to state here**, and `AddMailFathomDeployment` does not take it. What it takes
  is `DeploymentOptions` — the registered client identifier and the timeout — and it registers `DeploymentAddress`,
  which starts pointed at nothing and is pointed by whoever composes the application. There is no default address
  anywhere and nothing composes one from a literal, because a client that guessed would reach somebody else's
  deployment on a mistyped value. What this assembly does own is the rule that judges one: `DeploymentAddressRule`
  refuses a clear-text address to anything but this machine, because every request carries the signed-in credential and
  `http` to a routable host would hand it to whatever is on the path, and it refuses anything carrying more than an
  origin. `backend/src/Host/Configuration/DeploymentOptions.cs` draws the same line about the address that deployment
  publishes, and for the same reason.
- **Everything the backend returns about mail is personal data**, and the root instructions' classification follows it
  across the wire: it is not logged, not written to local storage without a stated reason, and not put in a telemetry
  event. A failure message never carries a deployment's own answer back either — the body is text from a machine this
  process does not own, and a screen that repeated it would be putting an attacker's words in MailFathom's voice.
- **The surface it reaches is `/api/client`**, the backend's third transport surface, served only where a deployment
  enabled `ClientEndpoint`. It is not the MCP endpoint and not the administrative one: a credential admitted by either
  of those authenticates nothing here. Today it answers one route, `GET /api/client/session`, reporting the running
  version and the grant the caller's credential carries and nothing that identifies that credential;
  [the client endpoint](../../docs/operations/client-endpoint.md) is the page.
- **Signing in is authorization code with PKCE**, which is the grant `mfctl` performs against the administrative
  surface and for the same reason: a desktop binary and a WebAssembly bundle are both readable by whoever runs them, so
  this is a public client and holds no secret. Where to sign in is discovered rather than configured — the deployment's
  RFC 9728 document names the authorization server, and that server's own discovery document names the endpoints — and
  a discovery document that does not report the issuer that led to it is refused, so nothing can move a sign-in to a
  server the deployment never named.
- **One step of that is head-specific, and it is a port**: `ISignInRedirectListener` puts the authorization page in
  front of the person and catches what comes back. The desktop head binds a loopback address and starts the platform's
  browser at it, as `mfctl` does; the browser head opens a window on the application's own origin and reads the
  redirect out of it. **The browser head must not navigate the document away** — that destroys the page along with the
  proof key and the anti-forgery value, leaving browser storage as the only place to put them back, which is exactly
  what this application does not do.
- **The credential lives in memory for the process's lifetime and nowhere else** — no file, no browser storage, no
  platform credential store — and it is not readable outside `Client.Backend`: a screen may ask whether somebody is
  signed in, and the handler in the transport pipeline is the only thing that sees it. So the session ends when the
  process does and the person signs in again.
- **Where it may be kept instead is settled and not yet built**, and
  [ADR 0018](../../docs/decisions/0018-where-the-client-keeps-its-sign-in-credential.md) is the whole of it — read the
  record before writing any of it rather than deciding a store while building a screen. It keeps the credential only
  where the operating system holds a secret for one user: the Credential Manager or the Data Protection API on Windows,
  the login keychain on macOS, Secret Service over D-Bus on Linux, and `PasswordVault` on a mobile head, which Uno backs
  with the Keystore and the Keychain but marks unsupported on its Skia targets — which is why the desktop head reaches
  its three operating systems itself. **The browser head keeps nothing**, because every store a browser offers is scoped
  to the page's origin rather than to a person, so anything running on the origin would read an owner's password.
  `Client.Backend` declares the port and is the only thing that reads the credential back out of it; a head's
  implementation under `Platforms/` reaches that platform's store and hands the value to nothing else, as
  `BrowserSignInRedirectListener` already does for a port of the same shape. A head whose store is absent or refuses
  falls back to memory and says so, never to a file beside the binary and never to
  `ApplicationData.Current.LocalSettings`, which holds the deployment address and no secret.
