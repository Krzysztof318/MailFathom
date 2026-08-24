# Publishing the client

<!-- describes: frontend/src/Client/Client.csproj, frontend/Directory.Build.props, .github/workflows/build-desktop-client.yml, deploy/docker/Dockerfile -->

Which of the client's heads is trimmed, which is compiled ahead of time, and why each answer is what it is. The two
heads are published by two different builds for two different audiences, and the optimizations available to one are
mostly unavailable to the other — so this page states the posture once rather than leaving it to be rediscovered per
pull request.

Nothing here is a local command. [Building and testing the client](local-development.md#building-and-testing-the-client)
is how a developer builds it; this page is about what a *published* head carries.

## The two publishes

`frontend/src/Client/Client.csproj` declares three target frameworks and two of them are heads.

| Head | Published by | Reaches somebody as |
|---|---|---|
| `net10.0-browserwasm` | the client stage of [`deploy/docker/Dockerfile`](https://github.com/Krzysztof318/MailFathom/blob/main/deploy/docker/Dockerfile) | the page a deployment serves, when it enabled [the client endpoint](client-endpoint.md) |
| `net10.0-desktop` | [`.github/workflows/build-desktop-client.yml`](https://github.com/Krzysztof318/MailFathom/blob/main/.github/workflows/build-desktop-client.yml), for `win-x64` and `win-arm64` | a `.zip` attached to a release, or left on a nightly run |

The third target framework, plain `net10.0`, builds no head: the unit suite references the application through it.
Nothing on this page applies to it.

## What is on and what is off

| | `net10.0-browserwasm` | `net10.0-desktop` |
|---|---|---|
| IL trimming (`PublishTrimmed`) | **on** — the WebAssembly SDK publishes trimmed by default | **off**, and refused rather than merely unset |
| XAML resource trimming (`UnoXamlResourcesTrimming`) | **on** | **off**, because it is a pass of the trimmer above |
| Native AOT (`PublishAot`) | **impossible** | **off**, and declined |
| Ahead-of-time compilation of any kind | **off** — the head is interpreted | **off** |
| Single-file publish, assembly merging | off | off, and refused |

Every one of those is a property of a *publish*, so none of them runs in a verification script or in a pull-request
workflow — see [Where an optimization is allowed to cost time](#where-an-optimization-is-allowed-to-cost-time).

## XAML resource trimming, on the browser head

`UnoXamlResourcesTrimming` is set for `net10.0-browserwasm` alone, in `Client.csproj`, conditioned on that one target
framework. It is not a second trimmer: Uno's pass runs inside the IL trimmer's own invocation, reading the application's
XAML to decide which controls are never referenced and telling the trimmer that their styles — found through each
`Style`'s `TargetType` — need not be kept. It therefore does something only where the IL trimmer already runs, which on
this project is the browser head and nothing else.

**No control has to be told to keep, today.** The pass decides from what the XAML names, and every control this
application uses is named in `App.xaml`, `Presentation/Shell.xaml`, or `Presentation/MainPage.xaml` — there is no
`XamlReader` call anywhere in the client and no control resolved from a string at run time. That is the condition under
which the pass is safe, rather than a fact about it: a control whose only use is dynamic is one the pass cannot see, its
style is removed, and the failure arrives when the screen loads rather than when the branch builds. If that changes, the
answer is an [IL Linker descriptor](https://platform.uno/docs/articles/features/resources-trimming.html#aggressive-trimming)
keeping the control or its namespace — not turning the pass off.

### Trim warnings stay warnings

`frontend/Directory.Build.props` sets `ILLinkTreatWarningsAsErrors` to `false`, and it is the one place this stack
steps back from `TreatWarningsAsErrors`. The trimmer reports on every assembly in the graph rather than on the ones
written here, and Uno.UI, the navigation router, and the Toolkit each raise `IL2104` that only their own authors can
fix — with `NETSDK1144` for the run behind it. A warning about code written *here* is answered where it is raised,
with a justified suppression naming why the reflection it reports is rooted.

## Native AOT does not exist for the browser head

`net10.0-browserwasm` runs on Mono, whose WebAssembly runtime offers Interpreter, Jiterpreter, Mixed
Interpreter/AOT, and Profile-Guided AOT. None of those is .NET Native AOT, and Uno's own
[Native AOT page](https://platform.uno/docs/articles/features/native-aot.html) names Android, iOS, Linux, macOS, and
Windows and no browser target at all. `PublishAot` on that head is not a decision that was taken one way; there is
nothing to take.

What the head runs today is the interpreter. The levers above it are real and are deferred rather than rejected —
[Profile-guided AOT, deferred](#profile-guided-aot-deferred) says what the nearest one costs.

## Native AOT is declined for the desktop head

Uno 6.6 — the version [`global.json`](https://github.com/Krzysztof318/MailFathom/blob/main/global.json) pins — supports
`PublishAot` on the Skia desktop head across Linux, macOS, and Windows, and publishes measured Uno.Chefs numbers for
each: on Windows, startup 1.605 s → 0.824 s for a publish that grows 725 MB → 970 MB. Uno's older
[Skia desktop page](https://platform.uno/docs/articles/features/using-skia-desktop.html#net-native-aot-support) still
says Native AOT is unsupported on Windows because WPF does not support it. **The newer page supersedes it**: it names
the pinned version and carries evidence, and the older one is describing a constraint that no longer applies to the
Skia head.

It is still declined here, for two reasons that stack.

**A licence condition forbids it, and that alone settles it.** `LibVLCSharp.dll` is in the `net10.0-desktop` graph —
`Uno.WinUI.Runtime.Skia.X11` declares it unconditionally, and a `win-x64` publish carries it beside the application
like any other assembly. It is LGPL-2.1-or-later, and
[ADR 0016](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0016-third-party-licence-obligations-per-artifact.md)
holds what a shipped artifact then owes: the library stays unmodified and separately replaceable. Native AOT compiles
the whole managed graph into one native image, which is the opposite of a file a recipient can swap. The same clause is
what keeps `PublishTrimmed` off that head, and with it the XAML resource trimming above, which Uno measures as a
Win32 desktop app going from 200 MB to 52 MB — a real loss, taken deliberately.
[`THIRD_PARTY_LICENSES.md`](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md) carries the
verdict and `frontend/src/AGENTS.md` the properties it leaves unset. Reopening any of it is a fresh licence review
rather than a build flag.

**And it could not be built where the desktop head is built.** A Native AOT publish runs the native toolchain of the
platform it targets, so it does not cross-compile. `build-desktop-client.yml` publishes both Windows heads from
`ubuntu-latest`, which works precisely because those publishes are IL with a runtime identifier — verified by
publishing both from Linux, where the apphost, `coreclr.dll`, and `libSkiaSharp.dll` come out as PE32+ images for
x86-64 and ARM64 respectively. Enabling `PublishAot` would cost a `windows-latest` runner for `win-x64`, and would
leave `win-arm64` with no host at all: GitHub offers no Arm64 Windows runner, so that head would stop being published
rather than start being faster.

## The desktop publish command, and the one parameter it omits

`build-desktop-client.yml` publishes each head with `--framework net10.0-desktop --runtime <rid> --self-contained true`
and the Release configuration on the restore as well as on the publish. Uno's
[publishing page](https://platform.uno/docs/articles/uno-publishing-desktop.html#publish-using-the-cli) asks for one
more parameter when a runtime identifier is given — `-p:TargetFrameworks=net10.0-desktop` — on the grounds that the
restore in front of the publish evaluates every target framework the project declares.

**That parameter is deliberately not passed, because here it breaks the publish.** A `-p:` value is an MSBuild *global*
property, and a global property flows into every project reference. `Client` references `Client.Backend`, which targets
plain `net10.0` and is built by the .NET SDK rather than the Uno one, so it receives `TargetFrameworks=net10.0-desktop`
and fails immediately:

```text
error NETSDK1139: The target platform identifier desktop was not recognized. [.../Client.Backend.csproj::TargetFramework=net10.0-desktop]
```

The command without it succeeds for both runtime identifiers and produces the head it was asked for. Uno's page is
written for a single-project application, where there is no reference for the property to reach; a solution whose head
references another project is the case it does not cover.

## Where an optimization is allowed to cost time

An ahead-of-time or trimmed publish costs minutes that a pull request should not wait on, so nothing enabled here runs
in front of one.

- `scripts/verify-fast.sh` and `scripts/verify-full.sh` restore, build, and test the client solution. Neither publishes
  any head.
- `.github/workflows/build-test-frontend.yml`, the client's pull-request gate, does the same three and no publish.
- The browser head is published by the container image build:
  [`publish-container-image.yml`](https://github.com/Krzysztof318/MailFathom/blob/main/.github/workflows/publish-container-image.yml)
  on a release and a nightly, and
  [`container-image.yml`](https://github.com/Krzysztof318/MailFathom/blob/main/.github/workflows/container-image.yml) on
  manual dispatch. Neither is a required check.
- The desktop head is published by `build-desktop-client.yml`, which is `workflow_call` only and is called by
  [`release.yml`](https://github.com/Krzysztof318/MailFathom/blob/main/.github/workflows/release.yml) and
  [`nightly.yml`](https://github.com/Krzysztof318/MailFathom/blob/main/.github/workflows/nightly.yml) alone.

## Profile-guided AOT, deferred

The lever that would actually shrink the browser head is Profile-Guided AOT: the Mono runtime compiles the methods a
recorded session actually executed to WebAssembly and interprets the rest, which Uno measures on its own RayTracer
sample as 5.5 MB uncompressed becoming 2.9 MB. It is deferred, and what defers it is that there is nothing to profile
yet rather than any doubt about the technique.

Taking it needs two passes and a file that ages:

1. Publish the head with `WasmShellGenerateAOTProfile` set, run it without a debugger, and use the application
   through the places that matter — the profile records the methods that session executed and nothing else.
2. Save `aot.profile` from the running application, put it under `Platforms/WebAssembly/`, unset the generation
   property, and set `WasmShellMonoRuntimeExecutionMode` to `InterpreterAndAOT` for the publish.

The profile is a snapshot of the application it was recorded from. A client whose screens are still being written would
carry a profile describing screens that no longer exist within a release or two, and re-recording it is a person
driving the application by hand rather than anything a build can repeat. It becomes worth doing once the screens
somebody uses every day have settled, and it is worth measuring before and after rather than only after: a number
nobody recorded is not a saving anybody can claim.
