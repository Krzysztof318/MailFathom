# The desktop client

<!-- describes: .github/workflows/build-desktop-client.yml, frontend/src-tauri/** -->

What a release publishes for somebody who wants MailFathom's client as an application on their own machine rather than
as a page a deployment serves: which platforms, in which formats, what each installer does, and what none of them does.

The client has two heads and one source tree. The **web head** is the bundle every published container image carries and
a deployment serves once it is switched on — [the client endpoint](client-endpoint.md) is that page. The **desktop
head** is the same bundle wrapped in a Tauri shell, built per platform and attached to a release as an installer. This
page is about the second one. Nothing here is a local command; [building and testing the
client](local-development.md#building-and-testing-the-client) is how a developer builds either head.

## What is published, and for what

| Platform | Format | Built on |
|---|---|---|
| Linux, x86-64 | `.deb` | `ubuntu-24.04` |
| Linux, x86-64 | `.rpm` | `ubuntu-24.04` |
| Windows, x86-64 | NSIS `-setup.exe` | `windows-latest` |

`frontend/src-tauri/tauri.conf.json` is where those three formats are chosen, and the build publishes what that
configuration produces rather than a list of its own. **A nightly is the one exception, and it publishes no `.rpm`**:
RPM's `Version` tag admits no hyphen, a nightly's version carries a prerelease identifier introduced by exactly that
character, and Windows needs the same string to parse as SemVer — so the two cannot be satisfied by one number, and
`src-tauri/run-tauri.ts` drops that one target rather than publishing a package whose version field is malformed. A
release, whose version is a plain `x.y.z`, builds all three.

**Two runners rather than one, and that is the difference from every other artifact this project publishes.** The
service's container image and the `mfctl` binaries are built for every platform on one Linux runner, because an IL
publish with a runtime identifier crosses freely. A desktop head does not: it links against the target platform's
WebView and system libraries — WebKitGTK on Linux, WebView2 on Windows — so each platform is built on its own runner,
and neither can stand in for the other. The Linux runner is pinned rather than left to follow whatever is newest,
because the packages link against its glibc and that makes the image a compatibility floor: a `.deb` or an `.rpm` built
on Ubuntu 24.04 needs glibc 2.39 or later, so a distribution released before it — Ubuntu 22.04, Debian 12 — installs
the package and cannot start the application. Build it yourself there, or use the web head, until that floor moves.

Nothing is published for macOS, and neither Android nor iOS is a head this project builds at all.

## What an installer does, and what it does not

- **It installs the application and its own bundle**, which is the client's compiled web assets and the Rust shell that
  opens a window over them. MailFathom's own `LICENSE` and `NOTICE` are installed with it, in the application's
  resource directory, and the notice covering the three packages the bundle itself carries travels inside the bundle
  as `THIRD-PARTY-NOTICES.txt`.
- **It brings no rendering engine on Linux.** Both packages link against the WebKitGTK and GTK your distribution
  ships and patches rather than carrying a copy. The `.deb` names `libwebkit2gtk-4.1-0` and `libgtk-3-0` as
  dependencies, so `apt` installs them with it; the `.rpm` names none, because those same libraries carry different
  package names on Fedora and on openSUSE and one wrong name makes a package refuse to install rather than pull
  something in. On an RPM distribution, install `webkit2gtk4.1` — or whatever your distribution calls it — beside the
  package.
- **On Windows it will fetch one.** The NSIS installer runs Microsoft's WebView2 bootstrapper when the machine has no
  WebView2 runtime, which needs an internet connection at install time; Windows 11 and every up-to-date Windows 10
  already carry it.
- **It installs per user on Windows**, into `%LOCALAPPDATA%` rather than `C:\Program Files`, so it asks for no
  administrator rights.
- **It carries no service and no database.** The desktop client is a client: it reaches a MailFathom deployment over
  [the client surface](client-endpoint.md) exactly as the web head does, and it needs one to be running and reachable
  before it can sign in.
- **It keeps the password in the machine's own credential store.** Signing in once is enough: the shell hands the
  credential to the Credential Manager on Windows and to the session's Secret Service on Linux — the two platforms
  this head is published for — filed under the address of the deployment it was given for, and signing out removes it. Where
  the store refuses the write, the client signs in anyway and says the password was not kept, and where it refuses the
  removal it says so rather than reporting a sign-out that did not happen. What is stored is one value
  and the client keeps no copy of it beside that. A machine offering no such store — a Linux session running no keyring
  daemon is the usual case — is told so on the sign-in screen and asked for the password again the next time the
  application opens, rather than having it written somewhere less safe instead.
- **It never updates itself.** No update channel exists and the shell links no updater, so a new version is a new
  download from a release page.
- **It is not signed.** No bundle published here carries an Authenticode signature or a Linux package signature, so
  Windows warns about an unknown publisher and a package manager reports the package as unsigned.

## Verifying a download

One checksum file covers every bundle a release publishes, named `mailfathom-client-<version>.sha256`, and it is taken
over exactly the bytes the release page carries — nothing rewrites a bundle between the build and the release. Because
nothing is signed, that file is the whole of what verifies a download:

```bash
sha256sum --check --ignore-missing mailfathom-client-0.8.0.sha256
```

Run it from the directory you downloaded into, with the checksum file beside whichever bundles you took.

## Why no AppImage

Tauri can bundle a Linux application as an AppImage, and this project deliberately does not publish one. An AppImage
works by packaging the host's own GTK and WebKitGTK shared objects into the artifact, several of which are
LGPL-2.1-or-later — so publishing one would mean redistributing those libraries under conditions this project would
then owe an operator a source offer for. A `.deb` and an `.rpm` depend on the distribution's copies instead and carry
none of it, which is why they are the two Linux formats published.
[`THIRD_PARTY_LICENSES.md`](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md) records that
reading against the crate closure the shell links.

## Where it is built, and what a failure costs

[`build-desktop-client.yml`](https://github.com/Krzysztof318/MailFathom/blob/main/.github/workflows/build-desktop-client.yml)
is the only thing that builds these bundles. It is `workflow_call` only, and two channels call it:
[`release.yml`](https://github.com/Krzysztof318/MailFathom/blob/main/.github/workflows/release.yml), which attaches the
result to the release, and
[`nightly.yml`](https://github.com/Krzysztof318/MailFathom/blob/main/.github/workflows/nightly.yml), which leaves it on
the run — a nightly is not a release and has nothing to attach to, so whoever tries a nightly image takes the client
from the run that produced it.

**It gates nothing on either channel.** A release whose desktop build failed is still a release whose image, chart, and
schema artifact are correct, and withholding those because an installer did not build would help nobody; the failure is
a red job beside a published release. That is the same posture
[the command binaries](release-procedure.md) are published under. What the build does wait for is verification: both
channels build it only from a commit whose tests, and on a release the integration suite, have already passed.

Neither verification script builds a desktop head, and neither does the client's pull-request gate. A bundle takes a
Rust toolchain, a platform's development packages, and minutes of compilation, none of which a change to a screen
should wait on — so the pull-request answer about the client is its linter, type check, unit suites, and browser suite,
and the bundle is built where a bundle is actually wanted.

## The version a bundle carries

`<VersionPrefix>` in `Version.props` is the only application version number in this repository, and a desktop bundle
gets it the way every other artifact does. The channel resolves the commit and the version once, hands both to the
build, and the build merges that number into the Tauri configuration — so the file names, the installer, and the
version the installed application reports are one number that no part of the build resolved for itself. A nightly's
bundles therefore carry that channel's full identifier rather than the release number the tree happens to declare.
