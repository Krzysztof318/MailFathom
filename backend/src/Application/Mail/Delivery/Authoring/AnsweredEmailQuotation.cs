// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Net;
using System.Text;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Authoring;

/// <summary>Builds the body of an answer: what its author wrote, then the message they were answering.</summary>
/// <remarks>
/// <para>
/// The quoted history is produced from the stored copy that was already read for this answer, and never from a second
/// retrieval. Reaching a mail server to quote a message would set the remote <c>\Seen</c> flag on mail somebody is only
/// answering, which is the invariant every read in this system is written under.
/// </para>
/// <para>
/// The author's own words are never cut. Where the two together exceed what this deployment composes, the quotation is
/// what gives way — a shortened history is a message somebody can still read and act on, and a shortened first
/// paragraph is words the author is never told were dropped. An author who writes past the bound alone is refused by
/// the composition rather than silently trimmed here.
/// </para>
/// </remarks>
internal static class AnsweredEmailQuotation
{
    /// <summary>The characters kept aside for the attribution line, the quote markers, and the wrapper around them.</summary>
    /// <remarks>
    /// The quoted text is bounded while the message is rendered, which is what keeps this from reading a whole long
    /// message to throw most of it away. What that bound cannot know is the cost of quoting itself — the attribution
    /// line, two characters in front of every plain-text line, and one wrapper element in the markup — so the allowance
    /// handed to the rendering is reduced by this much. It comfortably covers
    /// <see cref="MaximumAttributionCharacters" /> plus <see cref="QuotationMarkupCharacters" />, which is what lets the
    /// markup alternative be composed without a cut: a bound applied to markup after it was sanitized would hand back an
    /// element somebody else opened and this system closed nowhere.
    /// </remarks>
    public const int QuotationOverheadReserve = 320;

    /// <summary>The greatest number of characters the attribution line carries, in either body it is written into.</summary>
    /// <remarks>
    /// It is bounded because a display name is not: the name is whatever a sender wrote, so an attribution built from
    /// one is untrusted input whose length would otherwise decide how much of the answer's own bound is left. What the
    /// bound cuts is the sender's name rather than the sentence, so the line still says what it is.
    /// </remarks>
    public const int MaximumAttributionCharacters = 200;

    /// <summary>What the attribution line's own wording and timestamp cost, which the sender's name is bounded around.</summary>
    private const int AttributionSentenceReserve = 64;

    /// <summary>What the elements this system writes around a quotation cost in the markup alternative.</summary>
    /// <remarks>
    /// The paragraph holding the attribution, the block holding the quotation, and the preformatted element a message
    /// without markup of its own is quoted in come to forty-three characters; the rest is slack, because this is one of
    /// the two halves <see cref="QuotationOverheadReserve" /> was sized to cover and being a few characters generous
    /// costs nothing a reader can see.
    /// </remarks>
    private const int QuotationMarkupCharacters = 64;

    /// <summary>The blank line between what an author wrote and the message they were answering.</summary>
    private const string BodySeparator = "\n\n";

    /// <summary>Writes the line naming who wrote the answered message and when.</summary>
    /// <param name="headers">The answered message's own headers.</param>
    /// <param name="act">Which answer is being authored.</param>
    /// <returns>The attribution line, which never ends in a line break.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="headers" /> is <see langword="null" />.</exception>
    public static string Attribution(EmailContentHeaders headers, AuthoredResponseAct act)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var author = AuthorOf(headers);
        var sentAt = headers.SentAt is { } moment
            ? moment.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
            : null;

