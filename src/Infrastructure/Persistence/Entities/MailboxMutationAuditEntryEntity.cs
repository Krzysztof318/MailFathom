// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One finished change to a remote mailbox, kept for as long as its account's retention says.</summary>
/// <remarks>
/// Every association this row could have carried is a plain value instead, and that is the whole design: an entry that
/// hung on the stored email would be erased by that email's deletion, which is exactly the entry an audit of deletions
/// exists to hold. The same reasoning keeps the account and the folder out — the trail describes an act, and an act
/// stays true after the things it acted on are gone.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailboxMutationAuditEntryEntity
{
    /// <summary>The longest remote folder path stored, matching the bound the folder binding's own path column carries.</summary>
    internal const int MaximumFolderPathLength = 512;

    public Guid Id { get; set; }

    /// <summary>Gets or sets the mutation record this entry was written from.</summary>
    /// <remarks>
    /// Unique, which is what makes an append idempotent: one mutation has one ending, so a retried append after a commit
    /// whose answer was lost is refused by the index rather than producing a second entry for the same act.
    /// </remarks>
    public Guid MutationRecordId { get; set; }

    /// <summary>Gets or sets the account whose mailbox was changed, as a value rather than as an association.</summary>
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the local email the change was about, as a value rather than as an association.</summary>
    public Guid StoredEmailId { get; set; }

    /// <summary>Gets or sets the mutation's own name, which is the identity the closed enumeration publishes.</summary>
    public required string Mutation { get; set; }

    /// <summary>Gets or sets the remote path of the folder the email was in when the change was asked for.</summary>
    /// <remarks>
    /// The path is stored rather than the folder key, because a key resolves through a binding that a later rebinding
    /// replaces and a deleted account removes. What an operator asks months later is which folder the mail was in, and
    /// the answer has to be readable without anything else still existing.
    /// </remarks>
    public required string SourceFolderPath { get; set; }

    /// <summary>Gets or sets the hierarchy delimiter the source path was read with, where one is known.</summary>
    public string? SourceHierarchyDelimiter { get; set; }

    public uint SourceUidValidity { get; set; }

    public uint SourceUid { get; set; }

    /// <summary>Gets or sets the folder a relocation or a copy named, and <see langword="null" /> for every other mutation.</summary>
    public string? DestinationFolderPath { get; set; }

    /// <summary>Gets or sets the hierarchy delimiter the destination path was created with, where one is known.</summary>
    public string? DestinationHierarchyDelimiter { get; set; }

    /// <summary>Gets or sets the UIDVALIDITY a <c>COPYUID</c> response named, where the server supplied one.</summary>
    public uint? PlacementUidValidity { get; set; }

    /// <summary>Gets or sets the UID a <c>COPYUID</c> response named, where the server supplied one.</summary>
    public uint? PlacementUid { get; set; }

    /// <summary>Gets or sets which way a <c>\Seen</c> change was asked for, and <see langword="null" /> for every other mutation.</summary>
    public bool? DesiredSeenState { get; set; }

    public MailboxMutationOrigin RequesterOrigin { get; set; }

    /// <summary>Gets or sets the requester identity, which carries a rule's revision where a rule asked.</summary>
    public required string RequesterIdentity { get; set; }

    /// <summary>Gets or sets when the change was written down, which is when somebody asked for it.</summary>
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>Gets or sets when the change reached the ending this entry records.</summary>
    public DateTimeOffset CompletedAt { get; set; }

    public MailboxMutationAuditOutcome Outcome { get; set; }

    /// <summary>Gets or sets the code of the failure an abandoned change was given up on for, and <see langword="null" /> for one that was performed.</summary>
    public int? FailureCode { get; set; }
}
