# Codex Cloud Tooling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make pinned Superpowers skills automatically discoverable from this repository and package the Microsoft Learn MCP endpoint as a repository plugin for Codex Cloud distribution.

**Architecture:** Vendor the complete Superpowers 6.1.1 skill snapshot into Codex's repository skill location so no installation or startup download is required. Keep Microsoft Learn as a separate, read-only repository plugin exposed through the repo marketplace because Cloud plugin activation is governed by user and workspace policy.

**Tech Stack:** Codex Agent Skills, Codex plugin manifests, Codex repository marketplaces, JSON, Markdown, Git.

## Global Constraints

- Pin Superpowers to version `6.1.1` and preserve its MIT license.
- Store repository skills under `.agents/skills/`.
- Use the unauthenticated Streamable HTTP endpoint `https://learn.microsoft.com/api/mcp`.
- Do not add secrets, setup downloads, hooks, application code, or `.codex/config.toml` MCP fallback configuration.
- Keep Superpowers independent from plugin installation.
- Document that Cloud workspace policy and plugin installation or sharing remain required for Microsoft Learn MCP availability.
- Never commit directly on `main` or add co-author trailers.

---

### Task 1: Vendor the pinned Superpowers skill snapshot

**Files:**
- Create: `.agents/skills/<superpowers-skill-name>/**`
- Create: `.agents/skills/SUPERPOWERS_VERSION`
- Create: `.agents/skills/SUPERPOWERS_LICENSE`

**Interfaces:**
- Consumes: Superpowers `6.1.1` files from the verified installed plugin snapshot.
- Produces: repository-discoverable `SKILL.md` files and all support resources referenced by them.

- [x] **Step 1: Copy the complete skill directories**

Copy every directory from the verified Superpowers `6.1.1` `skills/` folder into `.agents/skills/` without changing file content or executable bits.

- [x] **Step 2: Record version and licensing metadata**

Create `.agents/skills/SUPERPOWERS_VERSION` with exactly:

```text
6.1.1
```

Copy the upstream MIT license verbatim to `.agents/skills/SUPERPOWERS_LICENSE`.

- [x] **Step 3: Verify the snapshot**

Run:

```bash
diff -qr /home/krzysztof/.codex/plugins/cache/openai-curated-remote/superpowers/6.1.1/skills .agents/skills --exclude SUPERPOWERS_VERSION --exclude SUPERPOWERS_LICENSE
cmp /home/krzysztof/.codex/plugins/cache/openai-curated-remote/superpowers/6.1.1/LICENSE .agents/skills/SUPERPOWERS_LICENSE
test "$(cat .agents/skills/SUPERPOWERS_VERSION)" = "6.1.1"
```

Expected: all commands exit with status `0` and produce no diff output.

- [x] **Step 4: Verify skill metadata**

Run a read-only validation that extracts each `name:` field from `.agents/skills/*/SKILL.md`, rejects missing or duplicate names, and confirms that each skill directory contains `SKILL.md`.

Expected: `14` unique skill names and no validation errors.

- [x] **Step 5: Commit the vendored snapshot**

```bash
git add .agents/skills
git commit -m "chore: vendor Superpowers skills for Codex"
```

### Task 2: Package Microsoft Learn MCP as a repository plugin

**Files:**
- Create: `plugins/mailmcp-microsoft-learn/.codex-plugin/plugin.json`
- Create: `plugins/mailmcp-microsoft-learn/.mcp.json`
- Create: `.agents/plugins/marketplace.json`

**Interfaces:**
- Consumes: Microsoft Learn public Streamable HTTP endpoint.
- Produces: installable plugin `mailmcp-microsoft-learn` and repo marketplace `mailmcp-repository`.

- [x] **Step 1: Create the MCP configuration**

Create `plugins/mailmcp-microsoft-learn/.mcp.json`:

```json
{
  "mcpServers": {
    "microsoft-learn": {
      "type": "http",
      "url": "https://learn.microsoft.com/api/mcp"
    }
  }
}
```

- [x] **Step 2: Create the plugin manifest**

Create `plugins/mailmcp-microsoft-learn/.codex-plugin/plugin.json`:

