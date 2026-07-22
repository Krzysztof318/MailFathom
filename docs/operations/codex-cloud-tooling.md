# Codex Cloud Tooling

This repository includes shared Codex tooling for MailMcp development:

- Superpowers 6.1.1 skills stored in `.agents/skills/`;
- a repository plugin that connects Codex to the public Microsoft Learn MCP server.

## Superpowers skills

Codex discovers repository skills from `.agents/skills/`. Start a new Codex session from this repository after pulling the files. The skills require no home-directory installation, setup script, or network download.

The vendored snapshot contains the complete upstream skill set and its supporting resources. `.agents/skills/SUPERPOWERS_VERSION` records the pinned version and `.agents/skills/SUPERPOWERS_LICENSE` preserves the upstream MIT license.

## Microsoft Learn plugin

The `mailmcp-microsoft-learn` plugin uses the public Streamable HTTP endpoint:

```text
https://learn.microsoft.com/api/mcp
```

Microsoft does not require authentication for this endpoint. The plugin contains no credentials and exposes read-only documentation discovery capabilities.

The repository marketplace is `.agents/plugins/marketplace.json` and appears as `MailMcp Repository`. Install `MailMcp Microsoft Learn` from that marketplace in a supported Codex plugin browser, then start a new Codex session so the MCP tools are loaded.

For Codex Cloud team availability:

1. Open the repository plugin from the ChatGPT desktop app or another supported Codex plugin surface.
2. Install and test `MailMcp Microsoft Learn` in a new low-risk session.
3. Share it with the intended ChatGPT workspace members or groups.
4. When workspace administration is available, assign or install the plugin for the eligible roles that should use it.
5. Start a new Codex Cloud session after installation or policy changes.

Repository files cannot override plan availability, workspace plugin policy, role assignments, MCP allowlists, or Cloud network policy. If the plugin is absent, ask the workspace administrator to confirm that custom plugins and MCP servers are allowed and that the plugin is installed for the user's role. If tools load but calls fail, verify Cloud reachability to `learn.microsoft.com`.

## Updating Superpowers

Treat Superpowers as a pinned third-party dependency:

1. Review the current official Superpowers release and its Codex compatibility notes.
2. Replace every vendored skill directory with the complete skill set from one release.
3. Update `.agents/skills/SUPERPOWERS_VERSION` to that exact release version.
4. Replace `.agents/skills/SUPERPOWERS_LICENSE` with the license from the same release.
5. Compare the vendored directories recursively with the selected upstream snapshot, including executable file modes recorded by Git.
6. Confirm that every first-level skill directory contains `SKILL.md` and that its frontmatter name is unique.
7. Run shell and JavaScript syntax checks for bundled scripts.
8. Review and commit the complete snapshot as one dependency update.

Do not download mutable Superpowers content from a setup script. A reviewed repository update keeps Cloud sessions reproducible and makes third-party changes visible in the Git history.

## References

- [Codex skills](https://learn.chatgpt.com/docs/build-skills)
- [Codex plugins](https://learn.chatgpt.com/docs/build-plugins)
- [Microsoft Learn MCP Server](https://learn.microsoft.com/en-us/training/support/mcp)
- [Superpowers releases](https://github.com/obra/superpowers/releases)
