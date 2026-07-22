# Codex Cloud Tooling Design

## Goal

Make the Superpowers workflows and the Microsoft Learn MCP server available to developers working with this repository in Codex Cloud, without relying on files from an individual developer's home directory.

## Constraints

- Repository skills must be discovered directly from a checkout.
- The Microsoft Learn integration must use its public Streamable HTTP endpoint and must not require repository secrets.
- Cloud plugin activation remains subject to the user's ChatGPT plan, workspace policy, and plugin installation or sharing controls.
- Third-party Superpowers files must remain attributable and pinned to a known version.
- The repository must not depend on a setup script downloading mutable content at session startup.

## Selected Design

### Superpowers skills

Vendor the complete Superpowers 6.1.1 skill set under `.agents/skills/`. Codex scans this repository location for skills, so the workflows are available from the checked-out revision without a separate installation step.

Keep the upstream directory structure and referenced support files intact. Record the pinned upstream version and preserve the MIT license alongside the vendored files. Updates will be explicit repository changes rather than automatic downloads.

### Microsoft Learn MCP plugin

Create a repository-local plugin under `plugins/mailmcp-microsoft-learn/` containing:

- `.codex-plugin/plugin.json` for plugin identity and component wiring;
- `.mcp.json` with the public `https://learn.microsoft.com/api/mcp` Streamable HTTP endpoint;
- no credentials, setup scripts, hooks, or application code.

Expose the plugin through `.agents/plugins/marketplace.json`. A developer can install it from the repository marketplace and a workspace administrator or plugin owner can share or assign it to the intended users. Repository checkout alone cannot bypass Cloud workspace plugin policies, so this activation boundary must be documented explicitly.

The plugin contains only Microsoft Learn MCP configuration. Superpowers remains repository-scoped to avoid making core development workflows depend on plugin installation and to avoid duplicating the vendored skill files.

### Operational documentation

Add `docs/operations/codex-cloud-tooling.md` after implementation. It will describe:

- which capabilities are automatic after checkout;
- how to install and share the Microsoft Learn plugin;
- how to start a new Codex session after activation;
- how workspace policy or unavailable plugin support appears to users;
- how to update the pinned Superpowers snapshot safely.

## Data and Control Flow

1. Codex Cloud checks out the repository.
2. Codex discovers Superpowers metadata from `.agents/skills/` and loads individual skill instructions on demand.
3. The user or workspace enables the repository's Microsoft Learn plugin.
4. A new Codex session receives the plugin's MCP tool definitions.
5. Microsoft documentation searches and article retrievals go to the public Microsoft Learn MCP endpoint; no MailMcp source code, credentials, or message content are required by the integration.

## Failure Modes

- If a skill is missing a referenced file, validation fails before commit.
- If the plugin or marketplace manifest is invalid, the plugin will not be offered for installation; local manifest validation must catch this.
- If workspace policy disables custom plugins or MCP servers, repository files cannot override that policy. The operations guide will identify administrator enablement as the remedy.
- If Cloud cannot reach the Microsoft endpoint, the skill set remains available but Microsoft Learn tools do not. Users should verify workspace/plugin availability and Cloud network policy rather than adding credentials.
- If the Microsoft Learn endpoint changes, the pinned plugin configuration requires an explicit reviewed update.

## Verification

- Validate all vendored `SKILL.md` frontmatter and ensure skill names are unique.
- Verify that support files referenced by the vendored skills exist.
- Validate plugin and marketplace JSON syntax and resolve every local marketplace path.
- Confirm the plugin manifest points to the bundled MCP configuration.
- Check the vendored version and license against the selected Superpowers 6.1.1 source.
- Run repository formatting and diff checks applicable to documentation and configuration changes.
- Inspect the final diff for secrets, unexpected binaries, unrelated changes, and accidental source modifications.

## Sources

- [Codex skills and plugins documentation](https://learn.chatgpt.com/docs/build-skills)
- [Codex plugin documentation](https://learn.chatgpt.com/docs/build-plugins)
- [Microsoft Learn MCP Server overview](https://learn.microsoft.com/en-us/training/support/mcp)
- [Superpowers releases](https://github.com/obra/superpowers/releases/tag/v6.1.1)
