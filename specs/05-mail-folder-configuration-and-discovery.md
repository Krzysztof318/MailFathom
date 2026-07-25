# Mail Folder Configuration and Discovery

**Roadmap group:** A — configuration, transport security, resilience
**Draft delivery stage:** 3
**Depends on:** 01, 04
**Estimated change size:** ~700 lines including tests and documentation

## Goal

Perform the folder-configuration design review that draft section 11.2 requires before any runtime folder mapping is implemented, and then implement it: operators and logs work with stable friendly folder names while synchronization stores the resolved remote folder identity that IMAP safety depends on.

## Current state

`MailFolderName` wraps whatever string the operator typed, and `MailFolderEntityResolver` resolves that string straight to a persisted folder row. There is no folder discovery, no separation between the name an operator chose and the identifier the server uses, and no handling for a server that renames or re-creates a folder.

## Approved scope

Two distinct concepts replace today's single string. `MailFolderAlias` is the stable operator-facing name used in configuration, logs, and future MCP filters; it is owned by MailMcp and never changes because the server changed. `RemoteFolderPath` is the server-advertised path together with its hierarchy delimiter; it is owned by the server and may change. Persistence keys folders by account plus alias and stores the currently resolved remote path alongside it.

A folder discovery operation lists remote folders through the mailbox session port and returns advertised paths together with their special-use attributes where the server reports them. Configuration maps each alias either to an explicit remote path or to a special-use role such as the inbox, which lets an account work without the operator knowing the server's naming. An alias that resolves to no remote folder is an explicit, reported failure for that folder only; it does not fail the account.

When the resolved remote path for an alias changes, the change is recorded and the folder's synchronization state is treated as suspect: the existing UIDVALIDITY check already governs correctness, so the change is logged as an auditable event and reconciliation proceeds under the existing rules rather than through a new invalidation mechanism.

## Safety and privacy

Folder names can themselves be personal or organizational information, so discovery results are stored and logged as ordinary sensitive metadata: the alias appears in logs, the remote path appears only in the audit event that records a mapping change. Discovery is read-only; the session port exposes no create, rename, or delete operation, and the specification requires that the listing operation cannot alter any message flag.

## Testing

`Domain.UnitTests` cover alias normalization, remote path and delimiter parsing, and rejection of blank or ambiguous mappings. `Application.UnitTests` cover explicit-path resolution, special-use resolution, the unresolved-alias failure being isolated to one folder, and the recorded mapping-change event. `Infrastructure.UnitTests` cover the MailKit listing adapter against the narrow client port, including a server that reports no special-use attributes.

## Out of scope

Per-folder synchronization policies such as choosing push versus polling, which specifications 11 and 12 own. Folder creation, subscription management, and hierarchy presentation in MCP results.

## Definition of done

- Configuration expresses folders as aliases; no configuration path requires the operator to write a server-specific identifier such as a provider-generated folder name.
- An account whose server uses a non-English inbox name synchronizes without configuration changes when the alias maps to the inbox special-use role.
- A mapping change emits an auditable event and does not silently repoint synchronization state.
- `docs/features/imap-synchronization.md` documents aliases, discovery, special-use mapping, and the mapping-change behavior.
- `dotnet msbuild eng/CodeCoverage.proj -t:Collect` passes the 85% whole-scope gate.
