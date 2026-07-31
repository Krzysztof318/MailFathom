// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using MailFathom.Application.Emails;
using MailFathom.Domain.Emails;
using MimeKit;
using MimeKit.Text;
using MimeKit.Tnef;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Decides what each part of a message is, in the order that decides it correctly.</summary>
/// <remarks>
/// <para>
/// The rules run as: the cryptographic envelope, then cryptographic leaf parts, then the body branch, then inline
/// resources an HTML body embeds, and everything left over is an attachment. The order is the specification rather than
/// an implementation detail, because several ordinary parts satisfy more than one rule at once — an <c>smime.p7s</c>
/// part arrives with <c>Content-Disposition: attachment</c>, and a <c>text/plain</c> body inside a
/// <c>multipart/mixed</c> is a part with no disposition at all.
/// </para>
/// <para>
/// The walk runs before any inline decision, because whether a part is an embedded resource depends on what the body
/// branch turned out to be: only an HTML part that the walk selected as body can make a <c>cid:</c> reference count.
/// </para>
/// </remarks>
internal sealed partial class MimeAttachmentClassifier
{
    /// <summary>Where RFC 1847 places the detached signature inside a signed container.</summary>
    private const int DetachedSignaturePosition = 1;

    private static readonly string[] CryptographicMediaTypes =
    [
        "application/pkcs7-signature",
        "application/pgp-signature",
        "application/pkcs7-mime",
        "application/pgp-encrypted",
    ];

    private readonly List<MimeEntity> bodyBranchLeaves = [];
    private readonly List<MimeEntity> unclassifiedEntities = [];
    private bool isEncrypted;
    private bool bodyBranchIsEncrypted;
    private bool carriesUnverifiedSignature;
    private bool containsUnexpandedTnefPart;

    /// <summary>Classifies one message's parts, measures the attachments among them, and names its body text parts.</summary>
    /// <param name="message">The parsed message.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>What the message carries besides its body, together with the textual parts that are its body.</returns>
    public static async Task<MimeContentClassification> ClassifyAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        var classifier = new MimeAttachmentClassifier();
        classifier.WalkEntity(message.Body, isInBodyBranch: true);

