// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;

namespace MailFathom.Domain.Mutations.Audit;

/// <summary>States one change MailFathom finished making to a mailbox, in the form that outlives the mail it changed.</summary>
/// <remarks>
/// <para>
/// This is the opposite artifact from <see cref="MailboxMutationRecord" /> and shares nothing but its source. The record
/// exists to make a mutation correct — it carries the idempotency identity, the stage a retry resumes from, and the
/// provenance that stops a rule reacting to its own effect — and its useful life ends when the mutation reaches a
/// terminal stage. This is written once at exactly that moment, read by nothing the mechanism depends on, and kept for
/// as long as its account's retention says.
/// </para>
/// <para>
/// It names the email by <see cref="StoredEmailId" /> and holds every folder as a path rather than as a binding, which
/// is what lets it survive the email's deletion — including when the mutation recorded <em>was</em> that deletion.
/// Inheriting the mail's deletion path would erase the record that MailFathom deleted it, which removes exactly the
/// entry an audit of deletions exists to hold.
/// </para>
/// <para>
/// It holds no mail content: no subject, no address, no body fragment, no filename. A folder path, a UIDVALIDITY, a UID,
/// a mutation name, and a requester identity are the server's own or MailFathom's own names for things. It is still
/// derived personal data — it says where a person's mail has been, when, and at whose instruction — which is why it is
/// written only for an account whose operator turned the trail on.
/// </para>
/// </remarks>
public sealed record MailboxMutationAuditEntry
{
    /// <summary>Gets what addresses this entry, independently of the mutation record it was written from.</summary>
    public required MailboxMutationAuditEntryId Id { get; init; }

    /// <summary>Gets the mutation record this entry was written from.</summary>
    /// <remarks>
    /// It correlates the history with the operational record while that record still exists, and stops naming anything
    /// once it is pruned. Nothing here reads it back: it is a fact about which mutation this was, not a reference the
    /// entry depends on.
    /// </remarks>
    public required MailboxMutationRecordId MutationRecordId { get; init; }

    /// <summary>Gets the account whose mailbox was changed.</summary>
    public required MailAccountId AccountId { get; init; }

    /// <summary>Gets the owner whose account the change was performed in.</summary>
    /// <remarks>
    /// Taken from the mutation record this entry states the ending of, so the trail records whose mailbox was changed
    /// without asking the account table what the identifier means.
    /// </remarks>
    public required MailOwnerId Owner { get; init; }

    /// <summary>Gets the local email the change was about.</summary>
    /// <remarks>
    /// It is a value rather than an association, so nothing about the email's own lifetime reaches this entry. It stays
    /// the identifier an operator joins the trail to a mailbox query by while the email exists, and stays readable as an
    /// identity afterwards.
    /// </remarks>
    public required StoredEmailId StoredEmailId { get; init; }

    /// <summary>Gets the change that was made.</summary>
    public required MailboxMutation Mutation { get; init; }

    /// <summary>Gets the remote path of the folder the email was in when the change was asked for.</summary>
    public required RemoteFolderPath SourceFolderPath { get; init; }

    /// <summary>Gets the UIDVALIDITY that folder reported when the change was asked for.</summary>
    public required ImapUidValidity SourceUidValidity { get; init; }

    /// <summary>Gets the UID the email carried in that folder.</summary>
    public required ImapUid SourceUid { get; init; }

    /// <summary>Gets the remote path of the folder a relocation or a copy named, and <see langword="null" /> for every other mutation.</summary>
    public required RemoteFolderPath? DestinationFolderPath { get; init; }

    /// <summary>Gets where the destination folder put the email, as far as the server said.</summary>
    /// <remarks>
    /// It is <see cref="RemoteEmailPlacement.NotReported" /> for a mutation that places nothing, for one the server
    /// completed without a <c>COPYUID</c> response, and for one that was abandoned before it placed anything. The
    /// mutation and the outcome together are what tell those apart.
    /// </remarks>
    public required RemoteEmailPlacement Placement { get; init; }

    /// <summary>Gets which way a <c>\Seen</c> change was asked for, and <see langword="null" /> for every other mutation.</summary>
    public required bool? DesiredSeenState { get; init; }

    /// <summary>Gets the authored act that asked, whose identity carries a rule's revision where a rule asked.</summary>
    public required MailboxMutationRequester Requester { get; init; }

    /// <summary>Gets when the change was written down, which is when somebody asked for it.</summary>
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>Gets when the change reached the ending this entry records.</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Gets how the change ended.</summary>
    public required MailboxMutationAuditOutcome Outcome { get; init; }

    /// <summary>Gets the code of the failure an abandoned change was given up on for, and <see langword="null" /> for one that was performed.</summary>
    /// <remarks>
    /// The code is kept and the message is not, for the reason the mutation record keeps only the code: a code is a
    /// stable identity somebody can look up months later, while a message is text assembled at the failure site.
    /// </remarks>
    public required MailFathomErrorCode? Failure { get; init; }

    /// <summary>Writes the entry one finished mutation leaves behind.</summary>
    /// <param name="id">The identity the entry is addressed by.</param>
    /// <param name="record">The mutation record, at the terminal stage it ended in.</param>
    /// <param name="sourceFolder">The binding the source occurrence was read under, which supplies its remote path.</param>
    /// <param name="completedAt">When the mutation reached that stage.</param>
    /// <returns>The entry to append to the trail.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> or <paramref name="sourceFolder" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="record" /> has not reached a terminal stage, or when <paramref name="sourceFolder" /> is not the binding the record's occurrence names.</exception>
    /// <remarks>
    /// The terminal stage is required rather than assumed, because an entry written from a mutation still in flight
    /// would state an ending that had not happened and no later write corrects it.
    /// </remarks>
    public static MailboxMutationAuditEntry Of(
        MailboxMutationAuditEntryId id,
        MailboxMutationRecord record,
        MailFolderResolution sourceFolder,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(sourceFolder);

        if (!record.IsTerminal)
        {
            throw new ArgumentException(
                $"A mutation at stage {record.Stage} has not ended, so no audit entry states how it ended.",
                nameof(record));
        }

        if (sourceFolder.Id != record.Request.Occurrence.FolderResolutionId)
        {
            throw new ArgumentException(
                "The folder binding does not carry the occurrence the mutation was requested for.",
                nameof(sourceFolder));
        }

        var wasPerformed = record.Stage == MailboxMutationStage.Completed;

        return new MailboxMutationAuditEntry
        {
            Id = id,
            MutationRecordId = record.Id,
            AccountId = record.Request.Occurrence.AccountId,
            Owner = record.Owner,
            StoredEmailId = record.Request.StoredEmailId,
            Mutation = record.Request.Mutation,
            SourceFolderPath = sourceFolder.RemotePath,
            SourceUidValidity = record.Request.Occurrence.UidValidity,
            SourceUid = record.Request.Occurrence.Uid,
            DestinationFolderPath = record.Request.DestinationPath,
            Placement = record.Placement,
            DesiredSeenState = record.Request.DesiredSeenState,
            Requester = record.Request.Requester,
            RequestedAt = record.RecordedAt,
            CompletedAt = completedAt,
            Outcome = wasPerformed ? MailboxMutationAuditOutcome.Performed : MailboxMutationAuditOutcome.Abandoned,

            // A performed change carries no failure even where an earlier attempt of it did, because the entry states
            // how the change ended rather than what it survived on the way.
            Failure = wasPerformed ? null : record.LastFailure,
        };
    }
}
