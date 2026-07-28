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
4. Apply the recurring-findings checklist below to the changed code.
5. Run `scripts/verify-fast.sh` when executable or test code changed since its last successful run. A green run that nothing has invalidated is already evidence; repeating it costs a Release build, the whole test suite, and two formatting passes to reprove the same thing. Report the run you are relying on. If it cannot run, state why.
6. Never invoke `dotnet format` directly to act on a finding. The fast loop repairs the changed files and reports what has no code fix; fix that and rerun the loop.

## Recurring findings

These categories account for most review findings raised against merged pull requests in this repository. Check each one that the change touches; a category the change does not reach needs no comment.

- **Bounds.** Every sequence the change reads from a remote server, a database, or a MIME tree has an explicit ceiling on count and size, and the limit is enforced before the untrusted input is expanded rather than after.
- **Reload and snapshots.** Configuration that can reload keeps the last valid value when a new one fails to bind or validate, takes one snapshot per attempt instead of re-reading a changing source mid-operation, discards a candidate superseded while validation ran, and cancels its validation during shutdown. A value read at startup is reconciled with one that changed before subscription.
- **Redaction.** Anything derived from an exception, a configuration value, a certificate, or a remote server response is redacted before it reaches a log, a span, or an exporter. Startup and shutdown paths redact too, including the ones that run before the container exists.
- **Concurrency.** A row a competing run may already have written is claimed only when its contents match what this run resolved; otherwise the change reports a conflict and re-resolves. Identity-bearing state is never adopted on the strength of its key alone.
- **Failure classification.** A transport failure is sorted into transient, terminal, cancelled, timed out, or authentication-failed, and an exhausted retry budget is translated into the domain outcome its caller acts on. An ambiguous server response is treated as terminal unless the protocol makes retrying provably safe.
- **Configuration validation.** Bound sections reject misspelled names rather than silently binding defaults, and every value a library constrains is validated against that library's documented range before the library sees it.
- **Test doubles.** No test consumes a real clock, a real delay, or wall-clock ordering. A fake preserves the ordering and identity guarantees of the real collaborator it replaces, including which request produced which response.
- **Telemetry.** A new meter, activity source, or exporter is actually subscribed by the host pipeline, and its resource attributes and endpoint match the pipeline the rest of the process uses.
- **Documentation drift.** Prose describing a validator, a guarantee, or an ownership rule states what the code now does. A narrower implementation than the documentation claims is a defect in the documentation.

## Reporting

Report findings first, ordered by severity. Every finding names the file and line, explains impact, and proposes the smallest correction.

End with:

```text
Verification: <commands and results, or not run with reason>
Residual risks: <specific risks or none>
```

If there are no findings, say so explicitly. Never imply a change passed checks that did not run.
