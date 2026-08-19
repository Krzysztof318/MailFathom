// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.Observability;
using MailKit;
using MailKit.Net.Imap;
using MimeKit;

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
    private const string FileOperationName = "file-outgoing-copy";
    private const string WithdrawOperationName = "withdraw-outgoing-copy";
    private const string PermanentKeywordsCapabilityName = "persistent keywords (RFC 9051 PERMANENTFLAGS)";

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
                    MailboxMutation.Delete.Name,
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

                var destination = await this.GetDestinationFolderAsync(
                    client,
                    destinationPath,
                    MailboxMutation.Copy,
                    attemptToken);

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

    /// <inheritdoc />
    public async Task SetFlaggedAsync(
        EmailOccurrenceId occurrenceId,
        bool isFlagged,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        ArgumentNullException.ThrowIfNull(journal);

        // The journal is taken and never advanced, for the reason a `\Seen` store does not advance it either: the store
        // is idempotent for one UID, so there is no stage a resumed attempt would want to skip.
        await this.PerformAsync(
            MailboxMutation.SetFlagged,
            occurrenceId,
            async (_, openFolder, scope, attemptToken) =>
            {
                var action = isFlagged ? StoreAction.Add : StoreAction.Remove;

                scope.CommandIssued(isFlagged ? "UID STORE +FLAGS (\\Flagged)" : "UID STORE -FLAGS (\\Flagged)");
                await openFolder.StoreAsync(
                    [new UniqueId(occurrenceId.Uid.Value)],
                    new StoreFlagsRequest(action, MessageFlags.Flagged) { Silent = true },
                    attemptToken);

                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddKeywordsAsync(
        EmailOccurrenceId occurrenceId,
        AuthoredMailKeywords keywords,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        ArgumentNullException.ThrowIfNull(keywords);
        ArgumentNullException.ThrowIfNull(journal);

        await this.PerformAsync(
            MailboxMutation.AddKeywords,
            occurrenceId,
            async (_, openFolder, scope, attemptToken) =>
            {
                this.RequireFolderKeeps(openFolder, keywords.Values, MailboxMutation.AddKeywords);

                await StoreKeywordsAsync(
                    openFolder,
                    occurrenceId.Uid,
                    StoreAction.Add,
                    keywords.Values,
                    scope,
                    attemptToken);

                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveKeywordsAsync(
        EmailOccurrenceId occurrenceId,
        AuthoredMailKeywords keywords,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        ArgumentNullException.ThrowIfNull(keywords);
        ArgumentNullException.ThrowIfNull(journal);

        await this.PerformAsync(
            MailboxMutation.RemoveKeywords,
            occurrenceId,
            async (_, openFolder, scope, attemptToken) =>
            {
                await StoreKeywordsAsync(
                    openFolder,
                    occurrenceId.Uid,
                    StoreAction.Remove,
                    keywords.Values,
                    scope,
                    attemptToken);

                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetKeywordsAsync(
        EmailOccurrenceId occurrenceId,
        AuthoredMailKeywords keywords,
        IMailboxMutationJournal journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        ArgumentNullException.ThrowIfNull(keywords);
        ArgumentNullException.ThrowIfNull(journal);

        await this.PerformAsync(
            MailboxMutation.SetKeywords,
            occurrenceId,
            async (_, openFolder, scope, attemptToken) =>
            {
                this.RequireFolderKeeps(openFolder, keywords.Values, MailboxMutation.SetKeywords);

                var carried = await ReadKeywordsAsync(openFolder, occurrenceId.Uid, scope, attemptToken);
                var surplus = carried
                    .Where(keyword => !keywords.Values.Contains(keyword, StringComparer.OrdinalIgnoreCase))
                    .ToArray();

                // The surplus goes first so that the state between the two commands is a subset of both the old set and
                // the new one: a client reading the message mid-sequence never sees a keyword it will not end up
                // carrying. Adding first would show the union instead. It is also the order two rules asking for the
                // same pair are applied in, so one mutation composes the way two do.
                await StoreKeywordsAsync(
                    openFolder,
                    occurrenceId.Uid,
                    StoreAction.Remove,
                    surplus,
                    scope,
                    attemptToken);
                await StoreKeywordsAsync(
                    openFolder,
                    occurrenceId.Uid,
                    StoreAction.Add,
                    keywords.Values,
                    scope,
                    attemptToken);

                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AppendedMailCopy> AppendAsync(
        ReadOnlyMemory<byte> rawMime,
        AppendedMailFlags flags,
        DateTimeOffset internalDate,
        CancellationToken cancellationToken)
    {
        if (rawMime.IsEmpty)
        {
            throw new ArgumentException("A message is appended with the bytes it was composed as.", nameof(rawMime));
        }

        using var scope = this.telemetry.BeginFiling(FileOperationName, this.SessionAccountId, this.folder.Alias);

        var copy = await this.lease.Connection.ExecuteMutationAsync(
            async (_, openFolder, attemptToken) =>
            {
                // Parsed rather than recomposed: the bytes are the ones the submission carried, and MimeKit writes the
                // headers and the body back as it read them. What the parse is for is the identity the message already
                // states, which is what recognizes the copy again on a server that names no placement.
                using var storedMime = RawMimeStream.Open(rawMime);
                using var message = await MimeMessage.LoadAsync(storedMime, attemptToken);

                scope.CommandIssued("APPEND");
                var appendedUid = await openFolder.AppendAsync(
                    new AppendRequest(message, MessageFlagsOf(flags), internalDate),
                    attemptToken);

                return new AppendedMailCopy(
                    PlacementOfAppend(openFolder, appendedUid),
                    NormalizedMessageIdOf(message));
            },
            cancellationToken);

        scope.Completed();

        return copy;
    }

    /// <inheritdoc />
    public async Task WithdrawAppendedAsync(
        ImapUidValidity uidValidity,
        ImapUid uid,
        CancellationToken cancellationToken)
    {
        using var scope = this.telemetry.BeginFiling(WithdrawOperationName, this.SessionAccountId, this.folder.Alias);

        await this.lease.Connection.ExecuteMutationAsync(
            async (client, openFolder, attemptToken) =>
            {
                // The folder is compared as it is selected right now rather than as it was when the copy was appended,
                // so a folder recreated in between has nothing removed from it: every UID in it names a different
                // message than the one this withdrawal was recorded against.
                if (openFolder.UidValidity != uidValidity.Value)
                {
                    throw new MailboxFolderRecreatedException(
                        this.SessionAccountId,
                        this.folder.Alias,
                        uidValidity,
                        ImapUidValidity.Create(openFolder.UidValidity));
                }

                // Named as the withdrawal it is rather than as a delete: the copy being removed is one MailFathom
                // filed, which the mutation boundary deliberately holds outside the closed set, so a refusal reported
                // as a delete would name a mutation nobody asked for.
                RequireCapability(
                    client,
                    ImapCapabilities.UidPlus,
                    WithdrawOperationName,
                    UidPlusCapabilityName,
                    this.SessionAccountId,
                    this.folder.Alias);

                UniqueId[] targetUid = [new UniqueId(uid.Value)];

                scope.CommandIssued("UID STORE +FLAGS (\\Deleted)");
                await openFolder.StoreAsync(
                    targetUid,
                    new StoreFlagsRequest(StoreAction.Add, MessageFlags.Deleted) { Silent = true },
                    attemptToken);

                scope.CommandIssued("UID EXPUNGE");
                await openFolder.ExpungeAsync(targetUid, attemptToken);

                return true;
            },
            cancellationToken);

        scope.Completed();
    }

    private MailAccountId SessionAccountId => this.lease.AccountId;

    /// <summary>Turns the two flags a filed copy may carry into the flag set the protocol takes.</summary>
    private static MessageFlags MessageFlagsOf(AppendedMailFlags flags)
    {
        var messageFlags = MessageFlags.None;

        if (flags.IsDraft)
        {
            messageFlags |= MessageFlags.Draft;
        }

        if (flags.IsSeen)
        {
            messageFlags |= MessageFlags.Seen;
        }

        return messageFlags;
    }

    /// <summary>Reads where an <c>APPENDUID</c> response says the folder put the copy.</summary>
    /// <remarks>
    /// The UID comes from the response and the UIDVALIDITY from the folder this session has selected, which is the same
    /// folder the append went into and is open — so unlike the copy destination this one reports a real validity. A
    /// server advertising no <c>UIDPLUS</c> answers with no UID at all, and that absence is reported as itself rather
    /// than turned into a search of the folder for something that looks like the message.
    /// </remarks>
    private static RemoteEmailPlacement PlacementOfAppend(IMailFolder openFolder, UniqueId? appendedUid) =>
        appendedUid is { Id: > 0U } placedUid && openFolder.UidValidity > 0U
            ? RemoteEmailPlacement.Reported(
                ImapUidValidity.Create(openFolder.UidValidity),
                ImapUid.Create(placedUid.Id))
            : RemoteEmailPlacement.NotReported();

    /// <summary>Reads the identity the appended message states, in the form a mail server reports it back.</summary>
    /// <remarks>
    /// MimeKit strips the angle brackets that delimit the header, which is the same form synchronization stores for
    /// arriving mail, so the two compare directly. A message stating none is recorded as stating none rather than as
    /// an empty string.
    /// </remarks>
    private static string? NormalizedMessageIdOf(MimeMessage message) =>
        string.IsNullOrWhiteSpace(message.MessageId) ? null : message.MessageId.Trim();


    /// <summary>Issues one keyword <c>STORE</c>, and issues nothing at all when the set it was given is empty.</summary>
    /// <remarks>
    /// An empty set reaches here from a replacement whose message carried nothing surplus, and from one that named no
    /// keyword at all. MailKit would send <c>STORE +FLAGS ()</c>, which is not a command RFC 9051 has, so the round trip
    /// is skipped rather than sent and refused.
    /// </remarks>
    private static async Task StoreKeywordsAsync(
        IMailFolder openFolder,
        ImapUid uid,
        StoreAction action,
        IReadOnlyList<string> keywords,
        MailboxMutationScope scope,
        CancellationToken cancellationToken)
    {
        if (keywords.Count == 0)
        {
            return;
        }

        scope.CommandIssued(action == StoreAction.Add ? "UID STORE +FLAGS (keywords)" : "UID STORE -FLAGS (keywords)");
        await openFolder.StoreAsync(
            [new UniqueId(uid.Value)],
            new StoreFlagsRequest(action, keywords) { Silent = true },
            cancellationToken);
    }

    /// <summary>Reads the keywords one message carries now, which is what a replacement has to know to be one.</summary>
    /// <remarks>
    /// <para>
    /// A replacement is issued as a removal of what is surplus followed by an addition of what was named, rather than as
    /// the <c>STORE FLAGS</c> that would say it in one command. That command replaces a message's whole flag set, so it
    /// would clear <c>\Seen</c>, <c>\Flagged</c>, <c>\Answered</c>, and <c>\Draft</c> while writing a label — and
    /// preserving them would mean reading them here and writing them back, which is the same round trip plus the chance
    /// of losing a flag another client set in between.
    /// </para>
    /// <para>
    /// <c>FETCH FLAGS</c> does not set <c>\Seen</c>, so reading here leaves the invariant this system is built around
    /// exactly where it was. Only a body fetch without <c>PEEK</c> would, and that is not what this asks for.
    /// </para>
    /// <para>
    /// A message the folder no longer holds answers with nothing, and nothing is what the replacement then removes. The
    /// <c>STORE</c> that follows is left to report the missing message, so this reading never becomes a second place
    /// that decides what a vanished occurrence means.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<string>> ReadKeywordsAsync(
        IMailFolder openFolder,
        ImapUid uid,
        MailboxMutationScope scope,
        CancellationToken cancellationToken)
    {
        scope.CommandIssued("UID FETCH (FLAGS)");
        var summaries = await openFolder.FetchAsync(
            [new UniqueId(uid.Value)],
            MessageSummaryItems.Flags,
            cancellationToken);

        return summaries is [{ Keywords: { } carried }, ..] ? [.. carried] : [];
    }

    /// <summary>Refuses a keyword write into a folder that would not keep the keyword between sessions.</summary>
    /// <remarks>
    /// <para>
    /// RFC 9051 has a folder answer <c>PERMANENTFLAGS</c> with <c>\*</c> when it accepts keywords it has not seen before,
    /// and list the ones it does keep when it does not. A folder saying neither will take the <c>STORE</c> and report
    /// success, and the keyword will be gone the next time anybody selects the folder — which reaches an operator as a
    /// rule that runs, says it worked, and changes nothing they can find.
    /// </para>
    /// <para>
    /// The refusal names the capability and never the keyword, for the reason every exception message here names an
    /// alias rather than a path: what an operator needs is which account and folder cannot keep labels, and the keyword
    /// they wrote is already in front of them in the file they wrote it in.
    /// </para>
    /// </remarks>
    private void RequireFolderKeeps(
        IMailFolder openFolder,
        IReadOnlyList<string> keywords,
        MailboxMutation mutation)
    {
        if (keywords.Count == 0 || openFolder.PermanentFlags.HasFlag(MessageFlags.UserDefined))
        {
            return;
        }

        if (keywords.All(keyword => openFolder.PermanentKeywords.Contains(keyword, StringComparer.OrdinalIgnoreCase)))
        {
            return;
        }

        throw new MailboxMutationUnsupportedException(
            this.SessionAccountId,
            this.folder.Alias,
            mutation.Name,
            PermanentKeywordsCapabilityName);
    }

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

    /// <summary>Resolves the folder a relocation or a copy names, and reports its absence as the settled answer it is.</summary>
    /// <remarks>
    /// <para>
    /// MailKit raises <see cref="FolderNotFoundException" /> here, which is a plain exception carrying a remote path and
    /// nothing else about what was being attempted. Left alone it would reach the record as an unclassified failure and
    /// be attempted once per run until the mutation's attempt bound was spent, which buys a login per attempt to be told
    /// the same thing. Translating it is what lets the change be given up on at once and stand visible for the operator
    /// whose folder it is.
    /// </para>
    /// <para>
    /// The resolution happens before the journal is advanced, on both paths, so a missing destination leaves the record
    /// exactly where it was and nothing has to be undone.
    /// </para>
    /// </remarks>
    private async Task<IMailFolder> GetDestinationFolderAsync(
        IImapClient client,
        RemoteFolderPath destinationPath,
        MailboxMutation mutation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetFolderAsync(destinationPath.Value, cancellationToken);
        }
        catch (FolderNotFoundException absent)
        {
            throw new MailboxDestinationFolderMissingException(
                this.SessionAccountId,
                this.folder.Alias,
                mutation,
                absent);
        }
    }

    private static void RequireCapability(
        IImapClient client,
        ImapCapabilities capability,
        string operation,
        string capabilityName,
        MailAccountId accountId,
        MailFolderAlias folderAlias)
    {
        if (!client.Capabilities.HasFlag(capability))
        {
            throw new MailboxMutationUnsupportedException(accountId, folderAlias, operation, capabilityName);
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
                MailboxMutation.Relocate.Name,
                UidPlusCapabilityName,
                this.SessionAccountId,
                this.folder.Alias);

            await RemoveSourceAsync(openFolder, occurrenceId.Uid, journal, scope, cancellationToken);

            return journal.Placement;
        }

        var destination = await this.GetDestinationFolderAsync(
            client,
            destinationPath,
            MailboxMutation.Relocate,
            cancellationToken);
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
            MailboxMutation.Relocate.Name,
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
