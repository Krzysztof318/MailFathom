// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using MailFathom.Application.Emails.Enrichment;

namespace MailFathom.AI.Enrichment;

/// <summary>Turns what an enrichment agent wrote into marks, keeping only what the message itself can back.</summary>
/// <remarks>
/// <para>
/// Every reading below is a pure function of the answer and the passages the turn published, which is what makes the
/// cases a provider produces once in a thousand runs ordinary examples in a test rather than something only a live
/// endpoint reaches.
/// </para>
/// <para>
/// It drops rather than repairs. A reading with no text, no reason, or no passage this turn published is one nothing
/// can be checked against, and there is no honest way to invent the missing half — so it falls away and the message
/// keeps the readings that survived. An answer nothing survives is a settled derivation of no marks, because the model
/// was reached and said nothing usable about the message; only a provider that never answered is withheld.
/// </para>
/// <para>
/// A citation is a position in the list the turn composed, so a number outside that list names no passage and takes its
/// reading with it. That is the whole of why the model is shown numbers rather than identifiers: an out-of-range number
/// is unusable, while an identifier a model wrote would be a passage of some other message and would look valid.
/// </para>
/// </remarks>
internal static class EmailEnrichmentReading
{
    private const string JsonFence = "```";

    /// <summary>Reads the marks out of an agent's answer.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <param name="passages">The passages the turn published, in the order it numbered them.</param>
    /// <param name="agentName">The name the agent was composed under, which every mark records as its origin.</param>
    /// <returns>The marks that survived, which is empty where none did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passages" /> is <see langword="null" />.</exception>
    internal static IReadOnlyList<EmailEnrichmentMark> Read(
        string? answerText,
        IReadOnlyList<EnrichablePassage> passages,
        string agentName)
    {
        ArgumentNullException.ThrowIfNull(passages);

        if (ReadDocument(answerText) is not { } document)
        {
            return [];
        }

        var provenance = EmailEnrichmentProvenance.FromAgent(agentName);

        return
        [
            .. new[]
                {
                    ToMark(EmailEnrichmentAspect.Sense, document.Sense, passages, provenance),
                    ToMark(EmailEnrichmentAspect.Significance, document.Significance, passages, provenance),
                    ToMark(EmailEnrichmentAspect.Commitment, document.Commitment, passages, provenance),
                }
                .OfType<EmailEnrichmentMark>(),
        ];
    }

    /// <summary>Turns one written reading into a mark, or into nothing where the message cannot back it.</summary>
    private static EmailEnrichmentMark? ToMark(
        EmailEnrichmentAspect aspect,
        EmailEnrichmentMarkDocument? written,
        IReadOnlyList<EnrichablePassage> passages,
        EmailEnrichmentProvenance provenance)
    {
        if (written is null
            || string.IsNullOrWhiteSpace(written.Text)
            || string.IsNullOrWhiteSpace(written.Reason))
        {
            return null;
        }

        var evidence = (written.Passages ?? [])
            .Where(ordinal => ordinal >= 0 && ordinal < passages.Count)
            .Distinct()
            .Select(ordinal => passages[ordinal].Id)
            .Take(EmailEnrichmentMark.MaximumEvidenceCount)
            .ToArray();

        if (evidence.Length is 0)
        {
            return null;
        }

        return EmailEnrichmentMark.Create(
            aspect,
            written.Text,
            written.Reason,
            evidence,
            provenance,
            aspect is EmailEnrichmentAspect.Commitment ? ReadDueAt(written.DueAt) : null);
    }

    /// <summary>Reads the instant a commitment falls due, or nothing where the model wrote something that is not one.</summary>
    /// <remarks>
    /// The date falls away on its own rather than taking the commitment with it. A commitment somebody made is still
    /// worth showing when the model wrote its date in a shape nothing can parse, and a date is the part of a mark a
    /// reader would otherwise act on without checking.
    /// </remarks>
    private static DateTimeOffset? ReadDueAt(string? dueAt) =>
        DateTimeOffset.TryParse(
            dueAt,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    private static EmailEnrichmentDocument? ReadDocument(string? answerText)
    {
        if (Unfenced(answerText) is not { } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, EmailEnrichmentJsonContext.Default.EmailEnrichmentDocument);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Finds the JSON object inside whatever the model wrote around it.</summary>
    /// <remarks>
    /// A model told to answer with one object still fences it, prefaces it, or writes a sentence after it often enough
    /// that treating any of those as a failed derivation would throw away usable marks. The outermost braces are what
    /// is read; anything either side of them is discarded unexamined.
    /// </remarks>
    private static string? Unfenced(string? answerText)
    {
        if (string.IsNullOrWhiteSpace(answerText))
        {
            return null;
        }

        var text = answerText.Replace(JsonFence, string.Empty, StringComparison.Ordinal);
        var opening = text.IndexOf('{', StringComparison.Ordinal);
        var closing = text.LastIndexOf('}');

        return opening >= 0 && closing > opening ? text[opening..(closing + 1)] : null;
    }
}
