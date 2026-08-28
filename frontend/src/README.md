# Client sources

This directory holds the client application, the way `backend/src/` holds the service's projects. It carries two
projects. `Client/` is an Uno Platform single project whose assembly is `MailFathom.Client`, and it carries every head
the application runs as rather than one project per platform. `Client.Backend/` is everything that talks to the
service, and it targets plain `net10.0` with no Uno package in its graph — which is what makes the boundary between a
view and a service call something the compiler refuses rather than something a reviewer checks.

Two heads are built today. `net10.0-desktop` is the Skia desktop head, which runs on Windows, Linux, and macOS, and
`net10.0-browserwasm` is the browser head. A third target framework, plain `net10.0`, builds no head: it is the
reference target [`frontend/tests/`](../tests/README.md) references the application through, since a test host is
neither a browser nor a window. The mobile heads are not here yet; adding them is a change to `TargetFrameworks` in
`Client/Client.csproj` and to nothing else.

What is inside the two projects:

| Path | What it holds |
|---|---|
| `Client/App.xaml`, `App.xaml.cs` | The composition root: the host every head starts through, logging, and the route registry |
| `Client/ClientComposition.cs` | Every service the application registers, beside the launch path rather than inside it, so the graph can be composed and resolved without a window |
| `Client/Presentation/` | The shell, the frame the three spaces are shown inside, the spaces themselves, and the MVUX models behind them |
| `Client/Styles/` | The Material palette every brush resolves from — the one place a colour value is written |
| `Client/Platforms/` | What belongs to one head only: the two entry points, the browser head's web manifest, linker configuration, and font stylesheet, and its half of the sign-in redirect |
| `Client/Assets/` | The application icon and the splash screen, from one SVG per head |
| `Client/Strings/` | One string table per language the application is readable in, keyed by the neutral culture |
| `Client.Backend/` | The typed client, the wire records, and the source-generated readers for them |
| `Client.Backend/Authorization/` | Signing in: discovery, the proof key, the exchange, and the token held in memory for the run |

The application opens on the frame the three spaces are shown inside, and the section below says what that frame is.
One of the three reads something today: Mail draws the mail of wherever the tree says somebody is. The other two are
still empty, and what the application can do beside them is what a reader decides about it — which language it is read
in, and which theme it is shown in — beside the version this build was stamped with, read from the assembly rather than
written here so the client and the service report one number.

It reaches MailFathom over the endpoints `backend/src/Host/` exposes and shares nothing else with it: no build file, no
package manifest, no configuration file, and no type. Which deployment it reaches is the composing host's to state, and
nothing states it yet. `AGENTS.md` beside this file states why, and states the conventions anything added here follows.

## How it is put together

The product is three spaces — **Discover**, **Mail**, and **Cases** — and they are one application rather than three.
What makes them one is the frame around them, which is what `Client/Presentation/Workspace/` holds and what a client
already pointed at a deployment opens on. `Client/Presentation/Spaces/` carries a page per space; Mail's holds the
message list described below, and Discover's and Cases' say they are empty until the parent that owns each fills its
own page in.

**Each space is a route, and so is everything else on screen.** `App.RegisterRoutes` nests `Discover`, `Mail`, and
`Cases` inside the frame and registers `Settings` and `Connect` beside it, every one of them named once in
`ClientRoutes`, so each is reachable and reloadable by its route — the
browser opened at `/Workspace/Cases` lands on Cases with that space named on the bar. Because Uno's navigation is what
moved there, the Android system back gesture and back key and the browser's back button all move back through the
client's own screens rather than out of the application: going to settings and back returns to the space somebody left,
not to the first one. A screen reached by swapping content by hand would be a screen neither of them knows about, which
is why nothing here does that.

**Where a route is registered decides whether it can be returned from**, which is why settings is a level up rather
than a fourth name beside the three. The spaces share one content area and are switched between, so moving among them
leaves nothing behind to go back to — which is what a bar of tabs means. Settings is entered instead, and entering is
what puts the workspace behind it.

**The composition follows the width of the window and never the platform.** Below 700 pixels the spaces are named on a
bar along the bottom and the workspace is one column; at 700 that bar is replaced by a navigation rail beside the
workspace; at 1000 the workspace itself opens a companion column, so what a space shows beside its main content stands
next to it instead of being reached as a separate screen. The two widths are `WorkspaceRailWindowWidth` and
`WorkspaceWideWindowWidth` in `App.xaml`, read by every adaptive trigger that acts on them, and the switch is
`VisualStateManager` throughout: no code asks which platform is running, so a resized desktop window and a phone are
the same case. `WorkspaceColumns` is the control that gives a space that workspace, so all three read the same at the
same width. Content stays clear of a notch, a rounded corner, and a system bar through `utu:SafeArea.Insets`, stated at
the root of the frame and again on the settings screen, which takes the whole surface rather than opening inside it.
The soft keyboard is the one mask the toolkit supports on a `SafeArea` or a `ScrollViewer` alone, so it is stated as a
control around the field this frame takes typing in.

