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
/// Each sentence states its filters outright, or states none, and the expectation asks for those filters and for no
/// filter the sentence did not state, because a guessed filter hides mail where a guessed criterion only ranks it lower.
/// A sentence naming two senders is the sharpest form of that: the one sender filter there is can hold only one of them,
/// so writing either hides the other's mail. The two that read two ways state nothing beyond a reading and are the one
/// kind a judge grades.
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

    private const string SecondSender = "accounts@fabrikam.example";

    private const string SupportSender = "support@contoso.example";

    private const string LegalRecipient = "legal@fabrikam.example";

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
        new(
            "Yesterday",
            "the delivery notes that came in yesterday",
            ReadsTwoWays: false,
            static reading => reading.Filters is { ReceivedFrom: { } from, ReceivedTo: { } to }
                && from == AskedOn.AddDays(-1)
                && to == AskedOn.AddDays(-1)
                ? OnlyStated(reading, reading.Filters with { ReceivedFrom = null, ReceivedTo = null })
                : $"the period reads {Period(reading.Filters)} rather than the one day before {AskedOn:yyyy-MM-dd}."),
        new(
            "SinceStartOfMonth",
            "anything about the office move since the start of this month",
            ReadsTwoWays: false,
            static reading => reading.Filters.ReceivedFrom == new DateOnly(2026, 9, 1)
                && (reading.Filters.ReceivedTo is null || reading.Filters.ReceivedTo == AskedOn)
                ? OnlyStated(reading, reading.Filters with { ReceivedFrom = null, ReceivedTo = null })
                : $"the period reads {Period(reading.Filters)} rather than 2026-09-01 onwards."),
        new(
            "TwoSenders",
            $"anything from {StatedSender} or {SecondSender} about the renewal",
            ReadsTwoWays: false,
            static reading => reading.Filters.SenderAddress is { } chosen
                ? $"the sender filter reads {chosen} alone, which hides every message the other sender wrote."
                : OnlyStated(reading, reading.Filters)),
        new(
            "SenderAndWords",
            $"unread mail from {SupportSender} about the password reset",
            ReadsTwoWays: false,
            static reading => string.Equals(reading.Filters.SenderAddress, SupportSender, StringComparison.OrdinalIgnoreCase) && reading.Filters.Unread
                ? OnlyStated(reading, reading.Filters with { SenderAddress = null, Unread = false })
                : $"the reading holds sender {reading.Filters.SenderAddress ?? "nothing"} and unread {reading.Filters.Unread} rather than {SupportSender} and unread."),
        new(
            "Recipient",
            $"what I sent to {LegalRecipient} about the NDA",
            ReadsTwoWays: false,
            static reading => string.Equals(reading.Filters.RecipientAddress, LegalRecipient, StringComparison.OrdinalIgnoreCase)
                ? OnlyStated(reading, reading.Filters with { RecipientAddress = null })
                : $"the recipient filter reads {reading.Filters.RecipientAddress ?? "nothing"} rather than {LegalRecipient}."),
        new(
            "Flagged",
            "starred messages about the office lease",
            ReadsTwoWays: false,
            static reading => reading.Filters.Flagged
                ? OnlyStated(reading, reading.Filters with { Flagged = false })
                : "the sentence asks for starred mail and the reading does not filter on it."),
        new(
            "PersonWithoutAddress",
            "emails from Ingrid about the travel budget",
            ReadsTwoWays: false,
            static reading => OnlyStated(reading, reading.Filters)),
        new(
            "AttachmentInMonth",
            "PDFs the auditor sent in July 2026",
            ReadsTwoWays: false,
            static reading => reading.Filters is { HasAttachments: true, ReceivedFrom: { } from, ReceivedTo: { } to }
                && from == new DateOnly(2026, 7, 1)
                && to == new DateOnly(2026, 7, 31)
                ? OnlyStated(reading, reading.Filters with { HasAttachments = false, ReceivedFrom = null, ReceivedTo = null })
                : $"the reading holds attachments {reading.Filters.HasAttachments} over {Period(reading.Filters)} rather than files over 2026-07-01 to 2026-07-31."),
        new(
            "ImportantReadsTwoWays",
            "the important messages about the merger",
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

    private static string Period(MailSearchPhraseFilters filters) =>
        $"{filters.ReceivedFrom?.ToString("yyyy-MM-dd", null) ?? "open"} to {filters.ReceivedTo?.ToString("yyyy-MM-dd", null) ?? "open"}";

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
