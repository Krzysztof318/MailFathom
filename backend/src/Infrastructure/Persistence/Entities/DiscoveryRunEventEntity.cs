// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One thing that happened during a Discover run, in the place it holds in that run.</summary>
/// <remarks>
/// <para>
/// <strong>This is mail-derived and sensitive throughout.</strong> A composed block quotes somebody's correspondence
/// and a declared citation names the message it was drawn from, so the payload is classified exactly as stored mail is:
/// it reaches no log, no span, no metric, and no failure message, it goes when a data subject does through the cascade
/// from the run and from the user beneath it, and it is swept under the run's own retention rather than kept.
/// </para>
/// <para>
/// The run and the sequence together are the key, which is the promise the run's own contract makes: a sequence starts
/// at one and never skips, and the key is what makes a second row under one number impossible rather than unlikely.
/// The sequence is derived inside the statement that writes the row, so nothing outside the database decides what a
/// run has reached.
/// </para>
/// <para>
/// The kind is stored beside the payload although the payload's own discriminator carries it, because it is the one
/// thing about an event that is read without reading what the event said — whether a run ended is a kind rather than a
/// quotation — and a row that has to be parsed to be understood is one an operator cannot look at without reading
/// somebody's mail.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class DiscoveryRunEventEntity
{
    /// <summary>The table these rows live in, named here because every statement against it is composed.</summary>
    internal const string TableName = "discovery_run_events";

    /// <summary>The column naming the run this belongs to, named here for the same reason the table is.</summary>
    internal const string RunIdColumnName = "RunId";

    /// <summary>The column holding the place in the run, named here for the same reason the table is.</summary>
    internal const string SequenceColumnName = "Sequence";

    /// <summary>The column holding which kind of event this is, named here for the same reason the table is.</summary>
    internal const string KindColumnName = "Kind";

    /// <summary>The column holding the event itself, named here for the same reason the table is.</summary>
    internal const string PayloadColumnName = "Payload";

    /// <summary>The column holding when the event was written, named here for the same reason the table is.</summary>
    internal const string WrittenAtColumnName = "WrittenAt";

    /// <summary>The longest kind the column takes.</summary>
    /// <remarks>A bound on the row rather than a statement of the format: what is written is one of the event contract's own published names, the longest of which is nine characters, and the column is given room past that so the names stay the contract's to choose.</remarks>
    internal const int KindLengthLimit = 64;

    /// <summary>Gets or sets the run this event belongs to.</summary>
    public Guid RunId { get; set; }

    /// <summary>Gets or sets the place this holds in the run, counted from one.</summary>
    public long Sequence { get; set; }

    /// <summary>Gets or sets the event contract's own published name for this kind of event.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets the event itself, as the run's own serialization contract writes it.</summary>
    /// <remarks>Mail-derived for a composed block and a declared citation, and counts alone for the other four kinds. It is held as JSON so a rolling upgrade's older build reads a newer build's row rather than failing on a column it does not know about.</remarks>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Gets or sets when the event was written, in UTC.</summary>
    public DateTimeOffset WrittenAt { get; set; }
}