**What travels with somebody between spaces is `IWorkspace`**, registered once for the run. It holds the question being
composed and the scope that question would be asked against — an account, a folder within it or a special-use role
taken across all of them, and what is selected there — and the frame shows both above the spaces: the field a question
is typed in, and the indicator beside it saying what it would be asked against. Neither answers anything here. Asking
is Discover's work; what this settles is that both survive the move, because both are held for the run rather than by
the model of whichever screen is on top. A scope names accounts, folders, special-use roles, and message identifiers, so
all four carry the same classification as anything else about mail.

**Narrowing is the mailbox tree's work**, which `Client/Presentation/Mailboxes/` holds and the frame shows in a pane
beside the spaces. One tree carries every account and the folder hierarchy beneath each, with the unread and stored
counts the service reports, and choosing a row is the one act that writes `IWorkspace.Scope` — so the list, the search,
and the field's scope indicator all read what was chosen there rather than each holding a selector of its own. Three
kinds of row narrow to something no folder does: every mailbox at once, a special-use role taken across accounts, and
an account without a folder. A folder is placed by the role the service gave it rather than by what it is called, which
is what puts a Polish `Odebrane` and an English `Inbox` under one heading, and a level of a server's hierarchy that
holds no folder of its own is drawn and narrows nothing. An account that cannot be reached and a copy merely behind are
said apart in words, neither as a spinner: a spinner claims something is arriving.

**Where somebody was working outlives the run**, which is the one thing about a scope this client writes down.
`IMailboxTreeMemory` keeps the chosen scope and the keys of the opened rows in `ApplicationData.LocalSettings`, so
starting the client again opens the tree where it was left rather than collapsed on everything, and `SharedWorkspace`
reads the same value so the scope and the tree cannot disagree about it. What is selected *inside* a scope is never
written: a selection names mail somebody was reading a moment ago rather than where they work, and the mail it names
may be gone by the next run. What is written is a folder alias, a hierarchy path, and an account identifier, which are
mail metadata and therefore personal data — held on the device, in the store the platform gives this application alone,
never logged and never sent anywhere.

**The mail of that place is one list, and it is the run's own.** `Client/Presentation/Messages/` holds it, and
`IMessageList` is registered once for the run beside the tree and the workspace — so leaving Mail and coming back finds
the list where it was rather than reading its first page again. It follows the *place* the scope names rather than the
whole scope, which is load-bearing rather than an optimization: the list writes what is selected back into that same
scope, and a list keyed on the whole of one would read the folder again on every click.

**What is loaded is a bounded window rather than everything somebody has scrolled past.** A page is asked for by cursor
in either direction, four of them are held at once, and taking a page at one end drops the one at the other — so the
number of rows the client holds is a property of the window rather than of how long the list has been scrolled, whatever
the folder holds. Dropping a page is safe because each one keeps the request that produced it: the page that becomes the
far end carries the cursor that reads back towards what went. That same value is what is written down when a folder is
left, so returning is a continuation rather than a jump to the top — remembered to the page rather than to the pixel,
since a page is the unit a cursor names. Every place one run visited is kept in memory, bounded; the one somebody is in
outlives the run in `ApplicationData.LocalSettings`, as a cursor and an arrangement and never as a row. A cursor a
deployment no longer honours is a position to give up rather than an error to show, so the list quietly opens at the
leading end instead.

**A row is drawn from the page that carried it and from nothing else** — sender or recipients, subject, date, read,
flagged and answered marks, whether there is an attachment, and the opening of the message's own text — so fifty rows
are one request rather than fifty. It carries the message's own identity as its key, which is what lets a refresh update
the rows that changed and leave the containers of the rest alone, and it states itself once as a sentence for a screen
reader instead of as six unlabelled controls. How the list is arranged — which end leads, and whether it keeps only
unread, only flagged, only mail carrying an attachment, or lets junk take part — is shown on the controls that did it
and is remembered with the place, because a cursor is only honoured against the filters and the order it was issued
under.

**What is selected is the application's rather than the control's.** The list mirrors its selection into
`IWorkspace.Scope`, which is what lets a question in Discover be asked about mail somebody picked in Mail; on a pointer
head that is the ordinary extended selection with its modifiers and its drag, and on a touch head a press and hold puts
the list into a selecting mode that is visible and can be left. Loading, a read that failed, a folder holding no mail,
and a folder whose mail this list is keeping out are four rendered states rather than one blank list — the last two are
told apart in words, and how current the copy is stays the tree's to say.

