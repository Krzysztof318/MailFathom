// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Images;

namespace MailFathom.AI.Descriptions;

/// <summary>Describes nothing, on a deployment that has not turned image description on.</summary>
/// <remarks>
/// <para>
/// The default state of an instance, and what every deployment gets until an operator says otherwise. It exists as a
/// registration rather than as an absent one because the answer a caller needs is a reason it can record against the
/// attachment, and a missing service would have made every caller carry a null check and decide for itself what an
/// absence meant.
/// </para>
/// <para>
/// Nothing is read and nothing is sent. The stream is not touched at all, so an instance in this state cannot disclose
/// an attachment however often it is asked, which is the property the switch exists to give and would not have if the
/// refusal came after the octets had been buffered.
/// </para>
/// </remarks>
internal sealed class InactiveImageAttachmentDescriber : IEmailAttachmentImageDescriber
{
    /// <summary>The one instance, since it holds nothing and answers everything the same way.</summary>
    internal static readonly InactiveImageAttachmentDescriber Instance = new();

    private InactiveImageAttachmentDescriber()
    {
    }

    /// <inheritdoc />
    public Task<ImageAttachmentDescription> DescribeAsync(
        string declaredMediaType,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declaredMediaType);
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ImageAttachmentDescription.Refused(ImageDescriptionRefusal.NotActivated));
    }
}
