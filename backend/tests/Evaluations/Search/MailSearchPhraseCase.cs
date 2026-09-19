// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search.Phrasing;

namespace MailFathom.Evaluations.Search;

/// <summary>One sentence typed into the search box, and what the reading of it has to hold.</summary>
/// <remarks>
/// <para>
/// A sentence is written here rather than drawn from the corpus, because what is measured is the reading of a sentence and
/// no mail reaches the agent. Every one is invented for this suite: it names nobody, and an address it names sits under a
/// reserved domain, so a sentence, its cached answer, and its report can be published like everything else a run keeps.
/// </para>
/// <para>
/// Each sentence states one filter outright, and the expectation asks for that filter and for no filter the sentence did
/// not state, because a guessed filter hides mail where a guessed criterion only ranks it lower. The one that reads two
/// ways states nothing beyond a reading and is the one kind a judge grades.
/// </para>
/// </remarks>
/// <param name="Name">The name the case is filed and reported under.</param>
/// <param name="Sentence">The sentence, in a person's own words.</param>
/// <param name="ReadsTwoWays">Whether the sentence reads two ways, so that no single reading is the right one.</param>
/// <param name="Expectation">Names what the reading gets wrong, or answers <see langword="null" /> when it gets nothing wrong.</param>
internal sealed record MailSearchPhraseCase(
    string Name,
    string Sentence,
    bool ReadsTwoWays,
    Func<MailSearchPhraseReading, string?> Expectation)
{
    /// <summary>The day every sentence is typed on, which a stated period is resolved against.</summary>
    public static readonly DateOnly AskedOn = new(2026, 9, 14);

    private const string StatedSender = "billing@northwind.example";

    /// <summary>Gets every case, in the order the report lists them.</summary>
    public static IReadOnlyList<MailSearchPhraseCase> All { get; } =
    [
        new(
            "Sender",
            $"invoices from {StatedSender}",
            ReadsTwoWays: false,
            static reading => string.Equals(reading.Filters.SenderAddress, StatedSender, StringComparison.OrdinalIgnoreCase)
                ? OnlyStated(reading, reading.Filters with { SenderAddress = null })
                : $"the sender filter reads {reading.Filters.SenderAddress ?? "nothing"} rather than {StatedSender}."),
        new(
            "Period",
            "contract drafts I received in August 2026",
            ReadsTwoWays: false,
            static reading => reading.Filters is { ReceivedFrom: { } from, ReceivedTo: { } to }
                && from == new DateOnly(2026, 8, 1)
                && to == new DateOnly(2026, 8, 31)
                ? OnlyStated(reading, reading.Filters with { ReceivedFrom = null, ReceivedTo = null })
                : $"the period reads {reading.Filters.ReceivedFrom?.ToString("yyyy-MM-dd", null) ?? "open"} to {reading.Filters.ReceivedTo?.ToString("yyyy-MM-dd", null) ?? "open"} rather than 2026-08-01 to 2026-08-31."),
        new(
            "Attachment",
            "the spreadsheet with the Q3 budget somebody attached",
            ReadsTwoWays: false,
            static reading => reading.Filters.HasAttachments
                ? OnlyStated(reading, reading.Filters with { HasAttachments = false })
                : "the sentence asks for mail carrying a file and the reading does not filter on attachments."),
        new(
            "Unread",
            "unread messages about the server outage",
            ReadsTwoWays: false,
            static reading => reading.Filters.Unread
                ? OnlyStated(reading, reading.Filters with { Unread = false })
                : "the sentence asks for unread mail and the reading does not filter on it."),
        new(
            "WordsOnly",
            "notes about the kitchen renovation quote",
            ReadsTwoWays: false,
            static reading => OnlyStated(reading, reading.Filters)),
        new(
            "OutstandingOrUnread",
            "invoices I still need to deal with",
            ReadsTwoWays: true,
            static _ => null),
    ];

    /// <summary>Finds a case by the name it is filed under.</summary>
    /// <param name="name">The case's name.</param>
    /// <returns>The case.</returns>
    public static MailSearchPhraseCase Named(string name) =>
        All.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => this.Name;

    /// <summary>Holds what is left of the filters, once the stated one is taken out, to nothing, and asks for a criterion.</summary>
    /// <remarks>Every sentence here describes a subject, and the instruction asks for a criterion wherever one does.</remarks>
    private static string? OnlyStated(MailSearchPhraseReading reading, MailSearchPhraseFilters unstated)
    {
        if (unstated != MailSearchPhraseFilters.None)
        {
            return $"the reading adds a filter the sentence never stated: {unstated}.";
        }

        return reading.Criteria.Count is 0
            ? "the reading carries no criterion, though the sentence describes what the mail is about."
            : null;
    }
}
