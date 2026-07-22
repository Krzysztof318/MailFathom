# Codex Cloud Tooling

This repository includes shared Codex tooling for MailMcp development:

- Superpowers 6.1.1 skills stored in `.agents/skills/`;
- a repository plugin that connects Codex to the public Microsoft Learn MCP server.

## Superpowers skills

Codex discovers repository skills from `.agents/skills/`. Start a new Codex session from this repository after pulling the files. The skills require no home-directory installation, setup script, or network download.

The vendored snapshot contains the complete upstream skill set and its supporting resources. `.agents/skills/SUPERPOWERS_VERSION` records the pinned version and `.agents/skills/SUPERPOWERS_LICENSE` preserves the upstream MIT license.

The root `.gitattributes` disables Git whitespace diagnostics only for `.agents/skills/**`. This preserves upstream content exactly while leaving ordinary repository files subject to the normal whitespace checks.

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
8. Confirm `.gitattributes` still scopes the whitespace exception only to `.agents/skills/**`.
9. Review and commit the complete snapshot as one dependency update.

Do not download mutable Superpowers content from a setup script. A reviewed repository update keeps Cloud sessions reproducible and makes third-party changes visible in the Git history.


## .NET development tooling

Codex Cloud runs `.codex/setup.sh` when a new repository environment is prepared. The script installs the .NET SDK from the official Microsoft `dotnet-install.sh` endpoint into `${DOTNET_INSTALL_DIR:-$HOME/.dotnet}` using channel `10.0`, exports `DOTNET_ROOT` to that SDK directory, then prepends that directory and `${DOTNET_CLI_HOME:-$HOME}/.dotnet/tools` to `PATH`.

The repository also includes `global.json` so .NET CLI commands resolve to SDK `10.0.100` or a later .NET 10 feature band when Codex Cloud has already cached a compatible SDK. This keeps command behavior aligned with the repository rule that MailMcp targets .NET 10.

After the SDK is available, the setup script installs or updates these command-line tools for the Codex Cloud user:

- `dotnet-ef` version `10.0.10`, installed as a global .NET tool so migrations and design-time EF Core commands are available through `dotnet ef`;
- Aspire CLI version `13.4.6`, installed as the `Aspire.Cli` global .NET tool from NuGet so `aspire` commands are available for future AppHost work.

The setup is intentionally user-local and does not use `sudo`, system package managers, repository secrets, or application package references. Re-run `.codex/setup.sh` to refresh the SDK/tooling in an existing environment. To override defaults for a diagnostic session, set `DOTNET_CHANNEL`, `DOTNET_INSTALL_DIR`, `DOTNET_CLI_HOME`, `DOTNET_INSTALL_SCRIPT_URL`, `DOTNET_EF_VERSION`, or `ASPIRE_CLI_VERSION` before invoking the script.

Verify a prepared environment with:

```bash
dotnet --info
dotnet ef --version
aspire --version
```

If setup fails while downloading scripts or tools, first check Codex Cloud network access to `dot.net` and `nuget.org`. If `dotnet` commands still select the wrong SDK, confirm that `$HOME/.dotnet` appears before older SDK locations in `PATH` and that `global.json` remains at the repository root.

## References

- [Codex skills](https://learn.chatgpt.com/docs/build-skills)
- [Codex plugins](https://learn.chatgpt.com/docs/build-plugins)
- [Microsoft Learn MCP Server](https://learn.microsoft.com/en-us/training/support/mcp)
- [Superpowers releases](https://github.com/obra/superpowers/releases)
