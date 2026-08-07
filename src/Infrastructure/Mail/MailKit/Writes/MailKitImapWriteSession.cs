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
/// The protocol choices are made per attempt from what the connection actually advertises rather than from anything
/// captured when the session opened, for the same reason the read session reads its capabilities that way: a recovered
/// connection can land on a server advertising something else.
/// </para>
/// <para>
/// MailKit's own <c>MoveTo</c> would carry a relocation on either kind of server, and it is deliberately used for the
/// native path alone. Its fallback issues a bare <c>EXPUNGE</c> when the server advertises no <c>UIDPLUS</c> — first
/// clearing <c>\Deleted</c> from every other message that carries it and then restoring it — which is a sequence that
/// destroys another client's pending deletion if it crashes in the middle, and destroys a message another client
/// flagged between the search and the expunge even if it does not. MailFathom refuses that case instead, and writes
/// the three commands out itself so each one is visible in the debug record.
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);

        return this.PerformAsync(
            MailboxMutation.Relocate,
            occurrenceId,
            (client, openFolder, scope, attemptToken) =>
                this.RelocateThroughBestAvailablePathAsync(
                    client,
                    openFolder,
                    occurrenceId,
                    destinationPath,
                    scope,
                    attemptToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(EmailOccurrenceId occurrenceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);

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

                await FlagDeletedAndExpungeAsync(openFolder, occurrenceId.Uid, scope, attemptToken);

                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetSeenAsync(
        EmailOccurrenceId occurrenceId,
        bool isSeen,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);

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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);

        return this.PerformAsync(
            MailboxMutation.Copy,
            occurrenceId,
            async (client, openFolder, scope, attemptToken) =>
            {
                var destination = await client.GetFolderAsync(destinationPath.Value, attemptToken);

                scope.CommandIssued("UID COPY");
                var copied = await openFolder.CopyToAsync(
                    [new UniqueId(occurrenceId.Uid.Value)],
                    destination,
                    attemptToken);

                return PlacementOf(copied);
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

    /// <summary>Moves the email with the server's own command where it has one, and with the three-command sequence where it does not.</summary>
    /// <remarks>
    /// The fallback is the main path rather than the exceptional one, because a server without RFC 6851 is ordinary.
    /// Both branches produce the same relocation from every layer above; only the debug record tells them apart.
    /// </remarks>
    private async Task<RemoteEmailPlacement> RelocateThroughBestAvailablePathAsync(
        IImapClient client,
        IMailFolder openFolder,
        EmailOccurrenceId occurrenceId,
        RemoteFolderPath destinationPath,
        MailboxMutationScope scope,
        CancellationToken cancellationToken)
    {
        var destination = await client.GetFolderAsync(destinationPath.Value, cancellationToken);
        var sourceUid = new UniqueId(occurrenceId.Uid.Value);

        if (client.Capabilities.HasFlag(ImapCapabilities.Move))
        {
            scope.ProtocolPathChosen(NativeProtocolPath);
            scope.CommandIssued("UID MOVE");

            var moved = await openFolder.MoveToAsync([sourceUid], destination, cancellationToken);

            return PlacementOf(moved);
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

        scope.CommandIssued("UID COPY");
        var copied = await openFolder.CopyToAsync([sourceUid], destination, cancellationToken);

        await FlagDeletedAndExpungeAsync(openFolder, occurrenceId.Uid, scope, cancellationToken);

        return PlacementOf(copied);
    }

    /// <summary>Marks one email deleted and removes exactly that email from the folder.</summary>
    /// <remarks>
    /// <para>
    /// A delete and the tail of a fallback relocation are the same two commands, so they share this path rather than
    /// each writing their own. Which operation they belong to is already the scope's to know, which is what keeps a
    /// relocation that failed at the expunge recorded as a failed relocation rather than as a failed delete.
    /// </para>
    /// <para>
    /// The expunge names the UID. RFC 3501's bare <c>EXPUNGE</c> removes every message in the folder that anyone has
    /// flagged <c>\Deleted</c> — including messages another client flagged and MailFathom has never seen — and that is
    /// not a side effect a mail tool may have, so the caller has already established that <c>UID EXPUNGE</c> exists.
    /// </para>
    /// </remarks>
    private static async Task FlagDeletedAndExpungeAsync(
        IMailFolder openFolder,
        ImapUid uid,
        MailboxMutationScope scope,
        CancellationToken cancellationToken)
    {
        UniqueId[] targetUid = [new UniqueId(uid.Value)];

        scope.CommandIssued("UID STORE +FLAGS (\\Deleted)");
        await openFolder.StoreAsync(
            targetUid,
            new StoreFlagsRequest(StoreAction.Add, MessageFlags.Deleted) { Silent = true },
            cancellationToken);

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
