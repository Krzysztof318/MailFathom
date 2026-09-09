// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Emails.Search.Phrasing;

namespace MailFathom.AI.Search;

/// <summary>Turns what a phrase-reading agent wrote into a reading the search screen can draw, or into no reading at all.</summary>
/// <remarks>
/// <para>
/// <strong>It never fails.</strong> A sentence is not unsearchable because a model wrapped its answer in prose, wrote a
/// date no calendar holds, or answered with a filter this build has no chip for. Each of those is read as far as it
/// goes and the rest is dropped, and an answer nothing survives of becomes
/// <see cref="MailSearchPhraseReading.Nothing" /> — the plain word search a deployment with no model runs.
/// </para>
/// <para>
/// Every value is bounded here rather than trusted, because each one becomes something a person is shown as their own
/// interpretation: a criterion past the bound would be a model restating the sentence back at them, and an unaccounted
/// part past it would be the whole sentence quoted as unread.
/// </para>
/// <para>
/// That is also what makes the reading testable without a provider: everything below is a pure function of the text,
/// so the cases a provider produces once in a thousand runs are ordinary examples here.
/// </para>
/// </remarks>
internal static class MailSearchPhraseDocumentReading
{
    /// <summary>The greatest number of characters an address filter may carry, which is the longest a mail address is.</summary>
    private const int MaximumAddressLength = 320;

    /// <summary>Reads a sentence's interpretation out of an agent's answer.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <returns>The reading, or <see cref="MailSearchPhraseReading.Nothing" /> where the answer carries none.</returns>
    internal static MailSearchPhraseReading Read(string? answerText)
    {
        if (ReadDocument(answerText) is not { } document)
        {
            return MailSearchPhraseReading.Nothing;
        }

        var filters = ReadFilters(document.Filters);

        // Distinct because each criterion is drawn as one object somebody takes off, and two that read the same are
        // one thing to them: a second copy would be a control that removes both or neither, whichever the screen
        // happened to do.
        IReadOnlyList<string> criteria =
        [
            .. (document.Criteria ?? [])
                .Select(static criterion => Bounded(criterion, MailSearchPhraseReading.MaximumCriterionLength))
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MailSearchPhraseReading.MaximumCriteria),
        ];

        var unaccounted = Bounded(document.Unaccounted, MailSearchPhraseReading.MaximumUnaccountedLength);

        // An object carrying no constraint, no criterion and nothing left over is a model that answered in the right
        // shape and read nothing, which is the same search as no reading at all. Publishing it as a reading would show
        // somebody an interpretation with nothing in it and claim their sentence was understood.
        return filters == MailSearchPhraseFilters.None && criteria.Count is 0 && unaccounted is null
            ? MailSearchPhraseReading.Nothing
            : new MailSearchPhraseReading(filters, criteria, unaccounted, WasRead: true);
    }

    private static MailSearchPhraseDocument? ReadDocument(string? answerText)
    {
        if (AgentJsonAnswer.Unfenced(answerText) is not { } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, MailSearchPhraseJsonContext.Default.MailSearchPhraseDocument);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads the constraints half, keeping each field only where it is one this screen can draw and remove.</summary>
    /// <remarks>
    /// A range whose end falls before its start is dropped whole rather than half kept. The two days are one statement
    /// about time, and keeping the readable half of a contradiction would narrow a search by something the person never
    /// said and cannot recognize as wrong.
    /// </remarks>
    private static MailSearchPhraseFilters ReadFilters(MailSearchPhraseFiltersDocument? document)
    {
        if (document is null)
        {
            return MailSearchPhraseFilters.None;
        }

        var from = Day(document.ReceivedFrom);
        var to = Day(document.ReceivedTo);

        if (from is { } start && to is { } end && end < start)
        {
            from = null;
            to = null;
        }

        return new MailSearchPhraseFilters
        {
            SenderAddress = Address(document.SenderAddress),
            RecipientAddress = Address(document.RecipientAddress),
            ReceivedFrom = from,
            ReceivedTo = to,
            Unread = document.Unread is true,
            Flagged = document.Flagged is true,
            HasAttachments = document.HasAttachments is true,
        };
    }

    /// <summary>Reads a calendar day, or nothing where what was written is not one.</summary>
    /// <remarks>The one format the instruction asks for, rather than whatever the current culture would also accept: a model that answered in another shape has written a date this build cannot be sure it read the same way round.</remarks>
    private static DateOnly? Day(string? written) =>
        DateOnly.TryParseExact(
            written?.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var day)
            ? day
            : null;

    /// <summary>Keeps a value only where it could be the address it is being used as.</summary>
    /// <remarks>
    /// The same refusal the screen's own address field makes, for the same reason: what an address is, is the
    /// deployment's to judge, and this refuses only what could not be one at all — so a model that wrote a person's
    /// name into an address field narrows nothing rather than narrowing to a filter that matches no mail.
    /// </remarks>
    private static string? Address(string? written)
    {
        if (Bounded(written, MaximumAddressLength) is not { } value)
        {
            return null;
        }

        var at = value.IndexOf('@', StringComparison.Ordinal);

        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1 && !value.Any(char.IsWhiteSpace)
            ? value
            : null;
    }

    /// <summary>Trims a value and keeps it only where something is left and it is short enough to be what it claims.</summary>
    /// <remarks>Refused rather than cut, because every one of these is shown to the person as their own words: a criterion truncated mid-phrase reads as something they wrote and did not.</remarks>
    private static string? Bounded(string? written, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(written))
        {
            return null;
        }

        var value = written.Trim();

        return value.Length > maximumLength || value.Any(char.IsControl) ? null : value;
    }
}
