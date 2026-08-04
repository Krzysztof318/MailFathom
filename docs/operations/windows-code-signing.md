# Signing the Windows CLI binaries

<!-- describes: .github/workflows/sign-cli-binaries.yml, .github/workflows/build-cli-binaries.yml -->

`mfctl` is published for four platforms, and the two Windows ones are Authenticode-signed by
[SignPath Foundation](https://signpath.org/) under its free program for open-source projects. This page records what
the release pipeline does with them, what has to exist in SignPath and in this repository's settings for it to work,
and what a failure means.

The Linux binaries carry no signature. Authenticode is a Windows format, and the checksum file published beside them is
what verifies those.

## Why a release signs at all

An operator administers a MailFathom deployment from their own workstation rather than from the machine the service
runs on, so `mfctl.exe` is downloaded onto a desktop and run there. Unsigned, it meets SmartScreen and an
unknown-publisher prompt, and the only thing that distinguishes a genuine download from a tampered one is a checksum
the operator has to think to check. A signature moves that decision into the operating system.

## Where signing sits in the release

`Release` builds the binaries, signs them, verifies the signatures, and only then attaches anything to the GitHub
release:

| Job | Runner | What it does |
| --- | --- | --- |
| `CLI binaries` | `ubuntu-latest` | Publishes the four self-contained binaries and uploads them as one artifact |
| `Sign the CLI binaries` / `Submit the signing request` | `ubuntu-latest` | Submits that artifact to SignPath, waits for the signed result, regenerates the checksum file |
| `Sign the CLI binaries` / `Verify the Authenticode signatures` | `windows-latest` | Verifies each signature against the Windows trust engine, then attests the result |
| `Publish the GitHub release` | `ubuntu-latest` | Attaches the signed artifact |

Three properties of that order are deliberate.

**Signing happens before publication, not after.** A signature is written into the PE file, so signing changes the
bytes. An artifact signed after the release page already offered it would be a different file from the one operators
downloaded, and the checksums would describe neither.

**The checksum file is regenerated after signing.** The build produces one covering the binaries as built, which on the
release path is not what anybody downloads. `Sign the CLI binaries` deletes it and takes fresh checksums over the
signed bytes. The build still produces its own because a nightly publishes no signature and that artifact has to be
self-consistent on its own.

**Verification runs on Windows.** `Get-AuthenticodeSignature` asks the operating system's own trust engine, so a
`Valid` verdict is the decision an operator's machine will reach. Verifying the same file on Linux would check the
chain against a CA bundle that does not model the Windows root program, which answers a different question. This is
the only Windows runner in the repository, and this is what it is for.

The verification step fails the run when a binary came back unsigned, when the signature does not match the bytes, when
the certificate is not trusted, when the signer does not match `SIGNPATH_EXPECTED_SIGNER`, or when the signature
carries no timestamp. An untimestamped signature stops verifying the day the certificate expires, so a release must not
carry one.

## What a failure does

Signing gates the CLI binaries and nothing else. A release whose image, chart, and schema artifact are correct is still
a release, and the command is the part an operator can wait for — so a failure anywhere in `Sign the CLI binaries`
leaves a red job beside a published release rather than no release.

What it never does is fall back. `Publish the GitHub release` downloads the signed artifact by name and has no branch
that reaches the unsigned one, so a failed signature means no CLI binaries on the release page rather than unsigned CLI
binaries on it. Re-running the release after fixing the cause attaches them.

A nightly signs nothing. Every Foundation signing request is approved by a person, and a channel that publishes on a
schedule cannot ask for that.

## Configuration in this repository

Nothing below is written into a workflow file. The identifiers are repository variables and the token is a repository
secret, both under **Settings → Secrets and variables → Actions**.

| Name | Kind | What it holds |
| --- | --- | --- |
| `SIGNPATH_ORGANIZATION_ID` | Variable | The organization id from SignPath, a GUID shown under **Organization → Settings** |
| `SIGNPATH_PROJECT_SLUG` | Variable | The slug of the SignPath project created for MailFathom |
| `SIGNPATH_SIGNING_POLICY_SLUG` | Variable | The slug of the signing policy the release submits against, typically `release-signing` |
| `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | Variable | The slug of the artifact configuration describing the archive being signed |
| `SIGNPATH_EXPECTED_SIGNER` | Variable | A substring the signing certificate's subject must contain, `SignPath Foundation` |
| `SIGNPATH_API_TOKEN` | Secret | A REST API token for a SignPath user holding submitter permission on that signing policy |

`SIGNPATH_EXPECTED_SIGNER` is required rather than optional, and the run fails when it is empty. A `Valid` Authenticode
status says a trusted certificate authority vouched for whoever signed the file; it does not say the signer was this
project. Checking the subject is what turns the first statement into the second, and skipping the check when the
variable is unset would silently accept any certificate the runner happens to trust.

The token authorizes submission alone. Approving a signing request is a separate permission and stays a person's act.

## Configuration in SignPath

Four things have to exist on SignPath's side before a release can sign.

**A project**, named for this repository, with the GitHub repository linked to it as its origin. SignPath verifies that
the artifact it was handed came from a run of that repository, which is why the workflow passes an artifact id rather
than uploading bytes: the connector downloads the artifact through the GitHub API itself.

**A trusted build system** entry for GitHub Actions. The Foundation requires that every job leading to a signing
request ran on a GitHub-hosted runner, which the release pipeline satisfies.

**An artifact configuration** matching the archive `actions/upload-artifact` produces. That action always uploads a ZIP,
so the root element is `<zip-file>`, and the binaries sit at its root rather than in a directory:

```xml
<?xml version="1.0" encoding="utf-8"?>
<artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
  <zip-file>
    <pe-file path="mfctl-*-win-x64.exe">
      <authenticode-sign />
    </pe-file>
    <pe-file path="mfctl-*-win-arm64.exe">
      <authenticode-sign />
    </pe-file>
  </zip-file>
</artifact-configuration>
```

The wildcard stands in for the version, which changes every release. The Linux binaries and the checksum file are not
named, so they travel through the request and come back untouched.

This configuration lives in SignPath rather than in this repository, which is the one part of the pipeline no gate here
can validate: renaming a published binary in `Build the CLI binaries` without mirroring the change into the artifact
configuration is a mismatch that nothing in a pull request would catch. `Sign the CLI binaries` therefore checks the
returned archive against the four names it expects before it takes checksums, so the mismatch surfaces as a named
failure during the release rather than as a quietly unsigned download afterwards.

**A signing policy** for releases, configured to timestamp and to require approval. Its slug is what
`SIGNPATH_SIGNING_POLICY_SLUG` carries.

## What the Foundation requires of the project

The certificate is issued to SignPath Foundation, which makes the Foundation the publisher Windows names. Eligibility
carries obligations beyond the pipeline:

- An OSI-approved license without commercial dual-licensing. MailFathom is Apache-2.0.
- Attribution on the pages that offer the download. The repository `README.md` carries it, and the release notes repeat
  it on every release.
- Signed files carry product name and version metadata. `Directory.Build.props` sets `Product` and `FileVersion`
  centrally, so the published `.exe` carries both in its version resource.
- Each release is approved by a person in SignPath before a certificate touches anything.

## Verifying a downloaded binary

**`<version>` below is the release you downloaded** — substitute it. Both commands quote the name, so a line pasted
without that substitution fails with a missing file rather than with a redirection.

On Windows, the file's properties dialog carries a **Digital Signatures** tab, or:

```powershell
Get-AuthenticodeSignature '.\mfctl-<version>-win-x64.exe' | Format-List Status, SignerCertificate
```

A `Status` of `Valid` is the whole answer: the bytes match the signature, the chain is trusted, and the publisher is
named. `SignerCertificate` is what shows that the publisher is SignPath Foundation rather than merely somebody a
certificate authority vouched for.

Every published file, signed or not, also carries a build provenance attestation naming the workflow and commit that
produced it:

```bash
gh attestation verify 'mfctl-<version>-win-x64.exe' --repo Krzysztof318/MailFathom
```

The signature says who vouches for the file; the attestation says where it came from. They are different questions and
both are worth asking.
