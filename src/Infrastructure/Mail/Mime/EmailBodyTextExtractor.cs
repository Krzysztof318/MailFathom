// Copyright © 2026 Krzysztof Kasprowicz

using System.Text;
using MailMcp.Application.Emails;
using MailMcp.Domain.Emails;
using MimeKit;

namespace MailMcp.Infrastructure.Mail.Mime;

/// <summary>Derives the searchable text of one message from the parts its structure resolved as the body.</summary>
/// <remarks>
/// <para>
/// A genuine <c>text/plain</c> part is preferred over every HTML alternative, because it is what the sender wrote
/// rather than a reading of how it was displayed. HTML is used only when the message offered no plain-text alternative,
/// and the result is marked lossy so nothing downstream treats the two as the same evidence.
/// </para>
/// <para>
/// The character bound is applied while the text is accumulated rather than after it exists, so a message far below
/// <c>MaxRawMimeBytes</c> but far above the text bound costs the bound rather than the body. One copy of each body part
/// is still decoded by the MIME library before this sees it, which is what <c>MaxRawMimeBytes</c> bounds.
/// </para>
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
        //
        // The body-specific marker is read rather than the summary's, which also answers for an encrypted attachment.
        // A readable message that forwards an encrypted one would otherwise have the body its author wrote discarded.
        if (classification.BodyIsEncrypted)
        {
            return ExtractedEmailText.EncryptedBody;
        }

        if (ReadPlainTextBody(classification.BodyTextParts, maxCharacters) is { } plainText)
        {
            return Build(plainText, maxCharacters, ExtractedEmailText.FromPlainTextBody);
        }

        if (DeriveTextFromHtmlBody(classification.BodyTextParts, maxCharacters) is { } derivedText)
        {
            return Build(derivedText, maxCharacters, ExtractedEmailText.DerivedFromHtmlBody);
        }

        return ExtractedEmailText.NoTextualBody;
    }

    /// <summary>Turns one source's accumulated text into the pair the index and a later reader each need.</summary>
    private static ExtractedEmailText Build(
        string boundedText,
        int maxCharacters,
        Func<string, string, ExtractedEmailText> describe)
    {
        // The accumulation already stopped at the bound, so this only cuts back to a text-element boundary.
        var originalText = MailTextBounds.TruncateAtTextElementBoundary(boundedText, maxCharacters).TrimEnd();
        if (originalText.Length == 0)
        {
            return ExtractedEmailText.NoTextualBody;
        }

        return describe(originalText, QuotedHistoryTrimmer.Trim(originalText));
    }

    /// <summary>Reads the plain-text body, joining the parts when a message resolved several as its body.</summary>
    private static string? ReadPlainTextBody(IReadOnlyList<TextPart> bodyTextParts, int maxCharacters)
    {
        var plainTextParts = bodyTextParts.Where(part => part.IsPlain).ToArray();
        if (plainTextParts.Length == 0)
        {
            return null;
        }

        var body = new StringBuilder();
        foreach (var part in plainTextParts)
        {
            if (body.Length > 0 && !MailBodyTextNormalizer.Append(body, "\n", maxCharacters))
            {
                break;
            }

            if (!MailBodyTextNormalizer.Append(body, part.Text, maxCharacters))
            {
                break;
            }
        }

        return body.ToString().Trim();
    }

    /// <summary>Derives text from the HTML body parts, which is only reached when no plain-text alternative exists.</summary>
    private static string? DeriveTextFromHtmlBody(IReadOnlyList<TextPart> bodyTextParts, int maxCharacters)
    {
        var htmlParts = bodyTextParts.Where(part => part.IsHtml).ToArray();
        if (htmlParts.Length == 0)
        {
            return null;
        }

        var body = new StringBuilder();
        foreach (var part in htmlParts)
        {
            if (body.Length > 0 && !MailBodyTextNormalizer.Append(body, "\n", maxCharacters))
            {
                break;
            }

            // The reader stops at the same bound, so the derivation never builds a string proportional to the markup.
            if (!MailBodyTextNormalizer.Append(body, HtmlBodyTextReader.ReadDisplayedText(part.Text, maxCharacters), maxCharacters))
            {
                break;
            }
        }

        return body.ToString().Trim();
    }
}