```json
{
  "name": "mailmcp-microsoft-learn",
  "version": "0.1.0",
  "description": "Search and fetch current Microsoft Learn documentation while developing MailMcp.",
  "author": {
    "name": "MailMcp contributors",
    "url": "https://github.com/Krzysztof318/MailMcp"
  },
  "homepage": "https://learn.microsoft.com/",
  "repository": "https://github.com/Krzysztof318/MailMcp",
  "keywords": [
    "documentation",
    "dotnet",
    "microsoft-learn",
    "mcp"
  ],
  "mcpServers": "./.mcp.json",
  "interface": {
    "displayName": "MailMcp Microsoft Learn",
    "shortDescription": "Search current Microsoft Learn documentation",
    "longDescription": "Use the public Microsoft Learn MCP server to search and fetch current official Microsoft documentation while developing MailMcp.",
    "developerName": "MailMcp contributors",
    "category": "Developer Tools",
    "capabilities": [
      "Read"
    ],
    "websiteURL": "https://learn.microsoft.com/",
    "privacyPolicyURL": "https://privacy.microsoft.com/privacystatement",
    "termsOfServiceURL": "https://learn.microsoft.com/legal/termsofuse",
    "defaultPrompt": [
      "Verify current Microsoft and .NET documentation before implementation."
    ]
  }
}
```

- [x] **Step 3: Expose the plugin in the repository marketplace**

Create `.agents/plugins/marketplace.json`:

```json
{
  "name": "mailmcp-repository",
  "interface": {
    "displayName": "MailMcp Repository"
  },
  "plugins": [
    {
      "name": "mailmcp-microsoft-learn",
      "source": {
        "source": "local",
        "path": "./plugins/mailmcp-microsoft-learn"
      },
      "policy": {
        "installation": "AVAILABLE",
        "authentication": "ON_INSTALL"
      },
      "category": "Developer Tools"
    }
  ]
}
```

- [x] **Step 4: Validate plugin structure and paths**

Run:

```bash
jq empty plugins/mailmcp-microsoft-learn/.codex-plugin/plugin.json
jq empty plugins/mailmcp-microsoft-learn/.mcp.json
jq empty .agents/plugins/marketplace.json
test "$(jq -r '.mcpServers' plugins/mailmcp-microsoft-learn/.codex-plugin/plugin.json)" = "./.mcp.json"
test "$(jq -r '.mcpServers["microsoft-learn"].url' plugins/mailmcp-microsoft-learn/.mcp.json)" = "https://learn.microsoft.com/api/mcp"
test -d "$(jq -r '.plugins[0].source.path' .agents/plugins/marketplace.json)"
```

Expected: all commands exit with status `0` and produce no error output.

- [x] **Step 5: Commit the plugin**

```bash
git add .agents/plugins/marketplace.json plugins/mailmcp-microsoft-learn
git commit -m "feat: add Microsoft Learn plugin for Codex"
```

### Task 3: Document activation, maintenance, and failure modes

**Files:**
- Create: `docs/operations/codex-cloud-tooling.md`

**Interfaces:**
- Consumes: repository skill snapshot, repository marketplace, and Microsoft Learn plugin from Tasks 1 and 2.
- Produces: operator-facing instructions for Cloud users and workspace administrators.

- [x] **Step 1: Write the operations guide**

Document these exact contracts:

- Superpowers skills load automatically from `.agents/skills/` in a new repository session.
- The Microsoft Learn plugin must be installed from the `MailMcp Repository` marketplace and a new session must be started afterward.
- Workspace sharing or role-based installation is required when the plugin should be available to every eligible workspace member.
- The public Microsoft Learn endpoint needs no secret, but workspace plugin/MCP policy and Cloud reachability still apply.
- Updates to Superpowers must replace the entire snapshot, update `SUPERPOWERS_VERSION`, preserve the license, and pass the snapshot comparison checks.

- [x] **Step 2: Validate documentation references**

Run:

```bash
rg -n "\.agents/skills|MailMcp Repository|https://learn.microsoft.com/api/mcp|SUPERPOWERS_VERSION" docs/operations/codex-cloud-tooling.md
```

Expected: each required operational contract appears in the guide.

- [x] **Step 3: Run final repository checks**

Run:

```bash
git diff --check
git status --short
git diff --stat main...HEAD
git diff main...HEAD -- . ':(exclude).agents/skills/**'
```

Expected: no whitespace errors, only task-related files, no secrets, and no production source changes.

- [ ] **Step 4: Commit the operations documentation**

```bash
git add docs/operations/codex-cloud-tooling.md docs/superpowers/plans/2026-07-22-codex-cloud-tooling.md
git commit -m "docs: explain Codex Cloud tooling setup"
```

- [ ] **Step 5: Push the branch**

```bash
git push -u origin agent/codex-cloud-tooling
```

Expected: the remote branch is created successfully. Direct push to `main` is intentionally excluded by repository policy.