**Settings is reached from the frame rather than named among the spaces**, because it is not one. It is the screen that
says which build is running and which deployment this client is pointed at, carries the two choices below and the way to
point it at another deployment, and carries the way back out as a navigation request rather than as a handler — the same
request the system back key makes, so one answer serves both.

## How it looks

The client wears MailFathom's own colours rather than the Uno template's. Four are sampled from the product mark in
[`assets/icon-900.png`](../../assets/icon-900.png) — the navy ground, the blue that carries the envelope and the
circuit traces, the envelope's pale blue, and the star's gold — and each becomes a Material Design 3 tonal palette in
`Client/Styles/ColorPaletteOverride.xaml`, sampled at the tone every semantic role is defined at. So the file is
generated from four decisions rather than chosen one key at a time, and every key Uno's palette declares carries a
value in both the light and the dark dictionary. The gold is the one role that is reserved rather than general: it is
the tertiary role and it marks what the model contributed, which is why nothing else takes it.

Every pair a reader or a control depends on is measured rather than asserted. The unit suite reads that file and fails
below WCAG AA — 4.5:1 for text and 3:1 for an outline or a control state — so a value edited to look better and a role
repointed at another tone both arrive as a failing test naming the pair.

The application offers **light**, **dark**, and **follow the system**, through `Uno.Extensions.Toolkit.IThemeService`
and its `AppTheme`, which the `ThemeService` feature brings in and `UseThemeSwitching()` registers. Following the
operating system is a value of that enum rather than a mechanism beside it, so a reader who never chooses is already in
it, a choice is written to the platform's own settings store and survives a restart on both heads, and `System` tracks
the operating system flipping while the application is running.

The application icon and the splash screen carry that mark rather than the template's, drawn in the sampled colours
rather than in palette keys — an icon is not themed, and the palette was derived from those colours rather than the
other way round. Both grounds are the navy, named by `UnoIconBackgroundColor` and `UnoSplashScreenColor` in
`Client/Client.csproj` rather than painted in an SVG, because the Uno resizetizer composes the ground itself. The
splash therefore reads the same whichever theme the application starts in.

## What it reads in

The client is readable in **English** and **Polish**, and English is the default: it is what `DefaultLanguage` in
`Client/Client.csproj` declares, so a head asked for a language neither table carries falls back to it, and a first run
by somebody who has configured nothing is readable before any setting has been found.

The two are one decision written in two places. `Client/Strings/en/Resources.resw` and `Client/Strings/pl/Resources.resw`
hold the words, and `LocalizationConfiguration:Cultures` in `Client/appsettings.json` names which of them
`Uno.Extensions.Localization.ILocalizationService` offers a person to choose from — a culture named there with no table
beside it would reach somebody as a screen with no words on it. So the unit suite derives both lists rather than naming
either: it reads the cultures out of the embedded configuration and the tables out of the directories under `Strings/`,
fails when one list holds a language the other does not, and fails again when a key is in one table and missing from
another. Both are neutral cultures rather than regional variants; a variant arrives when something actually differs
between regions.

A visible string reaches a screen through `x:Uid` rather than being written in a page, which is why no page here
carries user-visible text. What is per item rather than per control is the exception — the three theme offers, and what
the scope indicator says — and each of those is resolved in the model through `IStringLocalizer` against the same
tables.

**A `x:Uid` nothing answers is a control with no words on it**, which the application compiles without complaint and a
head shows without saying why. So the suite reads the authored pages the way it reads the tables, and fails when a name
a view states is answered by no entry — the failure that holding the tables against each other cannot see, because a
name missing from both is a name they agree about.

**A chosen language arrives on the next launch, and the screen says so.** Uno applies a culture while a head is
starting, so the visual tree already built keeps the words it was built with; the choice is written to a settings file
of the application's own and survives a restart, and an `InfoBar` states that plainly rather than leaving somebody
pressing the button again because nothing appeared to happen. A theme needs no restart, which is why only one of the
two pickers has a button beside it.

The browser head loads the whole ICU data set rather than the shards the WebAssembly SDK ships by default. Left alone,
the runtime picks one shard from the reader's browser languages, and a browser reporting English resolves no `pl` at
all — so Polish would disappear from the picker for exactly the person most likely to want it, on a build whose Polish
table is present and correct. `Client/Client.csproj` carries the property and the reasoning; the desktop head resolves
cultures from the operating system and needs none of it.
