# Changelog

All notable changes to MailFathom are recorded here, in the format of
[Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/). Versions follow
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html) as
[ADR 0004](docs/decisions/0004-versioning-and-release-policy.md) interprets it over MailFathom's four public surfaces:
the MCP tool contract, the configuration schema, the database schema, and the deployment contract.

What earns an entry is what a consumer of a release would notice — anything reaching one of those four surfaces, a
fixed defect that was observable from outside, and any change with a security consequence. A refactor, a test, a
continuous-integration adjustment, a documentation edit, and an internal rename get none, and the correct entry for a
change nobody outside can observe is no entry at all. Entries are written by the change that causes them rather than
reconstructed at release time.

A breaking entry opens with `**Breaking (<surface>)**` and states the operator's action rather than only the fact. A
release that touches the database schema says whether a migration must be applied, whether it can be applied while the
previous version is still running, and whether the release can be deployed over the previous release's data at all.

MailFathom is pre-release. Within `0.x` a minor bump may break any of the four surfaces, and every break is named
below against the surface it breaks; a patch is compatible on all four. Nightly builds get no section of their own,
because they are whatever `Unreleased` currently describes.

## [Unreleased]

### Added

- The host reports which build it is running: its startup record now carries the stamped version and the commit the
  assemblies were built from, and the MCP server reports its version to a client during `initialize`
  ([#119](https://github.com/Krzysztof318/MailFathom/issues/119)).
- The container image and the Helm chart name the version the build stamps, so a pulled artifact identifies itself
  without being run ([#119](https://github.com/Krzysztof318/MailFathom/issues/119)).

[Unreleased]: https://github.com/Krzysztof318/MailFathom/commits/main
