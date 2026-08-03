// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Renders one fixed body, honouring the bounds it is handed the way the real adapter does.</summary>
/// <remarks>
/// A substitute returning a fixed rendering cannot show what a read's character budget does, because the whole effect is
/// that later emails receive a smaller allowance than earlier ones. This fake applies the allowance it is given and
/// reports which bound produced it, so a test can assert the arithmetic the use case carries between emails.
/// </remarks>
internal sealed class BoundedBodyEmailContentRenderer(string plainTextBody, string? htmlBody = null)
    : IEmailContentRenderer
{
    /// <summary>Gets the remaining budget each render was told about, in the order the renders happened.</summary>
    public List<int> ObservedRemainingCharacters { get; } = [];

    /// <inheritdoc />
    public Task<EmailContentRenderingResult> RenderAsync(
        StoredEmailContent content,
        EmailContentRenderingBounds bounds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        cancellationToken.ThrowIfCancellationRequested();

        this.ObservedRemainingCharacters.Add(bounds.RemainingCharactersForRead);

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

        return Task.FromResult(EmailContentRenderingResult.Rendered(
            new EmailContentRendering(
                new EmailContentHeaders("Subject", SentAt: null, ReceivedAt: null, [], EmailThreadReferences.None),
                plainText,
                sanitizedHtml,
                BodyIsEncrypted: false,
                EmailAttachmentSummary.Create(
                    [],
                    inlineResourceCount: 0,
                    isEncrypted: false,
                    carriesUnverifiedSignature: false,
                    containsUnexpandedTnefPart: false))));
    }
}
