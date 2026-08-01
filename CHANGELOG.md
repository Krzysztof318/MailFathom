# Changelog

All notable changes to MailFathom are recorded here, in the format of
[Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/). Versions follow
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html) as
[ADR 0004](docs/decisions/0004-versioning-and-release-policy.md) interprets it over MailFathom's four public surfaces:
the MCP tool contract, the configuration schema, the database schema, and the deployment contract.

**This file is written by the release pull request and by nothing else.** Ordinary work does not touch it — not a
feature, not a fix, not a refactor — because a changelog is a statement about a *release*, and a release is what the
tagged and published pull request makes. `$prepare-release` composes each section from the work merged since the
previous tag, and that same pull request is the one whose merge commit is tagged and published to the container
registries. `CHANGELOG.md` is a protected path for the same reason: an edit to it outside that flow changes what a
release claims it shipped.

What earns an entry is what a consumer of a release would notice — anything reaching one of the four surfaces, a fixed
defect that was observable from outside, and any change with a security consequence. A refactor, a test, a
continuous-integration adjustment, a documentation edit, and an internal rename earn none.

A breaking entry opens with `**Breaking (<surface>)**` and states the operator's action rather than only the fact. A
release that touches the database schema says whether a migration must be applied, whether it can be applied while the
previous version is still running, and whether the release can be deployed over the previous release's data at all.

MailFathom is pre-release. Within `0.x` a minor bump may break any of the four surfaces, and every break is named
below against the surface it breaks; a patch is compatible on all four. Nightly builds get no section of their own,
because they are, by definition, whatever has been merged since the newest section below.

## [Unreleased]

Before the initial release. Everything in the repository so far is work towards `0.1.0` and none of it has been
published, so there is nothing here a consumer could have upgraded from and nothing to describe as a change.

`0.1.0` is the first section this file will carry. `$prepare-release` composes it from what merged up to that point,
in the pull request whose merge commit is tagged and published — see the
[`0.1.0 — first public release`](https://github.com/Krzysztof318/MailFathom/milestone/1) milestone and issue
[#210](https://github.com/Krzysztof318/MailFathom/issues/210).

[Unreleased]: https://github.com/Krzysztof318/MailFathom/commits/main
