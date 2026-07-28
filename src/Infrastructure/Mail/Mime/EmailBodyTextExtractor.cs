// Copyright © 2026 Krzysztof Kasprowicz

using System.Text;
using MailMcp.Application.Emails;
using MailMcp.Domain.Emails;
using MimeKit;

namespace MailMcp.Infrastructure.Mail.Mime;

/// <summary>Derives the searchable text of one message from the parts its structure resolved as the body.</summary>
/// <remarks>
/// A genuine <c>text/plain</c> part is preferred over every HTML alternative, because it is what the sender wrote
/// rather than a reading of how it was displayed. HTML is used only when the message offered no plain-text alternative,
/// and the result is marked lossy so nothing downstream treats the two as the same evidence.
/// </remarks>
internal static class EmailBodyTextExtractor
{
    /// <summary>Extracts the body text of one classified message.</summary>
    /// <param name="classification">What the structural walk resolved as attachments and as body text parts.</param>
    /// <param name="maxCharacters">The greatest number of characters the extracted text may hold.</param>
    /// <returns>The extracted text, or the reason the message yielded none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="classification" /> is <see langword="null" />.</exception>
    public static ExtractedEmailText Extract(MimeContentClassification classification, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(classification);

        // An encrypted body is present and unreadable rather than absent. Recording it as empty would make it
        // indistinguishable from a message that genuinely said nothing, and the difference is the whole reason the
        // marker exists: one is a complete record, the other a permanent gap in search.
        if (classification.Attachments.IsEncrypted)
        {
            return ExtractedEmailText.EncryptedBody;
        }

        if (ReadPlainTextBody(classification.BodyTextParts) is { } plainText)
        {
            return Build(plainText, maxCharacters, ExtractedEmailText.FromPlainTextBody);
        }

        if (DeriveTextFromHtmlBody(classification.BodyTextParts) is { } derivedText)
        {
            return Build(derivedText, maxCharacters, ExtractedEmailText.DerivedFromHtmlBody);
        }

        return ExtractedEmailText.NoTextualBody;
    }

    /// <summary>Turns one source's raw text into the pair the index and a later reader each need.</summary>
    private static ExtractedEmailText Build(
        string rawText,
        int maxCharacters,
        Func<string, string, ExtractedEmailText> describe)
    {
        var originalText = BoundLength(NormalizeForIndexing(rawText), maxCharacters);
        if (originalText.Length == 0)
        {
            return ExtractedEmailText.NoTextualBody;
        }

        return describe(originalText, QuotedHistoryTrimmer.Trim(originalText));
    }

    /// <summary>Reads the plain-text body, joining the parts when a message resolved several as its body.</summary>
    private static string? ReadPlainTextBody(IReadOnlyList<TextPart> bodyTextParts)
    {
        var plainTextParts = bodyTextParts.Where(part => part.IsPlain).ToArray();

        return plainTextParts.Length == 0
            ? null
            : string.Join('\n', plainTextParts.Select(part => part.Text));
    }

    /// <summary>Derives text from the HTML body parts, which is only reached when no plain-text alternative exists.</summary>
    private static string? DeriveTextFromHtmlBody(IReadOnlyList<TextPart> bodyTextParts)
    {
        var htmlParts = bodyTextParts.Where(part => part.IsHtml).ToArray();

        return htmlParts.Length == 0
            ? null
            : string.Join('\n', htmlParts.Select(part => HtmlBodyTextReader.ReadDisplayedText(part.Text)));
    }

    /// <summary>Reduces a body to the characters an index and a reader can both carry.</summary>
    /// <remarks>
    /// Line endings are unified so the trimming rules see one line structure whichever platform wrote the message.
    /// Control characters other than the line break and the tab are removed rather than kept: no body displays them,
    /// PostgreSQL rejects a null byte in a text value outright, and a message could otherwise place one in the middle
    /// of a word and make it unmatchable by any query.
    /// </remarks>
    private static string NormalizeForIndexing(string bodyText)
    {
        var unifiedLineEndings = bodyText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var normalized = new StringBuilder(unifiedLineEndings.Length);
        foreach (var character in unifiedLineEndings)
        {
            if (!char.IsControl(character) || character is '\n' or '\t')
            {
                normalized.Append(character);
            }
        }

        return normalized.ToString().Trim();
    }

    /// <summary>Cuts an over-long body at a boundary that leaves it a valid string.</summary>
    /// <remarks>
    /// What is cut is lost to search rather than lost outright, because the raw MIME the text came from stays stored
    /// beside it and a later design can re-derive from it under a larger bound.
    /// </remarks>
    private static string BoundLength(string bodyText, int maxCharacters) =>
        MailTextBounds.TruncateAtTextElementBoundary(bodyText, maxCharacters).TrimEnd();
}
