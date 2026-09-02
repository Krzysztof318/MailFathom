// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Screening;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Reads the subject and the body representations back out of a message this deployment composed.</summary>
/// <remarks>
/// <para>
/// The markup is returned as it stands rather than sanitized, which is the whole difference between this reader and
/// the one that renders stored mail for a person. A sanitizer answers what a browser may be allowed to render; a
/// screen asks what will be transmitted, and an attribute, a comment, or a style block a sanitizer would strip leaves
/// in the message exactly like the text beside it.
/// </para>
/// <para>
/// No structural limit is applied for the same reason no parse failure is modelled: the bytes were composed by this
/// deployment's own composer moments earlier, against bounds the composition already enforced, so a message that
/// declares a thousand parts here is a defect in that composer rather than hostile input to survive. What a parse
/// failure produces is the parser's own exception, travelling as the defect it is.
/// </para>
/// <para>
/// Nothing is materialized beyond the two body parts. <see cref="MimeMessage.TextBody" /> and
/// <see cref="MimeMessage.HtmlBody" /> select one part apiece and decode that one, so a message carrying attachments
/// costs the same to read as one carrying none.
/// </para>
/// </remarks>
internal sealed class MimeKitOutgoingMailTextReader : IOutgoingMailTextReader
{
    /// <inheritdoc />
    public async Task<OutgoingMailText> ReadAsync(
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        if (rawMime.IsEmpty)
        {
            throw new ArgumentException(
                "An outgoing message is screened from the MIME it will be transmitted as.",
                nameof(rawMime));
        }

        await using var parsingPass = RawMimeStream.Open(rawMime);

        using var message = await MimeMessage.LoadAsync(
            ParserOptions.Default,
            parsingPass,
            persistent: true,
            cancellationToken);

        // Each of the three is empty rather than absent where the message carries none, except the markup, which stays
        // absent: a message with no HTML alternative and a message whose HTML alternative is empty are the same thing
        // to a screen, and the composed value list drops both either way.
        return new OutgoingMailText(
            message.Subject ?? string.Empty,
            message.TextBody ?? string.Empty,
            message.HtmlBody);
    }
}
