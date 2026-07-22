# Codex Cloud .NET Tooling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Configure Codex Cloud repository setup so agents have .NET SDK 10, Aspire CLI, and the EF Core `dotnet ef` CLI tool available.

**Architecture:** Add a repository-owned `.codex/setup.sh` bootstrap script that installs user-local tooling without system package manager access. Pin CLI SDK resolution through `global.json` and document the operational contract in the existing Codex Cloud tooling guide.

**Tech Stack:** Bash, Microsoft `dotnet-install.sh`, .NET SDK 10, Aspire CLI, `dotnet-ef`, Markdown.

## Global Constraints

- Do not commit directly on `main` or `master`.
- Install the SDK under `${DOTNET_INSTALL_DIR:-$HOME/.dotnet}` and add both the SDK directory and `${DOTNET_CLI_HOME:-$HOME}/.dotnet/tools` to `PATH` for the setup session.
- Use the official .NET install script with channel `10.0`.
- Install or update global .NET tools idempotently at pinned versions `dotnet-ef` `10.0.10` and `Aspire.Cli` `13.4.6`.
- Do not add application package dependencies.
- Document behavior and troubleshooting under `docs/operations/`.

---

### Task 1: Add Codex Cloud setup script

**Files:**
- Create: `.codex/setup.sh`

**Interfaces:**
- Consumes: internet access to `https://dot.net/v1/dotnet-install.sh` and NuGet global tool feeds.
- Produces: an executable setup script callable by Codex Cloud.

- [x] **Step 1: Create the setup script**

Create `.codex/setup.sh` with strict Bash settings, local .NET installation, idempotent global tool install/update for `dotnet-ef`, and Aspire CLI installation through the `Aspire.Cli` global .NET tool.

- [x] **Step 2: Make it executable**

Run: `chmod +x .codex/setup.sh`
Expected: file mode includes executable bits.

### Task 2: Pin .NET SDK resolution

**Files:**
- Create: `global.json`

**Interfaces:**
- Consumes: .NET SDKs installed by the setup script.
- Produces: deterministic SDK selection for repository commands.

- [x] **Step 1: Create `global.json`**

Pin SDK version `10.0.100` with `rollForward` set to `latestFeature`.

### Task 3: Document operation and verification

**Files:**
- Modify: `docs/operations/codex-cloud-tooling.md`

**Interfaces:**
- Consumes: `.codex/setup.sh` and `global.json` behavior.
- Produces: durable operator documentation.

- [x] **Step 1: Update operations guide**

Document what the setup script installs, expected verification commands, and troubleshooting for network/package feed failures.