        return (act, author, sentAt) switch
        {
            (AuthoredResponseAct.Forward, null, null) => "Forwarded message:",
            (AuthoredResponseAct.Forward, null, _) =>
                string.Create(CultureInfo.InvariantCulture, $"Forwarded message, sent {sentAt}:"),
            (AuthoredResponseAct.Forward, _, null) =>
                string.Create(CultureInfo.InvariantCulture, $"Forwarded message from {author}:"),
            (AuthoredResponseAct.Forward, _, _) =>
                string.Create(CultureInfo.InvariantCulture, $"Forwarded message from {author}, sent {sentAt}:"),
            (_, null, null) => "The answered message read:",
            (_, null, _) => string.Create(CultureInfo.InvariantCulture, $"On {sentAt}, the answered message read:"),
            (_, _, null) => string.Create(CultureInfo.InvariantCulture, $"{author} wrote:"),
            (_, _, _) => string.Create(CultureInfo.InvariantCulture, $"On {sentAt}, {author} wrote:"),
        };
    }

    /// <summary>Writes the plain-text body of an answer.</summary>
    /// <param name="authored">The text the author wrote.</param>
    /// <param name="attribution">The line naming who is being answered.</param>
    /// <param name="quoted">The answered message's plain text, already bounded by the rendering that produced it.</param>
    /// <param name="maxCharacters">The greatest number of characters this deployment composes a body from.</param>
    /// <returns>The author's text with the quoted message beneath it, or their text alone when nothing fits beneath it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authored" />, <paramref name="attribution" />, or <paramref name="quoted" /> is <see langword="null" />.</exception>
    public static string PlainTextBody(string authored, string attribution, string quoted, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(authored);
        ArgumentNullException.ThrowIfNull(attribution);
        ArgumentNullException.ThrowIfNull(quoted);

        var allowance = maxCharacters - authored.Length - BodySeparator.Length;
        if (allowance <= attribution.Length)
        {
            return authored;
        }

        var quotation = new StringBuilder(attribution);

        foreach (var line in quoted.ReplaceLineEndings("\n").Split('\n'))
        {
            quotation.Append('\n').Append("> ").Append(line);
        }

        return authored
            + BodySeparator
            + MailTextBounds.TruncateAtTextElementBoundary(quotation.ToString(), allowance);
    }

    /// <summary>Writes the HTML alternative of an answer, when its author wrote one.</summary>
    /// <param name="authored">The markup the author wrote.</param>
    /// <param name="attribution">The line naming who is being answered.</param>
    /// <param name="quotedHtml">The answered message's sanitized markup, or <see langword="null" /> when it had none.</param>
    /// <param name="quotedText">The answered message's plain text, quoted when it carried no markup.</param>
    /// <param name="maxCharacters">The greatest number of characters this deployment composes a body from.</param>
    /// <returns>The author's markup with the quoted message beneath it, or their markup alone when nothing fits beneath it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authored" />, <paramref name="attribution" />, or <paramref name="quotedText" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The quoted markup arrives sanitized by the rendering that produced it, which is the same markup a reader of the
    /// answered message is shown. Nothing here sanitizes again and nothing here parses: the quotation is wrapped in one
    /// element this system writes, and the bound it was rendered under is what keeps the result proportionate.
    /// </para>
    /// <para>
    /// A message that carried no markup is quoted as its text, encoded rather than inserted. Text a sender wrote is not
    /// markup, and putting it into an HTML body unencoded would let a message with an angle bracket in it decide the
    /// structure of somebody else's reply. That encoding is an expansion, and it is applied to text somebody else
    /// wrote, so the cut falls on what the encoding produces rather than on what it was handed: text bounded before it
    /// is encoded lands several times past the bound afterwards.
    /// </para>
    /// </remarks>
    public static string HtmlBody(
        string authored,
        string attribution,
        string? quotedHtml,
        string quotedText,
        int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(authored);
        ArgumentNullException.ThrowIfNull(attribution);
        ArgumentNullException.ThrowIfNull(quotedText);

        var encodedAttribution = WebUtility.HtmlEncode(attribution);

        if (quotedHtml is { } sanitizedMarkup)
        {
            return Quoting(authored, encodedAttribution, sanitizedMarkup);
        }

        var allowance = maxCharacters - authored.Length - encodedAttribution.Length - QuotationMarkupCharacters;
        if (allowance <= 0)
        {
            return authored;
        }

        var quotedWithinBound = WebUtility.HtmlEncode(BoundedByEncodedCost(quotedText, allowance));

        return Quoting(
            authored,
            encodedAttribution,
            string.Create(CultureInfo.InvariantCulture, $"<pre>{quotedWithinBound}</pre>"));
    }

    /// <summary>Writes the author's markup with a quotation beneath it.</summary>
    private static string Quoting(string authored, string encodedAttribution, string quotation) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{authored}<p>{encodedAttribution}</p><blockquote>{quotation}</blockquote>");

    /// <summary>Cuts text to the longest prefix whose markup encoding fits an allowance.</summary>
    /// <remarks>
    /// Encoding for markup expands what it is given: an ampersand becomes five characters, a quotation mark six, and
    /// anything outside ASCII a numeric reference longer still. So a bound measured on the text a sender wrote says
    /// nothing about the body it ends up in, and text this system read as bounded composes to several times the
    /// deployment's own limit. Cutting the encoded string instead would cut through the middle of a reference and leave
    /// a reader the fragment <c>&amp;am</c>, which is why the cut is decided element by element on what each one costs
    /// once encoded. What comes back is the text itself rather than its encoding, because the same bound applies to the
    /// plain-text body, where nothing is encoded at all.
    /// </remarks>
    private static string BoundedByEncodedCost(string text, int maxCharacters)
    {
        var encodedCost = 0;
        var boundedLength = 0;
        var textElements = StringInfo.GetTextElementEnumerator(text);

        while (textElements.MoveNext())
        {
            var element = (string)textElements.Current;

            encodedCost += WebUtility.HtmlEncode(element).Length;
            if (encodedCost > maxCharacters)
            {
                break;
            }

            boundedLength = textElements.ElementIndex + element.Length;
        }

        return text[..boundedLength];
    }

    /// <summary>Names whoever wrote the answered message, as a reader of the quotation sees them.</summary>
    /// <remarks>
    /// <para>
    /// The <c>From</c> header is the author, and the <c>Sender</c> header names whoever submitted the message on their
    /// behalf. Attribution is about who wrote it, so the second is used only where a message carried no first.
    /// </para>
    /// <para>
    /// The name is bounded by what it costs in the markup alternative rather than by its own length, because the same
    /// attribution is written into both bodies and only one of them encodes it. A name written entirely out of
    /// ampersands is five times its own length once it is markup, so bounding the name as the sender wrote it would
    /// leave <see cref="QuotationOverheadReserve" /> truthful about the plain-text body and wrong about the other.
    /// </para>
    /// </remarks>
    private static string? AuthorOf(EmailContentHeaders headers)
    {
        var author = headers.Participants.FirstOrDefault(participant => participant.Role == EmailAddressRole.From)
            ?? headers.Participants.FirstOrDefault(participant => participant.Role == EmailAddressRole.Sender);

        if (author is null)
        {
            return null;
        }

        var described = author.Address.DisplayName is { } displayName
            ? string.Create(CultureInfo.InvariantCulture, $"{displayName} <{author.Address.Address}>")
            : author.Address.Address;

        return BoundedByEncodedCost(described, MaximumAttributionCharacters - AttributionSentenceReserve);
    }
}
