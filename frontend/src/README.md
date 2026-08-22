# Client sources

This directory holds the client application, the way `backend/src/` holds the service's projects. It carries one
project — `Client/`, an Uno Platform single project whose assembly is `MailFathom.Client` — and that project carries
every head the application runs as rather than one project per platform.

Two heads are built today. `net10.0-desktop` is the Skia desktop head, which runs on Windows, Linux, and macOS, and
`net10.0-browserwasm` is the browser head. A third target framework, plain `net10.0`, builds no head: it is the
reference target [`frontend/tests/`](../tests/README.md) references the application through, since a test host is
neither a browser nor a window. The mobile heads are not here yet; adding them is a change to `TargetFrameworks` in
`Client/Client.csproj` and to nothing else.

What is inside the project:

| Path | What it holds |
|---|---|
| `Client/App.xaml`, `App.xaml.cs` | The composition root: the host every head starts through, logging, and the route registry |
| `Client/Presentation/` | The shell, the pages, and the MVUX models behind them |
| `Client/Styles/` | The Material palette every brush resolves from — the one place a colour value is written |
| `Client/Platforms/` | What belongs to one head only: the two entry points, and the browser head's web manifest, linker configuration, and font stylesheet |
| `Client/Assets/`, `Client/Strings/` | The application icon and splash screen, and the string table |

The application is empty of features. It shows what it is — the product name and the version this build was stamped
with, read from the assembly rather than written here, so the client and the service report one number.

It reaches MailFathom over the endpoints `backend/src/Host/` exposes and shares nothing else with it: no build file, no
package manifest, no configuration file, and no type. `AGENTS.md` beside this file states why, and states the
conventions anything added here follows.
