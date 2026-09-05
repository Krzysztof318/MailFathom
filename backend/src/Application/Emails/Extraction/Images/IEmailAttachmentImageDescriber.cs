// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction.Images;

/// <summary>Turns one image attachment into text, or says why it stayed a picture.</summary>
/// <remarks>
/// <para>
/// An image has no text to extract, so it reaches search only if something first writes down what it shows. That is
/// what this does, and it is the whole of what it does: it composes no chunk, embeds nothing, stores nothing, and
/// decides nothing about where a described message ranks. What it produces is a string, which from that point is
/// attachment-derived text like any other, described by
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>.
/// </para>
/// <para>
/// **It runs in the background and never on a read path.** Describing costs a call to a chat provider, so a caller that
/// reached it while answering an MCP tool or a client request would have put a remote party's latency, rate limit, and
/// outage between a person and their own mail. What calls this is the work that runs after a message is stored.
/// </para>
/// <para>
/// **Every call sends the attachment's octets to a third party.** That is a disclosure of mail content at least as
/// large as sending message text for embedding — a photograph of a document discloses the document — and it is the one
/// egress on this path that no content scan covers, because
/// <see cref="SensitiveContent.Egress.SensitiveContentEgressGuard" /> detects regions in a string and an image is not
/// one. What governs it is the deployment's own activation and nothing else, which is why an instance that has not
/// turned image description on refuses every call with
/// <see cref="ImageDescriptionRefusal.NotActivated" /> rather than describing anything.
/// </para>
/// <para>
/// The octets arrive as a stream the caller owns, and are read once. Nothing here seeks, so a forward-only stream over
/// a stored part is what this expects; an attachment larger than one request may send is refused after the ceiling is
/// reached rather than after the whole of it is in memory.
/// </para>
/// </remarks>
public interface IEmailAttachmentImageDescriber
{
    /// <summary>Describes what one image attachment shows.</summary>
    /// <param name="declaredMediaType">The media type the part declared, which is the sender's claim rather than a fact about the octets.</param>
    /// <param name="content">The attachment's decoded octets, read forward from the current position.</param>
    /// <param name="cancellationToken">Cancels the read and the provider call.</param>
    /// <returns>The description, or the reason there is none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancelled, which is never reported as a refusal.</exception>
    Task<ImageAttachmentDescription> DescribeAsync(
        string declaredMediaType,
        Stream content,
        CancellationToken cancellationToken);
}
