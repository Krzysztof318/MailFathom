// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.ThreadStates;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One statement of a conversation's state, and the messages it rests on.</summary>
/// <remarks>
/// <para>
/// A row per statement rather than four sets of columns on the state, because the four aspects are the same shape and a
/// conversation carries any number of each: columns would have meant a fixed count per aspect chosen in advance, and a
/// fifth aspect would have meant a schema change rather than a value.
/// </para>
/// <para>
/// The ordinal is stored rather than derived, because the order is the producer's own — it names what matters most
/// first — and a row order is not a property a table has. It is per aspect, so the statements of one aspect read back
/// as the producer wrote them.
/// </para>
/// <para>
/// The sources are a <c>uuid[]</c> of the messages the statement rests on, in the order the producer named them, rather
/// than a table of their own. A statement's sources are read whole with the statement and never joined to, so a row per
/// citation would have bought a join and an ordinal column to preserve an order the array already carries; what a query
/// still does with it is ask which statements cite a message, which <c>= ANY</c> answers.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailThreadStateEntryEntity
{
    /// <summary>The greatest length a stored statement has, which the application value already shortens to.</summary>
    internal const int MaximumTextLength = ThreadStateEntry.MaximumTextLength;

    /// <summary>The greatest length a stored owner has, which the application value already shortens to.</summary>
    internal const int MaximumOwedByLength = ThreadStateEntry.MaximumOwedByLength;

    public long Id { get; set; }

    public Guid EmailThreadId { get; set; }

    /// <summary>Gets or sets the state this statement belongs to, which a write leaves unset.</summary>
    /// <remarks>Optional for the reason the state's own navigation onto its conversation is.</remarks>
    public EmailThreadStateEntity? ThreadState { get; set; }

    public ThreadStateAspect Aspect { get; set; }

    /// <summary>Gets or sets the statement's place among those of its own aspect, in the order the producer named them.</summary>
    public int Ordinal { get; set; }

    public required string Text { get; set; }

    /// <summary>Gets or sets who owes the commitment, absent on every aspect but a commitment and on a commitment that named nobody.</summary>
    public string? OwedBy { get; set; }

    /// <summary>Gets or sets when the commitment falls due, absent on every aspect but a commitment and on a commitment that named no date.</summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>Gets or sets the messages the statement rests on, in the order the producer named them.</summary>
    public required Guid[] Sources { get; set; }
}
