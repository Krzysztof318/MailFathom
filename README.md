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

## License

MailFathom is licensed under the [Apache License, Version 2.0](LICENSE), SPDX identifier `Apache-2.0`. Source files repeat that grant in a header the build enforces, and a published artifact carries `LICENSE` and `NOTICE` beside the binaries. The container image is that same publish output, so it carries both files and declares `org.opencontainers.image.licenses`; the Helm chart states the identifier as `artifacthub.io/license`.

MailFathom was originally created by **Krzysztof Kasprowicz**. The root [NOTICE](NOTICE) records that attribution, which section 4(d) of the license asks a derivative distribution to preserve while it remains relevant to the derived work. A fork may add its own attribution notices beside it. The notice adds no use restriction, changes nothing about the license, and claims nothing about contributions written by other copyright holders.

Contributions to this repository are offered under Apache-2.0, by section 5 of the license. There is no contributor licence agreement and no developer certificate of origin, and contributors keep the copyright in what they write.

Third-party components that MailFathom consumes are reviewed separately in [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md). That register records what MailFathom depends on and under which terms; it grants nothing in MailFathom itself, which `LICENSE` alone does.
