// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.ThreadStates;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>Where one conversation stands, as a derivation left it, and which shape of the conversation it describes.</summary>
/// <remarks>
/// <para>
/// Keyed by the conversation, which is what makes one state per conversation a property of the schema rather than a
/// check somebody has to remember: deriving twice reaches the same row, so two runs asking together resolve to one
/// record rather than to a history nobody asked for.
/// </para>
/// <para>
/// The two <c>DerivedFrom</c> columns are the whole of how a state stays current. They record the conversation as it
/// stood when the derivation was made, and the selection compares them against the conversation as it stands now, so a
/// conversation that gains a reply re-enters the queue on its own and nothing has to watch for a change.
/// </para>
/// <para>
/// The row exists even when the derivation found nothing to say, and that is the whole reason it is a row of its own
/// rather than a column on the statements. A conversation with no statements and no row is one still owed a derivation;
/// one with no statements and a row is one a derivation has already answered about, and paying for that answer twice is
/// exactly what the record prevents.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailThreadStateEntity
{
    public Guid EmailThreadId { get; set; }

    /// <summary>Gets or sets the conversation this state is about, which a write leaves unset.</summary>
    /// <remarks>Optional for the reason a derivation's own navigation onto its email is: the record is staged by the identifier its caller was given, and the foreign key already refuses a state about a conversation that is not there.</remarks>
    public EmailThreadEntity? EmailThread { get; set; }

    /// <summary>Gets or sets how much of the conversation the derivation was shown.</summary>
    public ThreadStateCoverage Coverage { get; set; }

    public DateTimeOffset DerivedAt { get; set; }

    /// <summary>Gets or sets how many messages the conversation held when the derivation was made.</summary>
    public int DerivedFromMessageCount { get; set; }

    /// <summary>Gets or sets when the most recent of those messages arrived, absent where none of them recorded an arrival.</summary>
    public DateTimeOffset? DerivedFromLatestArrival { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token, which is PostgreSQL's own <c>xmin</c> rather than a column.</summary>
    /// <remarks>
    /// Two runs can reach one conversation: an account run derives from it while a later run reaches the same
    /// conversation after a reply moved it back into the queue. The token is what turns that into a conflict the retry
    /// policy resolves from a fresh read instead of one writer overwriting the other's statements.
    /// </remarks>
    public uint ConcurrencyVersion { get; set; }

    /// <summary>Gets the statements the derivation produced, which is empty where it found nothing to say.</summary>
    public ICollection<EmailThreadStateEntryEntity> Entries { get; } = [];
}
