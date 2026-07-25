---
name: review-change
description: Use when reviewing a working-tree diff, pull-request patch, or completed implementation before final verification.
---

# Review Change

## Workflow

1. Resolve the repository root, comparison base, staged diff, and unstaged diff.
2. Read the applicable `AGENTS.md`, selected specification, implemented-behavior documentation, and relevant ADRs.
3. Review only changed behavior and its direct consequences. Check:
   - correctness, failure handling, cancellation, and security;
   - architecture boundaries and domain-correct naming;
   - privacy, safe logging, bounded work, and configuration validation;
   - IMAP content retrieval preserving `\Seen`;
   - unit-test, documentation, and third-party license impact;
   - unrelated edits, generated files, and secrets.
4. Run `eng/agent-workflow/verify-fast.sh` when executable or test code changed. If it cannot run, state why.

Report findings first, ordered by severity. Every finding names the file and line, explains impact, and proposes the smallest correction.

End with:

```text
Verification: <commands and results, or not run with reason>
Residual risks: <specific risks or none>
```

If there are no findings, say so explicitly. Never imply a change passed checks that did not run.
