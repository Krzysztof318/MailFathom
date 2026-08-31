---
_disableContribution: true
---

# API reference

<!-- describes: none -->

This reference is generated from the XML documentation comments in MailFathom's own source, and documents the code as
the release named in the version selector carries it. Only this introduction is in the repository; everything under it
is produced at build time, so a type that is renamed or removed changes the reference in the same commit.

**None of it is a supported public API.** MailFathom ships as a service, a command-line client, and an application
rather than as a library: nothing here is packed to NuGet, and the four surfaces the versioning policy treats as consumed are the
configuration keys, the database schema, the MCP tool contracts, and the HTTP endpoints — not these types. Read this
reference to follow how a contract is implemented, not to take a dependency on it.

## What it covers

A namespace appears here when its boundary exposes something publicly. [The solution structure](../architecture/solution-structure.md)
describes what each of them is for, and why the dependencies between them point the way they do. Only the service
stack appears: `frontend/` carries no build while the client is rebuilt in React, and a client written in JavaScript
would not be read by this generator in any case.

| Boundary | What you will find |
| --- | --- |
| [Domain](xref:MailFathom.Domain) | The business concepts and their invariants, with no dependency on any framework |
| [Application](xref:MailFathom.Application) | The use cases and the ports they are served through |
| [Infrastructure](xref:MailFathom.Infrastructure) | Persistence, IMAP and SMTP, content storage, security, and observability |
| [Mcp](xref:MailFathom.Mcp) | The protocol mapping, and nothing else |
| [Common](xref:MailFathom.Common) | What more than one boundary needs and none of them owns |
| [Host](xref:MailFathom.Host.Configuration.Provisioning) | One exception type, which is all of the host that is public |

## What it does not cover, and why that is the point

**`AI` and `Cli` declare no public type at all, and `Host` declares one.** They are not missing from the reference —
there is nothing in them to document. Each is composition rather than capability: `Host` wires configuration,
endpoints, and process lifetime together, `Cli` assembles the `mfctl` commands, and `AI` holds the retrieval work that
has not been built yet. Code like that is `internal` because nothing outside its own assembly calls it, and the
absence here is the architecture stating that: a boundary with no public surface is a boundary nothing takes a
dependency on.

The Aspire orchestration project is excluded outright rather than by visibility. It composes a developer's machine
rather than anything a deployment runs, and [local development](../operations/local-development.md) documents it.
