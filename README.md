# MailMcp

MailMcp is a clean-architecture modular monolith for synchronizing mail from IMAP accounts, storing a durable local copy, indexing messages for retrieval, and exposing read-only mail capabilities through MCP.

## Current baseline

This repository currently contains the .NET 10 solution scaffold, central build/package configuration, initial runtime boundaries, Microsoft Testing Platform v2 test-project setup, and basic documentation structure.

## Verify

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format --verify-no-changes
```
