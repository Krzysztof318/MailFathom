<img src="assets/icon-900.png" alt="MailFathom" width="128" align="right">

# MailFathom

MailFathom is a clean-architecture modular monolith for synchronizing mail from IMAP accounts, storing a durable local copy, indexing messages for retrieval, and exposing read-only mail capabilities through MCP.

## Current baseline

This repository currently contains the .NET 10 solution scaffold, central build/package configuration, initial runtime boundaries, Microsoft Testing Platform v2 test-project setup, and basic documentation structure.

## Verify

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format --verify-no-changes
```

## Contributing

[CONTRIBUTING.md](CONTRIBUTING.md) gets you from a clone to a passing verification run and states the rules a pull request has to satisfy. [docs/operations/local-development.md](docs/operations/local-development.md) is the full setup, and [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) applies to everyone taking part.

## Security

MailFathom holds mailbox credentials, OAuth tokens, certificate material, and a local copy of someone's mail. Report a vulnerability privately through [SECURITY.md](SECURITY.md) rather than in a public issue.

## License

MailFathom is licensed under the [Apache License, Version 2.0](LICENSE), SPDX identifier `Apache-2.0`. Source files repeat that grant in a header the build enforces, and a published artifact carries `LICENSE` and `NOTICE` beside the binaries. The container image is that same publish output, so it carries both files and declares `org.opencontainers.image.licenses`; the Helm chart states the identifier as `artifacthub.io/license`.

MailFathom was originally created by **Krzysztof Kasprowicz**. The root [NOTICE](NOTICE) records that attribution, which section 4(d) of the license asks a derivative distribution to preserve while it remains relevant to the derived work. A fork may add its own attribution notices beside it. The notice adds no use restriction, changes nothing about the license, and claims nothing about contributions written by other copyright holders.

Contributions to this repository are offered under Apache-2.0, by section 5 of the license. There is no contributor licence agreement and no developer certificate of origin, and contributors keep the copyright in what they write.

Third-party components that MailFathom consumes are reviewed separately in [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md). That register records what MailFathom depends on and under which terms; it grants nothing in MailFathom itself, which `LICENSE` alone does.

The application icon in [`assets/`](assets) is MailFathom's own asset rather than a third-party component, and the same grant covers it. The register records how it was produced and why no one else holds rights in it.
