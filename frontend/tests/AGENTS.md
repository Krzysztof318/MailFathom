# Client Unit Test Instructions

These instructions apply under `frontend/tests/` in addition to the repository root instructions.

The conventions in `frontend/src/AGENTS.md` apply to test code as well, as do the C# conventions and the shared
unit-test rules in the root file. A test here is C# compiled by the same build with the same analyzers, so naming,
async shape, the rule about what an `await` captures, and the rest hold exactly as they do under `frontend/src/`. The
directory cascade does not load that file for a change under `frontend/tests/`, which is why this line points at it —
the same arrangement `backend/tests/AGENTS.md` has with the server's.

What does *not* reach here is `backend/tests/AGENTS.md`. Its subject is a service's suites: the coverage denominator,
the boundary projects, the substituted mail library, the paid-provider switch, and the integration suite that starts
containers. None of those exists in this stack, and the cascade never loads that file for a change under `frontend/`.

## Unit testing policy

The repository's standing rule holds here rather than lapsing into "UI cannot be unit-tested": **a behaviour change
carries tests.** Most of a screen's behaviour lives in its model, and a model is a plain record that needs no visual
tree, so most of it is reachable by an ordinary unit test.

- **Do not adopt the Uno template's NUnit default, and do not add a second assertion library.** The root file states
  the framework and the runner both suites share; what this stack has to refuse is the template's own default.
- **Assert a feed by awaiting it** — `var value = await model.SomeFeed;` — or by enumerating its messages where the
  progress and error axes are the subject. Uno documents a `Uno.Extensions.Reactive.Testing` package with a `FeedTests`
  base and a `.Record()` extension for this; **it is not usable here.** The last version published to nuget.org is
  2.5.11, from September 2023, and it binds `Uno.Extensions.Reactive` 2.5.11 while the SDK pin resolves 7.2.3 — its
  `FeedRecorder` no longer compiles against the current API, and it would drag MSTest and VSTest into a suite that runs
  on Microsoft Testing Platform. Reach for it if a version matching the pinned Uno.Extensions line is ever published;
  until then the model's own public surface is the seam.
- **A model reaching a service is tested against a fake of that service**, hand-written or substituted at the port, the
  same way the backend tests an adapter boundary.
- **What genuinely needs a running head stays out of the unit suite**: XAML that has to be parsed into a visual tree,
  a layout that has to be measured, navigation that has to move a frame, and anything platform-specific. Reading
  authored XAML as files — the way `AuthoredViews` holds `x:Uid` and `AuthoredXaml` holds `FeedView` sources,
  templates, `ItemsSource`, commands, two-way bindings, and named visual states — is not that, and belongs here.
  Name a case that still needs a head in the pull request rather than asserting it indirectly, and leave it for the
  UI-test project a later change adds — an `Uno.UITest` head is a decision that has not been taken yet, so do not add
  one as a side effect of a feature.
- **The Uno App MCP is evidence, not a test.** A screenshot and a visual-tree snapshot are how a change is shown to
  work; they prove nothing on somebody else's machine and never stand in for a test. A change that edits XAML or
  styles still owes both of them in the session that wrote it, and they do not stand in for the authored-view
  assertions above. `$review-change` and `docs/operations/agent-workflow.md` name the same obligation, so skipping it
  fails review rather than only this page.

## Layout, and what runs the suite

- **A test project mirrors the source it covers**, exactly as `backend/tests/` does: `tests/Client.UnitTests/Presentation/`
  covers `src/Client/Presentation/`.
- **This is the one project in the stack that carries a lock file**, and restore runs in locked mode wherever it is
  gated. It targets plain `net10.0`, compiles no head, and its closure is the one this repository's own pins produce;
  `frontend/src/AGENTS.md` § *Project and build layout* states why the application project deliberately carries none.
- **Both local gates run this suite** when the change reaches this stack, from the same change filters `ci.yml` uses:
  `scripts/verify-fast.sh` and `scripts/verify-full.sh` restore, build, and test `frontend/MailFathom.Client.slnx`, and
  the `Frontend` job of `CI` runs the same three commands on a pull request. There is no coverage threshold here —
  `.config/CodeCoverage.proj` measures projects under `backend/src/`, and no number is asked of this suite.