        return new MimeContentClassification(
            await classifier.SummarizeAsync(cancellationToken),
            [.. classifier.bodyBranchLeaves.OfType<TextPart>()],
            classifier.bodyBranchIsEncrypted);
    }

    [GeneratedRegex("""cid:(?<contentId>[^"'\s>)\\]+)""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ContentIdReference();

    private void WalkEntity(MimeEntity? entity, bool isInBodyBranch)
    {
        if (entity is null)
        {
            return;
        }

        if (entity is Multipart envelope && this.TryWalkCryptographicEnvelope(envelope, isInBodyBranch))
        {
            return;
        }

        if (this.IsCryptographicLeafPart(entity, isInBodyBranch))
        {
            return;
        }

        if (isInBodyBranch)
        {
            this.WalkBodyBranch(entity);

            return;
        }

        if (entity is Multipart container)
        {
            foreach (var child in container)
            {
                this.WalkEntity(child, isInBodyBranch: false);
            }

            return;
        }

        // A nested message/rfc822 is one part and is deliberately not recursed into, so a forwarded thread reports one
        // attachment rather than the attachment count of every message inside it.
        this.unclassifiedEntities.Add(entity);
    }

    /// <summary>Recognizes a cryptographic container from the container itself rather than from what it holds.</summary>
    /// <remarks>
    /// Matching on child media types is what breaks PGP/MIME: the envelope holds an <c>application/pgp-encrypted</c>
    /// control part next to ciphertext that is usually typed <c>application/octet-stream</c>, so a child-driven rule
    /// catches the control part and lets the ciphertext through as an attachment with a file name that does not exist.
    /// </remarks>
    private bool TryWalkCryptographicEnvelope(Multipart envelope, bool isInBodyBranch)
    {
        if (envelope.ContentType.IsMimeType("multipart", "encrypted") && DeclaresSecurityProtocol(envelope))
        {
            this.MarkEncrypted(isInBodyBranch);

            return true;
        }

        if (!envelope.ContentType.IsMimeType("multipart", "signed") || !DeclaresSecurityProtocol(envelope))
        {
            return false;
        }

        this.carriesUnverifiedSignature = true;

        // RFC 1847 gives a signed container exactly two children: the signed content, classified as though the
        // envelope were not there, and the detached signature, classified not at all. A message carrying more is
        // malformed, and its extra children are still walked rather than dropped — silently discarding a third part
        // would take a real file out of the attachment summary and out of every filter built on it.
        foreach (var (child, position) in envelope.Select((child, position) => (child, position)))
        {
            if (position == DetachedSignaturePosition)
            {
                continue;
            }

            this.WalkEntity(child, isInBodyBranch && position == 0);
        }

        return true;
    }

    /// <summary>Records encrypted content, and separately whether it was the body that arrived encrypted.</summary>
    /// <remarks>
    /// The two answer different questions and only look the same on the common message. The summary marker says the
    /// message carries encrypted content somewhere, which is what a filter over the mailbox asks. Body text extraction
    /// asks something narrower: whether *this message's own body* is unreadable. A readable message that forwards an
    /// encrypted one as an <c>application/pkcs7-mime</c> attachment satisfies the first and not the second, and reading
    /// the summary marker there would discard a body a person wrote and can see.
    /// </remarks>
    private void MarkEncrypted(bool isInBodyBranch)
    {
        this.isEncrypted = true;
        this.bodyBranchIsEncrypted |= isInBodyBranch;
    }

    /// <summary>Decides whether a container declared the security protocol that makes it an envelope.</summary>
    /// <remarks>
    /// RFC 1847 requires the <c>protocol</c> parameter on both container types, and these rules read the container
    /// precisely because the container is what states the parts' role. A container that names no protocol has stated
    /// nothing, so honoring it would let a bare <c>Content-Type: multipart/encrypted</c> line with no cryptography
    /// behind it take an ordinary file out of the attachment summary and out of every filter built on it. Such a
    /// container is classified as the ordinary multipart it turned out to be, which keeps its children visible; a
    /// genuine signature or ciphertext part among them is still caught by the cryptographic leaf rule.
    /// </remarks>
    private static bool DeclaresSecurityProtocol(Multipart envelope) =>
        !string.IsNullOrWhiteSpace(envelope.ContentType.Parameters["protocol"]);

    /// <summary>Recognizes a cryptographic leaf before any disposition is read.</summary>
    /// <remarks>
    /// This precedes disposition on purpose: an <c>smime.p7s</c> part almost always declares itself an attachment, so a
    /// rule that honored disposition first would count exactly the part these rules exist to stop counting.
    /// </remarks>
    private bool IsCryptographicLeafPart(MimeEntity entity, bool isInBodyBranch)
    {
        if (!CryptographicMediaTypes.Contains(entity.ContentType.MimeType, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (entity.ContentType.IsMimeType("application", "pkcs7-signature")
            || entity.ContentType.IsMimeType("application", "pgp-signature"))
        {
            this.carriesUnverifiedSignature = true;
        }

        this.MarkOpaqueSmimeBody(entity, isInBodyBranch);

        return true;
    }

    /// <summary>Marks what an opaque S/MIME part did to the body it replaced.</summary>
    /// <remarks>
    /// An <c>application/pkcs7-mime</c> part is the whole message rather than a file beside it, and its
    /// <c>smime-type</c> parameter says which. Without this, an enveloped message would be recorded as one with no
    /// parts and no explanation — indistinguishable from an empty message, which is exactly the gap the encrypted
    /// marker exists to close for the <c>multipart/encrypted</c> shape.
    /// </remarks>
    private void MarkOpaqueSmimeBody(MimeEntity entity, bool isInBodyBranch)
    {
        if (!entity.ContentType.IsMimeType("application", "pkcs7-mime"))
        {
            return;
        }

        // A MIME parameter value is not case-normalized by the parser, so the comparison is.
        var smimeType = entity.ContentType.Parameters["smime-type"];
        if (string.Equals(smimeType, "enveloped-data", StringComparison.OrdinalIgnoreCase)
            || string.Equals(smimeType, "authEnveloped-data", StringComparison.OrdinalIgnoreCase))
        {
            this.MarkEncrypted(isInBodyBranch);
        }
        else if (string.Equals(smimeType, "signed-data", StringComparison.OrdinalIgnoreCase))
        {
            this.carriesUnverifiedSignature = true;
        }
    }

    /// <summary>Resolves the body branch recursively, which is what keeps an ordinary attachment count correct.</summary>
    /// <remarks>
    /// In a <c>multipart/mixed</c> the body is the first child, resolved again by these same rules; in a
    /// <c>multipart/related</c> it is the root part the <c>start</c> parameter names, or the first child when the
    /// parameter is absent; in a <c>multipart/alternative</c> every member is a representation of the body. Resolving
    /// only at the message root would classify the <c>text/plain</c> body of a mixed message as an attachment.
    /// </remarks>
    private void WalkBodyBranch(MimeEntity entity)
    {
        if (entity is not Multipart multipart)
        {
            this.bodyBranchLeaves.Add(entity);

            return;
        }

        if (multipart.ContentType.IsMimeType("multipart", "alternative"))
        {
            foreach (var alternative in multipart)
            {
                this.WalkEntity(alternative, isInBodyBranch: true);
            }

            return;
        }

        var bodyChild = multipart.ContentType.IsMimeType("multipart", "related")
            ? FindRelatedRootPart(multipart)
            : multipart.FirstOrDefault();

        foreach (var child in multipart)
        {
            this.WalkEntity(child, isInBodyBranch: ReferenceEquals(child, bodyChild));
        }
    }

    private static MimeEntity? FindRelatedRootPart(Multipart multipart)
    {
        var startContentId = NormalizeContentId(multipart.ContentType.Parameters["start"]);
        if (startContentId is null)
        {
            return multipart.FirstOrDefault();
        }

        return multipart.FirstOrDefault(child =>
            string.Equals(NormalizeContentId(child.ContentId), startContentId, StringComparison.OrdinalIgnoreCase))
            ?? multipart.FirstOrDefault();
    }

    private async Task<EmailAttachmentSummary> SummarizeAsync(CancellationToken cancellationToken)
    {
        var embeddedContentIds = this.FindContentIdsTheHtmlBodyReferences();
        var attachments = new List<ExtractedEmailAttachment>();
        var inlineResourceCount = 0;

        foreach (var entity in this.unclassifiedEntities)
        {
            if (IsEmbeddedResource(entity, embeddedContentIds))
            {
                inlineResourceCount++;

                continue;
            }

            if (entity is TnefPart)
            {
                this.containsUnexpandedTnefPart = true;
            }

            attachments.Add(await DescribeAttachmentAsync(entity, cancellationToken));
        }

        return EmailAttachmentSummary.Create(
            attachments,
            inlineResourceCount,
            this.isEncrypted,
            this.carriesUnverifiedSignature,
            this.containsUnexpandedTnefPart);
    }

    /// <summary>Collects the identifiers an HTML body embeds, which is what makes a part a resource rather than a file.</summary>
    private HashSet<string> FindContentIdsTheHtmlBodyReferences()
    {
        var referencedContentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var htmlBody in this.bodyBranchLeaves.OfType<TextPart>().Where(part => part.IsHtml))
        {
            CollectContentIdsReferencedBy(htmlBody.Text, referencedContentIds);
        }

        return referencedContentIds;
    }

    /// <summary>Reads the <c>cid:</c> URLs a body points at, rather than every occurrence of that text in its source.</summary>
    /// <remarks>
    /// A reference counts only where a renderer would follow it: an attribute value, or the style sheet a
    /// <c>&lt;style&gt;</c> element carries. Scanning the whole source instead would let a crafted message hide a real
    /// file by naming its <c>Content-ID</c> in visible text, in a comment, or in script data — the part would be
    /// recorded as an embedded resource and disappear from the attachment summary and from every filter built on it.
    /// The body is tokenized rather than pattern-matched because only a tokenizer can tell those contexts apart.
    /// </remarks>
    private static void CollectContentIdsReferencedBy(string html, HashSet<string> referencedContentIds)
    {
        using var htmlReader = new StringReader(html);
        var tokenizer = new HtmlTokenizer(htmlReader);
        var isInsideStyleElement = false;

        while (tokenizer.ReadNextToken(out var token))
        {
            if (token is HtmlTagToken tag)
            {
                // A style element holds character data rather than markup, so its start tag is the only thing that can
                // announce the style sheet the next data token carries.
                isInsideStyleElement = tag.Id == HtmlTagId.Style && !tag.IsEndTag && !tag.IsEmptyElement;

                referencedContentIds.UnionWith(
                    tag.Attributes.SelectMany(attribute => ReadContentIdReferences(attribute.Value)));

                continue;
            }

            if (isInsideStyleElement && token is HtmlDataToken styleSheet)
            {
                referencedContentIds.UnionWith(ReadContentIdReferences(styleSheet.Data));
            }
        }
    }

    /// <summary>Reads every identifier one reference context names.</summary>
    private static IEnumerable<string> ReadContentIdReferences(string? referenceContext) =>
        referenceContext is null
            ? []
            : ContentIdReference()
                .Matches(referenceContext)

                // RFC 2392 builds a cid URL by removing the angle brackets and percent-encoding whatever cannot appear
                // literally in a URL, so "<logo/dark@example.test>" is written "cid:logo%2Fdark@example.test". Comparing
                // the encoded form would miss the part and report an embedded resource as a file.
                .Select(reference => DecodeContentIdReference(reference.Groups["contentId"].Value));

    /// <summary>Decides whether a part is a resource the body embeds.</summary>
    /// <remarks>
    /// A missing <c>Content-Disposition</c> counts as inline, because senders routinely omit the header on embedded
    /// images and requiring it would make the classification depend on which client wrote the message. An explicit
    /// <c>attachment</c> disposition wins, because there the sender has said what the part is.
    /// </remarks>
    private static bool IsEmbeddedResource(MimeEntity entity, HashSet<string> embeddedContentIds)
    {
        var disposition = entity.ContentDisposition?.Disposition;
        if (string.Equals(disposition, ContentDisposition.Attachment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var contentId = NormalizeContentId(entity.ContentId);

        return contentId is not null && embeddedContentIds.Contains(contentId);
    }

    private static async Task<ExtractedEmailAttachment> DescribeAttachmentAsync(
        MimeEntity entity,
        CancellationToken cancellationToken)
    {
        var fileName = AttachmentFileName.TryNormalize(ReadDeclaredFileName(entity), out var normalizedFileName)
            ? normalizedFileName
            : (AttachmentFileName?)null;

        return new ExtractedEmailAttachment(
            fileName,
            entity.ContentType.MimeType,
            await MeasureDecodedOctetsAsync(entity, cancellationToken));
    }

    /// <summary>Reads the name the message declared, already decoded from its RFC 2047 or RFC 2231 form.</summary>
    private static string? ReadDeclaredFileName(MimeEntity entity) => entity switch
    {
        MimePart part => part.FileName,
        _ => entity.ContentDisposition?.FileName ?? entity.ContentType.Name,
    };

    /// <summary>Measures a part by streaming it through a counter, so no attachment content is ever retained.</summary>
    private static async Task<long> MeasureDecodedOctetsAsync(MimeEntity entity, CancellationToken cancellationToken)
    {
        await using var counter = new DecodedOctetCountingStream();

        switch (entity)
        {
            case MimePart { Content: { } content }:
                await content.DecodeToAsync(counter, cancellationToken);
                break;

            case MessagePart { Message: { } nestedMessage }:
                await nestedMessage.WriteToAsync(counter, cancellationToken);
                break;

            default:
                return 0;
        }

        return counter.WrittenOctets;
    }

    /// <summary>Turns a <c>cid:</c> URL body back into the identifier a <c>Content-ID</c> header carries.</summary>
    /// <remarks>A reference whose escaping is malformed is compared as written rather than discarded.</remarks>
    private static string DecodeContentIdReference(string reference)
    {
        try
        {
            return Uri.UnescapeDataString(reference);
        }
        catch (UriFormatException)
        {
            return reference;
        }
    }

    private static string? NormalizeContentId(string? contentId)
    {
        if (contentId is null)
        {
            return null;
        }

        var trimmed = contentId.Trim().Trim('<', '>').Trim();

        return trimmed.Length == 0 ? null : trimmed;
    }
}
