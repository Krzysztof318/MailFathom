# Client Instructions

These instructions apply under `frontend/` in addition to the repository root instructions. Everything the root file
states holds here — the English rule, the branch rule, the licensing header, the documentation obligations, and the
privacy classification of anything that touches mail — and nothing below repeats one of them.

What does *not* reach here is `backend/src/AGENTS.md`. Those are the conventions of a service: API and failure design,
dependency injection over a generic host, outbound HTTP client lifetimes, asynchronous return types at a use-case
boundary. A view is none of those things, and the directory cascade never loads that file for a change under
`frontend/`, so this one carries the client's half in full rather than by reference.

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

## Project and test layout

```
frontend/
  MailFathom.Client.slnx     the client solution; the backend solution never references a project here
  Directory.Build.props      the client's build contract, importing the root Version.props
  Directory.Packages.props   the client's own pins; Uno's own packages come from the SDK
  src/Client/                the application — one Uno single project, every head inside it
  tests/Client.UnitTests/    the unit suite
```

- **The two stacks share no build file.** `frontend/Directory.Build.props` and `frontend/Directory.Packages.props` are
  this stack's own, and a property that has to hold in both is written in both rather than lifted to the root — with
  one exception, `Version.props`, which both import so the client and the service report one number.
- **The Uno version is a single pin**: `msbuild-sdks` in the repository-root `global.json`. Every Uno package version
  follows from it, which is why almost nothing Uno publishes appears in `Directory.Packages.props`. A capability is
  asked for through `UnoFeatures` in the project file rather than by adding a package reference.
- **Lock files are committed here too**, and restore runs in locked mode wherever it is gated. Regenerate deliberately
  with `dotnet restore frontend/MailFathom.Client.slnx --force-evaluate` in the change that moves a pin, and read the
  transitive diff.
- **A test project mirrors the source it covers**, exactly as `backend/tests/` does: `tests/Client.UnitTests/Presentation/`
  covers `src/Client/Presentation/`.
- **Nothing under `frontend/` reaches into `backend/`**, and `backend/MailFathom.slnx` names no project here. Both
  verification scripts build the backend solution alone, so a client change proves itself by building this solution and
  running this suite.

## Reaching the backend

The client is an HTTP client of `backend/src/Host` and nothing more. It speaks to the endpoints that host exposes; it
never references a backend project, never links a backend source file, and never receives a type defined under
`backend/src/`.

- **The contract is the wire format**, so the client declares its own types for what it sends and receives, named for
  what the client does with them. Two records that happen to have the same fields on both sides of an HTTP call are not
  duplication — they are one contract stated at each end, and coupling them would put a domain type in a view.
- **Serialization is source-generated.** The browser head is trimmed, and a reflection-based `ReadFromJsonAsync` is
  removed by the trimmer rather than reported; `.config/BannedSymbols.txt` already refuses those overloads.
- **Everything the backend returns about mail is personal data**, and the root instructions' classification follows it
  across the wire: it is not logged, not written to local storage without a stated reason, and not put in a telemetry
  event.
- **How the client authenticates has not been decided.** Until it is, do not invent one — no token cache, no credential
  store, no refresh loop.

## Testing

The repository's standing rule holds here rather than lapsing into "UI cannot be unit-tested": **a behaviour change
carries tests.** Most of a screen's behaviour lives in its model, and a model is a plain record that needs no visual
tree, so most of it is reachable by an ordinary unit test.

- **The suite is xUnit.net v3 on Microsoft Testing Platform**, the same framework and runner `backend/tests/` uses, so
  one `dotnet test` contract covers the repository. Do not adopt the Uno template's NUnit default, and do not add a
  second assertion library.
- **Name a test `Member_Scenario_ExpectedBehavior`** and follow Arrange, Act, Assert, as the backend suites do.
- **Assert a feed by awaiting it** — `var value = await model.SomeFeed;` — or by enumerating its messages where the
  progress and error axes are the subject. Uno documents a `Uno.Extensions.Reactive.Testing` package with a `FeedTests`
  base and a `.Record()` extension for this; **it is not usable here.** The last version published to nuget.org is
  2.5.11, from September 2023, and it binds `Uno.Extensions.Reactive` 2.5.11 while the SDK pin resolves 7.2.3 — its
  `FeedRecorder` no longer compiles against the current API, and it would drag MSTest and VSTest into a suite that runs
  on Microsoft Testing Platform. Reach for it if a version matching the pinned Uno.Extensions line is ever published;
  until then the model's own public surface is the seam.
- **A model reaching a service is tested against a fake of that service**, hand-written or substituted at the port, the
  same way the backend tests an adapter boundary.
- **What genuinely needs a running head stays out of the unit suite**: XAML that has to be parsed, a layout that has to
  be measured, navigation that has to move a frame, and anything platform-specific. Name such a case in the pull
  request rather than asserting it indirectly, and leave it for the UI-test project a later change adds — an
  `Uno.UITest` head is a decision that has not been taken yet, so do not add one as a side effect of a feature.
- **The Uno App MCP is evidence, not a test.** A screenshot and a visual-tree snapshot are how a change is shown to
  work; they prove nothing on somebody else's machine and never stand in for a test.
