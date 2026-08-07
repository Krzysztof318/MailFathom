// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Observability;
using MailKit;
using MailKit.Net.Imap;

namespace MailFathom.Infrastructure.Mail.MailKit.Writes;

/// <summary>Changes one folder of one account over the account's single write connection.</summary>
/// <remarks>
/// <para>
/// Every operation here issues commands that change remote state, which is what makes this type the exact inverse of
/// <see cref="MailKitImapMailboxSession" />: nothing it does is repeatable, so nothing it does is retried, and the
/// connection it runs on is one no read path can obtain.
/// </para>
/// <para>
/// Which command a *fresh* attempt uses is decided from what the connection actually advertises rather than from
/// anything captured when the session opened, for the same reason the read session reads its capabilities that way: a
/// recovered connection can land on a server advertising something else.
/// </para>
/// <para>
/// A *resumed* attempt is the exact inverse, and for the same reason. What a half-finished sequence still owes was
/// decided by the attempt that started it, so it is read from the durable record rather than asked of the connection
/// again — a fallback relocation resumed against a server that now advertises <c>MOVE</c> would otherwise be read as
/// already finished, leaving the email in both folders permanently.
/// </para>
/// <para>
/// MailKit's own <c>MoveTo</c> would carry a relocation on either kind of server, and it is deliberately used for the
/// native path alone. Its fallback issues a bare <c>EXPUNGE</c> when the server advertises no <c>UIDPLUS</c> — first
/// clearing <c>\Deleted</c> from every other message that carries it and then restoring it — which is a sequence that
/// destroys another client's pending deletion if it crashes in the middle, and destroys a message another client
/// flagged between the search and the expunge even if it does not. MailFathom refuses that case instead, and writes
/// the three commands out itself so each one is visible in the debug record.
/// </para>
/// <para>
/// Each operation announces the stage it has reached to the journal it is given, before the command that would change
/// the mailbox rather than after it, and reads that journal to know how much of the sequence a previous attempt already
/// carried. That is what makes the sequences resumable without the caller knowing what they are made of: how a sequence
/// is composed never leaves this adapter, and what a stopped one still owes is recorded rather than re-derived.
/// </para>
/// </remarks>
internal sealed class MailKitImapWriteSession : IMailboxWriteSession
{
    private const string NativeProtocolPath = "native";
    private const string FallbackProtocolPath = "fallback";
    private const string UidPlusCapabilityName = "UIDPLUS extension (RFC 4315)";

    private readonly MailboxWriteConnectionLease lease;
    private readonly MailFolderResolution folder;
    private readonly MailboxMutationTelemetry telemetry;

