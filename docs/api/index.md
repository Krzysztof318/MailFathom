---
_disableContribution: true
---

# API reference

<!-- describes: none -->

This page introduces the reference the documentation site generates from the source; the reference itself is generated
at build time and is not in the repository, so nothing under this directory but this page is committed.

This reference is generated from the XML documentation comments in MailFathom's own source, one namespace tree per
architectural boundary. It documents the code as the release named in the header carries it.

**None of it is a supported public API.** MailFathom ships as a service and a command-line client rather than as a
library: nothing here is packed to NuGet, and the four surfaces the versioning policy treats as consumed are the
configuration keys, the database schema, the MCP tool contracts, and the HTTP endpoints — not these types. Read this
reference to follow how a contract is implemented, not to take a dependency on it.

The boundaries the namespaces follow are described in
[the solution structure](../architecture/solution-structure.md):

- **Domain** — the business concepts and their invariants, with no dependency on any framework.
- **Application** — the use cases and the ports they are served through.
- **Infrastructure** — persistence, IMAP and SMTP, content storage, security, and observability.
- **AI** — retrieval, chunking, and embeddings.
- **Mcp** — the protocol mapping, and nothing else.
- **Host** — composition, configuration, endpoints, and process lifetime.
- **Cli** — the `mfctl` command.
- **Common** — what more than one boundary needs and none of them owns.

The orchestration project that runs the local development topology is left out: it composes a developer's machine
rather than anything a deployment runs. [Local development](../operations/local-development.md) documents it.
