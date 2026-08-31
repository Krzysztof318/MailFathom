---
name: review-change
description: Use when reviewing a working-tree diff, pull-request patch, or completed implementation before final verification.
license: Apache-2.0
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/Krzysztof318/MailFathom
---

# Review Change

## Workflow

1. Resolve the repository root, comparison base, staged diff, and unstaged diff.
2. Read the applicable `AGENTS.md`, the issue that governs the change, implemented-behavior documentation, and relevant ADRs.
3. Review only changed behavior and its direct consequences. Check:
   - correctness, failure handling, cancellation, and security;
   - architecture boundaries and domain-correct naming;
   - privacy, safe logging, bounded work, and configuration validation;
   - IMAP content retrieval preserving `\Seen`;
   - unit-test, documentation, and third-party license impact;
   - unrelated edits, generated files, and secrets.
4. Run `scripts/review-obligations.sh` and work through what it reports. It names what the change obliges the rest of the repository to do — the tests covering each changed source file, named for the service and sitting beside it for the client, the pages whose `describes:` marker covers each changed path, the registers whose trigger moved — which is the part of a review no diff contains, because there the defect *is* the absence of a second file. It reports and never gates, and it costs about thirteen seconds whatever the diff holds, so run it on every change rather than on the ones that look like they need it.
5. Apply the recurring-findings checklist below to the changed code.
6. Run `scripts/verify-fast.sh`. A green run that nothing has invalidated is already evidence, and the script knows it: it records a digest of what it verified and reports the earlier run rather than repeating a Release build, the whole test suite, and a formatting pass to reprove the same thing. So run it instead of reasoning about whether to — an unchanged tree costs under a second and answers with the run it is relying on. Report that answer. If it cannot run, state why.
7. Never invoke `dotnet format` directly to act on a finding. The fast loop repairs the changed files, and the Release build in front of it names by file and line whatever no rewrite fixes; fix that and rerun the loop.

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
- **Comments and documentation.** A comment the change added carries why the code is the way it is, and no line narrates the statement beneath it. XML documentation states a contract the signature does not, rather than restating the member or parameter name to satisfy `CS1591`. A `TODO` or `FIXME` names a concrete unresolved issue with enough context to act on it. Comments the change did not touch are left alone, and one the change made false is corrected in it.
- **Obligations elsewhere.** What `scripts/review-obligations.sh` reported is confirmed in the file it points at, never restated from the report. Confirmation is the behavior this change introduced or altered that no test now reaches, stated as the input and the wrong result that would go unnoticed; the sentence, table row, or example that stopped being true; the specific register row that is missing. A rename owes no test, a page whose marker covers a path may say nothing about the part that moved, and a register may already carry the row — so a row that survives none of that is dropped rather than reported. The same bar applies in reverse: a category the report is silent about is not evidence that nothing is owed, because the marker on a page or the name of a type is what the report reasons from.

## Reporting

Report findings first, ordered by severity. Every finding names the file and line, explains impact, and proposes the smallest correction.

End with:

```text
Verification: <commands and results, or not run with reason>
Residual risks: <specific risks or none>
```

If there are no findings, say so explicitly. Never imply a change passed checks that did not run.
