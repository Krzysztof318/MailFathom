// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel;
using MailMcp.Application.EmailContent;

namespace MailMcp.Mcp.Tools;

/// <summary>Publishes the body representations of one email, or the reason there are none.</summary>
/// <remarks>
/// <para>
/// The plain text is always present and is empty in each state where nothing could be read, which is why
/// <see cref="Availability" /> is published beside it rather than left to be inferred: an empty body and an unreadable
/// one are different findings about a message and a caller acts on them differently.
/// </para>
/// <para>
/// The sanitized HTML is present only when the caller asked for it and the message actually has an HTML part, so its
/// absence answers whichever of those two questions the caller was in a position to ask.
/// </para>
/// </remarks>
[Description("The body of the email, in the representations that could be produced, or the reason there are none.")]
internal sealed record EmailBodyContent
{
    /// <summary>Gets whether the body could be read, or why it could not.</summary>
    [Description("Whether the body could be read: 'readable' when the text below is the message, 'encryptedNotReadableLocally' when the body arrived inside a cryptographic envelope MailMcp cannot open, or 'notStoredExceededSizeLimit' when the email was deliberately stored without its content. In the two latter states the text is empty because nothing could be read, not because the message displayed nothing.")]
    public required EmailBodyAvailabilityState Availability { get; init; }

    /// <summary>Gets the plain-text representation, which is empty whenever the body could not be read.</summary>
    [Description("The plain-text representation of the body, which is the one to read from. It is derived from the message's text part, or from its HTML when it carried no text part.")]
    public required EmailBodyText PlainText { get; init; }

    /// <summary>Gets the sanitized HTML representation, or <see langword="null" /> when none was produced.</summary>
    [Description("The sanitized HTML representation, or null when it was not requested or the message carries no HTML part. Scripts, event handlers, embedded objects, form elements, external references, and cid: references to the message's own inline parts are removed, so nothing in it can fetch a resource or report that the mail was read.")]
    public EmailBodyText? SanitizedHtml { get; init; }

    /// <summary>Publishes the body a read produced.</summary>
    /// <param name="body">The body the use case returned.</param>
    /// <returns>The wire representation of <paramref name="body" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body" /> is <see langword="null" />.</exception>
    public static EmailBodyContent From(EmailContentBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return new EmailBodyContent
        {
            Availability = PublishedAvailability(body.Availability),
            PlainText = EmailBodyText.From(body.PlainText),
            SanitizedHtml = body.SanitizedHtml is { } sanitizedHtml ? EmailBodyText.From(sanitizedHtml) : null,
        };
    }

    /// <summary>Reads the published value the application state names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when an application state has no published value, which means one was added without deciding what a
    /// client should be told about it.
    /// </exception>
    private static EmailBodyAvailabilityState PublishedAvailability(EmailBodyAvailability availability) =>
        availability switch
        {
            EmailBodyAvailability.Readable => EmailBodyAvailabilityState.Readable,
            EmailBodyAvailability.EncryptedNotReadableLocally => EmailBodyAvailabilityState.EncryptedNotReadableLocally,
            EmailBodyAvailability.NotStoredExceededSizeLimit => EmailBodyAvailabilityState.NotStoredExceededSizeLimit,
            _ => throw new ArgumentOutOfRangeException(
                nameof(availability),
                availability,
                "The body availability has no published protocol value."),
        };
}
