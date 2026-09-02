// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class RecurringSendEntity
{
    public Guid Id { get; set; }

    /// <summary>Gets or sets the account every occurrence is submitted through and sent as.</summary>
    /// <remarks>A plain column rather than a foreign key onto the stored account, for the reason the outgoing record's copy is one: an account configured to send need never have synchronized anything, and a key here would refuse a declaration from a submission-only account instead of recording it.</remarks>
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the owner whose account the recurrence sends from.</summary>
    public required Guid OwnerId { get; set; }

    public OutgoingEmailOrigin RequesterOrigin { get; set; }

    public required string RequesterIdentity { get; set; }

    /// <summary>Gets or sets the repetition as it was declared, in the syntax every recurring dispatch is written in.</summary>
    /// <remarks>
    /// Stored as the text rather than as parsed parts, because the text is what an operator reads back when they ask
    /// what repeats and because the parts are the schedule syntax's to own. It was parsed before this row was written,
    /// so a value here names occasions unless the syntax itself has moved underneath it.
    /// </remarks>
    public required string Schedule { get; set; }

    /// <summary>Gets or sets how many bytes of MIME were stored as the draft occurrences are composed from.</summary>
    /// <remarks>Kept here as well as on the draft row so that what a declaration will send can be read without pulling the draft's <c>bytea</c> into memory, exactly as the outgoing record's length is.</remarks>
    public long DraftByteLength { get; set; }

    public DateTimeOffset DeclaredAt { get; set; }

    /// <summary>Gets or sets when the declaration was stopped, and <see langword="null" /> while it still produces occurrences.</summary>
    /// <remarks>
    /// An instant rather than a flag, and the row is kept rather than deleted: what an owner stopped and when they
    /// stopped it is part of the account of a mailbox that used to send something every week, and a deleted row would
    /// make the stopping indistinguishable from a declaration nobody ever made.
    /// </remarks>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Gets or sets the occasion this declaration last produced a message for, and <see langword="null" /> while it has produced none.</summary>
    public DateTimeOffset? LastOccurrenceAt { get; set; }

    /// <summary>Gets or sets the message the last occasion produced, and <see langword="null" /> while there has been none.</summary>
    /// <remarks>
    /// No foreign key stands behind it, deliberately. The occurrence is an ordinary outgoing record with a lifetime of
    /// its own — erased with the mail it belongs to, and long outlived by the declaration — and a key here would either
    /// refuse that erasure or rewrite what this declaration last did.
    /// </remarks>
    public Guid? LastOccurrenceEmailId { get; set; }

    public ICollection<RecurringSendRecipientEntity> Recipients { get; } = [];

    /// <summary>Gets or sets the stored draft this declaration points at, loaded only where a caller asked for it.</summary>
    /// <remarks>The navigation exists so the draft is erased with the declaration it belongs to; nothing that lists declarations loads it, for the reason every raw MIME column carries.</remarks>
    public RecurringSendDraftEntity? Draft { get; set; }

    /// <summary>Gets or sets the PostgreSQL <c>xmin</c> token this row's optimistic concurrency is detected through.</summary>
    public uint ConcurrencyVersion { get; set; }
}
