// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One question answered from one account's mailbox, kept for as long as that account's retention says.</summary>
/// <remarks>
/// The account is a plain value and the emails are an association, which is the split the record's obligations demand.
/// An account being removed from configuration leaves its history readable, because what a deployment stopped serving it
/// still answered for; an email being erased reaches the runs that read it, because the record names messages rather
/// than acts performed on them and a message nobody may hold any more is not one this row may go on naming.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailAnsweringAuditEntryEntity
{
    /// <summary>The longest chat endpoint alias stored, matching the bound an account identifier carries.</summary>
    internal const int MaximumAliasLength = 128;

    /// <summary>The longest instruction version stored, which is a short digest of this build's own text.</summary>
    internal const int MaximumInstructionsVersionLength = 64;

    public Guid Id { get; set; }

    /// <summary>Gets or sets the run this entry records, which the entries of the run's other accounts share.</summary>
    /// <remarks>
    /// Unique together with the account, which is what makes an append idempotent: one run has one ending per account,
    /// so a retried append after a commit whose answer was lost is refused by the index rather than producing a second
    /// entry for the same question.
    /// </remarks>
    public Guid RunId { get; set; }

    /// <summary>Gets or sets the account whose mailbox the run was allowed to read, as a value rather than as an association.</summary>
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the owner whose mailbox the run was allowed to read, as a value rather than as an association.</summary>
    public required Guid OwnerId { get; set; }

    /// <summary>Gets or sets this deployment's own configured name for the chat endpoint the run was conducted through.</summary>
    public required string ChatEndpointAlias { get; set; }

    /// <summary>Gets or sets the version of the instruction the run was conducted under.</summary>
    public required string InstructionsVersion { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Gets or sets when the run reached the ending this entry records.</summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>Gets or sets the name of the ending the run reached.</summary>
    /// <remarks>
    /// The column holds the name directly rather than a converted enum, so a name this build declares no member for is
    /// a row the mapping refuses instead of one that fails materialization. That matters here more than anywhere else in
    /// this model: a record is read a page at a time, and an ending a later build wrote would otherwise fail the page
    /// containing it and every page after it.
    /// </remarks>
    public required string Outcome { get; set; }

    /// <summary>Gets or sets the names of the ways the run read less of the mailbox than an undegraded run of the same question would.</summary>
    /// <remarks>A set, so the text names none, one, or both — held directly for the reason <see cref="Outcome" /> is.</remarks>
    public required string Degradation { get; set; }

    /// <summary>Gets or sets the emails of this account the run retrieved, which the run's answer may then have cited.</summary>
    /// <remarks>
    /// Rows rather than an array column, which is what makes the deletion obligation structural: an erased message takes
    /// its row with it through the foreign key, instead of leaving an identifier inside a value nothing would reach.
    /// </remarks>
    public ICollection<MailAnsweringAuditedEmailEntity> Emails { get; } = [];
}
