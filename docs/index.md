---
_disableContribution: true
---

# MailFathom documentation

<!-- describes: none -->

**A brain for your mail — self-hosted, AI-native, and yours alone.**

MailFathom is a self-hosted service that synchronizes mail from your IMAP accounts into a local PostgreSQL copy,
indexes it for search, and serves it to AI agents as read-only tools over the
[Model Context Protocol](https://modelcontextprotocol.io/). Reading is local, and synchronization never marks anything
read on the mail server.

![A chat client asked to show the latest mail, answered with a table of the ten most recent messages, their receipt times, and the moment the local copy was last synchronized](https://raw.githubusercontent.com/Krzysztof318/MailFathom/main/assets/mcp-tools/list-recent-emails.png)

*One question, answered from the local copy in an ordinary chat client. The `***` were blacked out by hand before the
file entered a public repository — MailFathom redacts nothing on its way to a client.*

This page is the front door of the published site. It exists only there: the file GitHub shows when you browse
`docs/` is [`README.md`](https://github.com/Krzysztof318/MailFathom/blob/main/docs/README.md), which indexes the same
pages for somebody reading them in the repository.

## Where to start

| If you want to | Read |
| --- | --- |
| Install, configure, and use MailFathom | [The user guide](users/README.md) |
| Deploy, operate, and troubleshoot a running instance | [Operations](operations/configuration-reference.md) |
| Understand what the tools return and what they bound | [Features](features/mcp-tools.md) |
| Follow the boundaries the code is built on | [Architecture](architecture/solution-structure.md) |
| Look up a type or a member | [API reference](api/index.md) |

## Which version you are reading

**The site opens on the current release.** The selector in the header moves between the releases that are still
documented and `latest`, which is built from the default branch and therefore describes work no release carries yet —
a page there can document a setting you cannot configure in the version you are running. Every page states its version
in that same selector, and a page outside the current release says so in a banner.

## What this site leaves out

The [repository](https://github.com/Krzysztof318/MailFathom) holds what is deliberately not published here: the
architectural decision records under
[`docs/decisions/`](https://github.com/Krzysztof318/MailFathom/tree/main/docs/decisions), which are a closed record of
why a decision was taken rather than documentation of how MailFathom behaves; the specifications under
[`specs/`](https://github.com/Krzysztof318/MailFathom/tree/main/specs), which state intent rather than fact; and the
instructions that govern the agents and contributors working on it. MailFathom is published under the
[Apache License 2.0](https://github.com/Krzysztof318/MailFathom/blob/main/LICENSE).
