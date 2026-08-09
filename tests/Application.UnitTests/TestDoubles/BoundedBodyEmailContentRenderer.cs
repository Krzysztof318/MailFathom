// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Renders one fixed body and a fixed set of attachments, honouring the bounds the way the real adapter does.</summary>
/// <remarks>
/// A substitute returning a fixed rendering cannot show what a read's budgets do, because the whole effect is that later
/// emails receive a smaller allowance than earlier ones. This fake applies the allowances it is given and reports which
/// bound produced each one, so a test can assert the arithmetic the use case carries between emails.
/// </remarks>
/// <param name="plainTextBody">The plain-text body every render returns, before bounding.</param>
/// <param name="htmlBody">The HTML body a render returns when one was asked for, before bounding.</param>
/// <param name="attachmentOctetCounts">The decoded size of each attachment the rendered message carries.</param>
internal sealed class BoundedBodyEmailContentRenderer(
    string plainTextBody,
    string? htmlBody = null,
    IReadOnlyList<int>? attachmentOctetCounts = null)
    : IEmailContentRenderer
{
    /// <summary>Gets the remaining budget each render was told about, in the order the renders happened.</summary>
    public List<int> ObservedRemainingCharacters { get; } = [];

    /// <summary>Gets the attachment bounds each render was told about, in the order the renders happened.</summary>
    public List<EmailAttachmentContentBounds?> ObservedAttachmentBounds { get; } = [];

    /// <inheritdoc />
    public Task<EmailContentRenderingResult> RenderAsync(
        StoredEmailContent content,
        EmailContentRenderingBounds bounds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        cancellationToken.ThrowIfCancellationRequested();

        this.ObservedRemainingCharacters.Add(bounds.RemainingCharactersForRead);
        this.ObservedAttachmentBounds.Add(bounds.AttachmentContent);

        var plainText = EmailBodyRepresentation.Bounded(
            plainTextBody,
            EmailBodyCharacterAllowance.Of(
                bounds.MaxCharactersPerRepresentation,
                bounds.RemainingCharactersForRead));

        var sanitizedHtml = bounds.IncludeSanitizedHtml && htmlBody is not null
            ? EmailBodyRepresentation.Bounded(
                htmlBody,
                EmailBodyCharacterAllowance.Of(
                    bounds.MaxCharactersPerRepresentation,
                    bounds.RemainingCharactersForRead - plainText.Text.Length))
            : null;

        var attachments = RenderAttachments(bounds.AttachmentContent, attachmentOctetCounts ?? []);


        return Task.FromResult(EmailContentRenderingResult.Rendered(
            new EmailContentRendering(
                new EmailContentHeaders("Subject", SentAt: null, ReceivedAt: null, [], EmailThreadReferences.None),
                plainText,
                sanitizedHtml,
                BodyIsEncrypted: false,
                EmailAttachmentSummary.Create(
                    attachments.Select(attachment => attachment.Description),
                    inlineResourceCount: 0,
                    isEncrypted: false,
                    carriesUnverifiedSignature: false,
                    containsUnexpandedTnefPart: false),
                attachments)));
    }

    /// <summary>Describes one attachment per configured size, keeping the content the allowance permits.</summary>
    /// <remarks>
    /// Every attachment is described whether or not content was asked for, the way the real adapter describes what one
    /// walk of the message found; only the octets depend on the bounds.
    /// </remarks>
    private static List<RenderedEmailAttachment> RenderAttachments(
        EmailAttachmentContentBounds? attachmentContent,
        IReadOnlyList<int> octetCounts)
    {
        var rendered = new List<RenderedEmailAttachment>(octetCounts.Count);
        var remainingOctets = attachmentContent?.RemainingOctetsForRead ?? 0;

        foreach (var octetCount in octetCounts)
        {
            var attachmentContentOfPart = ContentOf(attachmentContent, octetCount, remainingOctets);

            remainingOctets -= attachmentContentOfPart.Octets.Length;
            rendered.Add(new RenderedEmailAttachment(
                new ExtractedEmailAttachment(FileName: null, "application/octet-stream", octetCount),
                attachmentContentOfPart));
        }

        return rendered;
    }

    /// <summary>Decides what one attachment of the given size returns under the bounds the read carried.</summary>
    private static EmailAttachmentContent ContentOf(
        EmailAttachmentContentBounds? attachmentContent,
        int octetCount,
        int remainingOctets)
    {
        if (attachmentContent is null)
        {
            return EmailAttachmentContent.NotRequested;
        }

        var allowance = EmailAttachmentContentAllowance.Of(
            attachmentContent.MaxOctetsPerAttachment,
            remainingOctets);

        return octetCount > allowance.MaxOctets
            ? EmailAttachmentContent.Withheld(allowance.AvailabilityWhenExceeded)
            : EmailAttachmentContent.Returned(new byte[octetCount]);
    }
}
