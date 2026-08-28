// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using AngleSharp.Html.Parser;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Domain.Emails;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Produces the document a reading pane draws, from the HTML the message displays.</summary>
/// <remarks>
/// <para>
/// One parse of one body, and what comes out of it is a typed tree rather than markup. That is what keeps a second
/// parser out of the picture entirely: the client links no HTML parser, so nothing downstream can disagree with this
/// parse about what the document is, and the mutation attacks built out of two parsers reading one string have no
/// second reader to work with.
/// </para>
/// <para>
/// The markup is cut before it is parsed, exactly as the sanitized representation's is, so a body far beyond the bound
/// costs the bound rather than its own size. The parser closes what the cut left open, which is what makes cutting the
/// source rather than the result safe.
/// </para>
/// <para>
/// Nothing here reaches the network. The parser is built with no requester, the pictures are resolved out of the
/// message's own parts, and a remote reference is counted and dropped rather than followed.
/// </para>
/// </remarks>
internal static class MailBodyProjection
{
    /// <summary>Produces the document for one message.</summary>
    /// <param name="message">The parsed message, which the pictures are resolved out of.</param>
    /// <param name="htmlParts">The HTML the message displays, in the order the walk found the parts.</param>
    /// <param name="retainRemoteImages">Whether the reader asked for this message's remote pictures.</param>
    /// <param name="maximumCharacters">How much markup is reduced before the source is cut.</param>
    /// <param name="maximumImageOctets">How many octets of its own pictures this document may still inline.</param>
    /// <param name="cancellationToken">Cancels the decode of the message's own pictures.</param>
    /// <returns>The document, or the fact that the pane reads this message as its plain text instead.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either reference argument is <see langword="null" />.</exception>
    internal static async Task<MailDocument> ProduceAsync(
        MimeMessage message,
        IReadOnlyList<string> htmlParts,
        bool retainRemoteImages,
        int maximumCharacters,
        int maximumImageOctets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(htmlParts);

        if (htmlParts.Count == 0)
        {
            return MailDocument.Refused(MailDocumentRefusal.NoHtmlPart);
        }

        // What the call has left narrows the document's own octet bound rather than sitting beside it, so the one
        // number governs both halves: how much is decoded out of the message, and how much of it the answer carries.
        var bounds = MailDocumentBounds.Default with
        {
            MaximumInlineImageOctetsPerDocument =
                Math.Min(MailDocumentBounds.Default.MaximumInlineImageOctetsPerDocument, maximumImageOctets),
        };

        var joined = string.Join('\n', htmlParts);
        var source = MailTextBounds.TruncateAtTextElementBoundary(joined, maximumCharacters);

        // The default configuration builds a parser with no requester and no script engine, so parsing fetches nothing
        // and runs nothing. Naming that here rather than trusting it is the point: a configuration that gained either
        // would turn this line into the one place mail could reach the network from.
        var parsed = new HtmlParser().ParseDocument(source);

        if (parsed.Body is not { } body)
        {
            return MailDocument.Refused(MailDocumentRefusal.ReductionFailed);
        }

        // The parse comes first so the pictures the body actually names are known before any part is decoded. Resolving
        // in MIME order without that would spend the document's octet budget on an attached photograph the body never
        // draws — which clients routinely give a content identifier — and leave the small logo it does draw refused.
        var inlineImages = await MailInlineImages.ResolveAsync(
            message,
            MailPictureReferences.NamedBy(body),
            bounds.MaximumInlineImages,
            bounds.MaximumInlineImageOctets,
            bounds.MaximumInlineImageOctetsPerDocument,
            cancellationToken);

        var reducer = new MailBodyReducer(bounds, inlineImages, retainRemoteImages);

        // The cut happens before the parse, so the reducer never meets the words it removed and would report a whole
        // document. Telling it here is what keeps every way a body is shortened readable as one flag.
        if (source.Length < joined.Length)
        {
            reducer.NoteTruncated();
        }

        return reducer.Reduce(body);
    }
}