    internal MailKitImapWriteSession(
        MailboxWriteConnectionLease lease,
        MailFolderResolution folder,
        MailboxMutationTelemetry telemetry)
    {
        this.lease = lease;
        this.folder = folder;
        this.telemetry = telemetry;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => this.lease.DisposeAsync();

    /// <inheritdoc />
    public Task<RemoteEmailPlacement> RelocateAsync(
        EmailOccurrenceId occurrenceId,
        RemoteFolderPath destinationPath,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        ArgumentNullException.ThrowIfNull(journal);

        return this.PerformAsync(
            MailboxMutation.Relocate,
            occurrenceId,
            (client, openFolder, scope, attemptToken) =>
                this.RelocateThroughBestAvailablePathAsync(
                    client,
                    openFolder,
                    occurrenceId,
                    destinationPath,
                    journal,
                    scope,
                    attemptToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        EmailOccurrenceId occurrenceId,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        ArgumentNullException.ThrowIfNull(journal);

        await this.PerformAsync(
            MailboxMutation.Delete,
            occurrenceId,
            async (client, openFolder, scope, attemptToken) =>
            {
                RequireCapability(
                    client,
                    ImapCapabilities.UidPlus,
                    MailboxMutation.Delete,
                    UidPlusCapabilityName,
                    this.SessionAccountId,
                    this.folder.Alias);

                await RemoveSourceAsync(openFolder, occurrenceId.Uid, journal, scope, attemptToken);

                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetSeenAsync(
        EmailOccurrenceId occurrenceId,
        bool isSeen,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        ArgumentNullException.ThrowIfNull(journal);

        // The journal is taken and never advanced, because a `\Seen` store is idempotent for one UID: repeating it
        // reaches the same flag state, so there is no stage a resumed attempt would want to skip. What the record is
        // for here is provenance — the change comes back through synchronization looking like the owner marking mail
        // read in their own client — and that is written before this session is opened at all.
        await this.PerformAsync(
            MailboxMutation.SetSeen,
            occurrenceId,
            async (_, openFolder, scope, attemptToken) =>
            {
                var action = isSeen ? StoreAction.Add : StoreAction.Remove;

                scope.CommandIssued(isSeen ? "UID STORE +FLAGS (\\Seen)" : "UID STORE -FLAGS (\\Seen)");
                await openFolder.StoreAsync(
                    [new UniqueId(occurrenceId.Uid.Value)],
                    new StoreFlagsRequest(action, MessageFlags.Seen) { Silent = true },
                    attemptToken);

                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RemoteEmailPlacement> CopyAsync(
        EmailOccurrenceId occurrenceId,
        RemoteFolderPath destinationPath,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        ArgumentNullException.ThrowIfNull(journal);

        return this.PerformAsync(
            MailboxMutation.Copy,
            occurrenceId,
            async (client, openFolder, scope, attemptToken) =>
            {
                // A copy the record already carries a confirmed placement for is finished, and its one command is the
                // one that must never be issued twice. Answering from the record is the whole reason the record is
                // written first. The check sits inside the attempt rather than in front of it so a resumed copy is
                // still refused an occurrence this session's selection does not cover, exactly as a fresh one is.
                if (HasConfirmedPlacement(journal))
                {
                    return journal.Placement;
                }

                var destination = await client.GetFolderAsync(destinationPath.Value, attemptToken);

                // A copy owes no source removal: leaving the source exactly where it is *is* the operation.
                await journal.PlacementIssuedAsync(requiresSourceRemoval: false, attemptToken);

                scope.CommandIssued("UID COPY");
                var copied = await openFolder.CopyToAsync(
                    [new UniqueId(occurrenceId.Uid.Value)],
                    destination,
                    attemptToken);

                var placement = PlacementOf(copied);
                await journal.PlacementConfirmedAsync(placement, attemptToken);

                return placement;
            },
            cancellationToken);
    }

    private MailAccountId SessionAccountId => this.lease.AccountId;

    /// <summary>Reads where a <c>COPYUID</c> response says the destination folder put the email.</summary>
    /// <remarks>
    /// <para>
    /// Both halves of the identity come out of the response rather than out of the destination folder object. RFC 4315
    /// puts the UIDVALIDITY in the <c>COPYUID</c> response beside the UID it belongs to, and the folder this session
    /// resolved by path was never selected — an unopened <see cref="IMailFolder" /> reports zero, which is not a
    /// UIDVALIDITY at all. Reading it there would turn a relocation that had already moved the message into a failure
    /// raised while describing it.
    /// </para>
    /// <para>
    /// MailKit reports an empty map both when the server advertises no <c>UIDPLUS</c> and when it advertises it and
    /// answered without the response anyway, and the two mean the same thing here: the server completed the change and
    /// did not say where. Searching the destination afterwards would replace a fact with a guess, so the absence is
    /// reported as itself — and so is a response that named a UID without a usable validity, because half an identity
    /// is not one.
    /// </para>
    /// </remarks>
    private static RemoteEmailPlacement PlacementOf(UniqueIdMap copied) =>
        copied.Destination is [{ Validity: > 0U } placedUid, ..]
            ? RemoteEmailPlacement.Reported(
                ImapUidValidity.Create(placedUid.Validity),
                ImapUid.Create(placedUid.Id))
            : RemoteEmailPlacement.NotReported();

    private static void RequireCapability(
        IImapClient client,
        ImapCapabilities capability,
        MailboxMutation mutation,
        string capabilityName,
        MailAccountId accountId,
        MailFolderAlias folderAlias)
    {
        if (!client.Capabilities.HasFlag(capability))
        {
            throw new MailboxMutationUnsupportedException(accountId, folderAlias, mutation, capabilityName);
        }
    }

    /// <summary>Reports whether the record already says the email reached its destination folder.</summary>
    private static bool HasConfirmedPlacement(IMailboxMutationJournal journal) =>
        journal.Stage is MailboxMutationStage.PlacementConfirmed or MailboxMutationStage.SourceFlaggedDeleted;

    /// <summary>Moves the email with the server's own command where it has one, and with the three-command sequence where it does not.</summary>
    /// <remarks>
    /// <para>
    /// The fallback is the main path rather than the exceptional one, because a server without RFC 6851 is ordinary.
    /// Both branches produce the same relocation from every layer above; only the debug record tells them apart.
    /// </para>
    /// <para>
    /// A resumed relocation whose placement is already confirmed has the copy behind it, and what remains is read from
    /// the record rather than worked out again. <c>MOVE</c> removes the source as part of the same command and the
    /// fallback leaves it in the folder, so the same stage means opposite things depending on which ran — and the
    /// connection a retry lands on is not required to be the one that answered the first. Asking it would let a
    /// fallback relocation resumed against a server now advertising <c>MOVE</c> be read as finished, leaving the email
    /// in both folders with nothing left to surface it.
    /// </para>
    /// </remarks>
    private async Task<RemoteEmailPlacement> RelocateThroughBestAvailablePathAsync(
        IImapClient client,
        IMailFolder openFolder,
        EmailOccurrenceId occurrenceId,
        RemoteFolderPath destinationPath,
        IMailboxMutationJournal journal,
        MailboxMutationScope scope,
        CancellationToken cancellationToken)
    {
        if (HasConfirmedPlacement(journal))
        {
            // The record says which sequence placed the email, so nothing here consults the connection about it.
            if (!journal.RequiresSourceRemoval)
            {
                scope.ProtocolPathChosen(NativeProtocolPath);

                return journal.Placement;
            }

            scope.ProtocolPathChosen(FallbackProtocolPath);
            RequireCapability(
                client,
                ImapCapabilities.UidPlus,
                MailboxMutation.Relocate,
                UidPlusCapabilityName,
                this.SessionAccountId,
                this.folder.Alias);

            await RemoveSourceAsync(openFolder, occurrenceId.Uid, journal, scope, cancellationToken);

            return journal.Placement;
        }

        var destination = await client.GetFolderAsync(destinationPath.Value, cancellationToken);
        var sourceUid = new UniqueId(occurrenceId.Uid.Value);

        if (client.Capabilities.HasFlag(ImapCapabilities.Move))
        {
            scope.ProtocolPathChosen(NativeProtocolPath);

            await journal.PlacementIssuedAsync(requiresSourceRemoval: false, cancellationToken);

            scope.CommandIssued("UID MOVE");
            var moved = await openFolder.MoveToAsync([sourceUid], destination, cancellationToken);

            var nativePlacement = PlacementOf(moved);
            await journal.PlacementConfirmedAsync(nativePlacement, cancellationToken);

            return nativePlacement;
        }

        scope.ProtocolPathChosen(FallbackProtocolPath);

        // The expunge is what the fallback cannot do safely without UIDPLUS, and the copy is the step that would
        // already have happened by the time that was discovered. Checking first is what keeps a refused relocation
        // from leaving a duplicate behind in the destination folder.
        RequireCapability(
            client,
            ImapCapabilities.UidPlus,
            MailboxMutation.Relocate,
            UidPlusCapabilityName,
            this.SessionAccountId,
            this.folder.Alias);

        await journal.PlacementIssuedAsync(requiresSourceRemoval: true, cancellationToken);

        scope.CommandIssued("UID COPY");
        var copied = await openFolder.CopyToAsync([sourceUid], destination, cancellationToken);

        var placement = PlacementOf(copied);
        await journal.PlacementConfirmedAsync(placement, cancellationToken);

        await RemoveSourceAsync(openFolder, occurrenceId.Uid, journal, scope, cancellationToken);

        return placement;
    }

    /// <summary>Marks one email deleted and removes exactly that email from the folder.</summary>
    /// <remarks>
    /// <para>
    /// A delete and the tail of a fallback relocation are the same two commands, so they share this path rather than
    /// each writing their own. Which operation they belong to is already the scope's to know, which is what keeps a
    /// relocation that failed at the expunge recorded as a failed relocation rather than as a failed delete.
    /// </para>
    /// <para>
    /// The flag is announced to the journal between the two commands, so a resumed attempt that already reached it
    /// reissues the expunge alone. Both commands are idempotent for one UID, so repeating either would cost nothing;
    /// the stage exists because an operator reading a stuck mutation is owed the sequence it actually got to.
    /// </para>
    /// <para>
    /// The expunge names the UID. RFC 3501's bare <c>EXPUNGE</c> removes every message in the folder that anyone has
    /// flagged <c>\Deleted</c> — including messages another client flagged and MailFathom has never seen — and that is
    /// not a side effect a mail tool may have, so the caller has already established that <c>UID EXPUNGE</c> exists.
    /// </para>
    /// </remarks>
    private static async Task RemoveSourceAsync(
        IMailFolder openFolder,
        ImapUid uid,
        IMailboxMutationJournal journal,
        MailboxMutationScope scope,
        CancellationToken cancellationToken)
    {
        UniqueId[] targetUid = [new UniqueId(uid.Value)];

        if (journal.Stage is not MailboxMutationStage.SourceFlaggedDeleted)
        {
            scope.CommandIssued("UID STORE +FLAGS (\\Deleted)");
            await openFolder.StoreAsync(
                targetUid,
                new StoreFlagsRequest(StoreAction.Add, MessageFlags.Deleted) { Silent = true },
                cancellationToken);

            await journal.SourceFlaggedDeletedAsync(cancellationToken);
        }

        scope.CommandIssued("UID EXPUNGE");
        await openFolder.ExpungeAsync(targetUid, cancellationToken);
    }

    /// <summary>Runs one mutation under its own telemetry scope, against the occurrence this session is allowed to change.</summary>
    private async Task<TResult> PerformAsync<TResult>(
        MailboxMutation mutation,
        EmailOccurrenceId occurrenceId,
        Func<IImapClient, IMailFolder, MailboxMutationScope, CancellationToken, Task<TResult>> change,
        CancellationToken cancellationToken)
    {
        using var scope = this.telemetry.Begin(mutation, this.SessionAccountId, this.folder.Alias);

        var result = await this.lease.Connection.ExecuteMutationAsync(
            (client, openFolder, attemptToken) =>
            {
                this.RequireOccurrenceBelongsToSession(occurrenceId, openFolder);

                return change(client, openFolder, scope, attemptToken);
            },
            cancellationToken);

        scope.Completed();

        return result;
    }

    /// <summary>Refuses an occurrence this session's selection does not cover.</summary>
    /// <remarks>
    /// The UIDVALIDITY is compared against the folder as it is selected right now rather than as it was when the
    /// session opened, so a folder recreated under a recovered connection cannot have a mutation applied to it using
    /// UIDs that named different emails.
    /// </remarks>
    private void RequireOccurrenceBelongsToSession(EmailOccurrenceId occurrenceId, IMailFolder openFolder)
    {
        if (occurrenceId.AccountId != this.SessionAccountId ||
            occurrenceId.FolderResolutionId != this.folder.Id ||
            occurrenceId.UidValidity.Value != openFolder.UidValidity)
        {
            throw new ArgumentException(
                "The email occurrence does not belong to the open mailbox write session.",
                nameof(occurrenceId));
        }
    }
}
