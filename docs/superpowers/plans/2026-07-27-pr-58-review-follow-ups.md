# PR 58 Review Follow-Ups Task Brief

**Branch:** `agent/mail-transport-security-policy` — already pushed, with open **draft** pull request #58 (implements `specs/01-mail-transport-security-policy.md`, closes issue #35).

Read the PR body and its review threads before starting; they carry the reasoning behind the current shape.

## Hard constraints

- No new branch and no new pull request. Everything lands on `agent/mail-transport-security-policy` and in PR #58. Push once, at the end.
- PR #58 stays a draft. Do not mark it ready for review.
- No `Co-authored-by:` or any other co-author trailer. Never commit on `main`.
- Read root `AGENTS.md` plus the `AGENTS.md` files under `src/`, `src/Infrastructure/`, `tests/`, and `docs/` before editing. They override default habits.
- Do not edit anything under `specs/`. A specification is a contract, and its "Current state" section records pre-change naming on purpose.
- Files under `docs/superpowers/` are history. Leave their existing text alone; you may append to the "Revisions accepted during implementation" section of `docs/superpowers/plans/2026-07-26-mail-transport-security-policy.md`.
- Owner decision on record: **do not** add a `Host.UnitTests` project and do not move `MailSynchronizationOptions` out of `Host`. That question is settled; leave the Host glue untested as it is today.

## Task 1 — keep the machine-readable error identity

`src/Infrastructure/Mail/MailAccountTransportSecurityOptions.cs` defines
`MailAccountTransportSecurityConfigurationError(string PropertyName, string Description)`.

The record discards the machine-readable identity the domain already computed, which conflicts with the `src/AGENTS.md` rule requiring stable machine-readable error codes alongside safe human-readable messages.

- Carry the originating `MailTransportSecurityViolation` on the record as a **nullable** property. It is null for the unsupported-SASL-name error, which is a parse failure rather than a domain violation.
- Update the producer (`FindConfigurationErrors`) and the consumer in `src/Host/Configuration/MailSynchronizationOptions.cs`.
- Extend `tests/Infrastructure.UnitTests/MailAccountTransportSecurityOptionsTests.cs` so at least one test asserts the violation survives the projection and one asserts the parse failure carries none.
- Do **not** introduce a generic or shared configuration-error abstraction. There is one producer and one consumer; the repository rule against premature abstraction applies.

## Task 2 — stop overusing the `MailKit` prefix on provider-neutral types

The prefix belongs only on types that actually traffic in MailKit types. Verify each case in the code before renaming rather than trusting this list.

**Rename and relocate** (they contain no MailKit type at all):

- `MailKitImapAccountSettings` → `ImapAccountSettings`
- `IMailKitImapAccountSettingsProvider` → `IImapAccountSettingsProvider`
- Move both out of `src/Infrastructure/Mail/MailKit/` into `src/Infrastructure/Mail/`, in a file named after the primary type.
- Move `MailAuthenticationMechanismUnavailableException` from `src/Infrastructure/Mail/MailKit/` to `src/Infrastructure/Mail/`. Its name is already provider-neutral; only the folder is wrong.

**Keep the prefix** (each one takes or returns a MailKit type, so the name is honest):

- `IMailKitImapClient`, `MailKitImapClientAdapter` — `ConnectAsync` takes `SecureSocketOptions`.
- `MailKitImapMailboxSession`, `MailKitImapMailboxSessionFactory` — the MailKit-backed implementations.
- `MailKitTransportSecurityMapping` — maps the domain policy onto `SecureSocketOptions`.
- The test files named after those types.

**Then sweep the whole repository**, including files untouched by this pull request: `src/`, `tests/`, `shared/`, `deploy/`, and `docs/`. Look for any other abstraction, port, interface, data record, DI registration, or documentation reference that carries a provider name it does not depend on. Apply the same test each time: if replacing MailKit with another IMAP library would not change a single member of the type, the name must not say MailKit. Update `docs/` prose that names the renamed types.

Update call sites in `src/Host/Program.cs`, `src/Host/Configuration/MailSynchronizationOptions.cs`, `src/Infrastructure/ServiceCollectionExtensions.cs`, and the affected tests.

## Completion gates

1. `$check-docs-licenses` — mandatory, including when the licensing verdict is `n/a`.
2. `$review-change` over the working-tree diff.
3. Stage the task files, then run `scripts/verify-full.sh`. It must exit 0, and `dotnet msbuild .config/CodeCoverage.proj -t:Collect` must pass the 85% whole-scope gate. Fix and rerun the whole script on failure; partial results do not count.
4. Commit with a focused message, push the branch once.
5. Append a short section to the PR #58 body describing both changes. `gh pr edit` fails against this repository with a Projects-classic GraphQL error and silently drops the edit, so patch through the REST API:

   ```bash
   gh api repos/Krzysztof318/MailMcp/pulls/58 --jq .body > body.md
   # append the new section to body.md
   gh api repos/Krzysztof318/MailMcp/pulls/58 -X PATCH -f body="$(cat body.md)"
   ```

6. Confirm `Closes #35` is still the first line of the published body and that the PR is still a draft.

Report what changed, what the verification produced, and anything you deliberately left alone.
