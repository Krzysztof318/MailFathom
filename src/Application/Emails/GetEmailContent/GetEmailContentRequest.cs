// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>What a caller asks for when reading emails from the local mailbox copy.</summary>
/// <remarks>
/// <para>
/// Emails are named by their domain identity rather than as text, so an adapter converts a caller's strings once, at its
/// own boundary, and a malformed identifier is refused before it reaches a use case.
/// </para>
/// <para>
/// The two representation flags govern the whole call rather than one email each. A caller asking for markup or for
/// attachment content wants it for what it is about to read, and a flag per identifier would make the argument list
/// grow with the batch while answering a question no caller asks per email.
/// </para>
/// </remarks>
public sealed record GetEmailContentRequest
{
    /// <summary>The greatest number of emails one read may name.</summary>
    /// <remarks>
    /// <para>
    /// A listing returns up to 100 summaries and a search up to 50 ranked matches, so a caller can always name more
    /// emails than one read serves. The bound is small deliberately: it is the count half of the control on how much
    /// mail one protocol call draws out of a mailbox, and a handful of messages is what a caller reads after a search
    /// rather than what it archives.
    /// </para>
    /// <para>
    /// It lives on the request because that is what it constrains, so the boundary that checks a caller's list before
    /// parsing it and the use case that enforces the invariant afterwards read one number.
    /// </para>
    /// </remarks>
    public const int MaximumEmails = 10;

    private GetEmailContentRequest(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        bool includeSanitizedHtml,
        bool includeAttachmentContent)
    {
        this.StoredEmailIds = storedEmailIds;
        this.IncludeSanitizedHtml = includeSanitizedHtml;
        this.IncludeAttachmentContent = includeAttachmentContent;
    }

    /// <summary>Gets the emails to read, in the order the caller named them.</summary>
    /// <remarks>The order is the contract: results are returned in it, and the read's character budget is spent in it.</remarks>
    public IReadOnlyList<StoredEmailId> StoredEmailIds { get; }

    /// <summary>Gets whether to also return the sanitized HTML representation of each body.</summary>
    /// <remarks>
    /// Opt-in because it costs a sanitization pass over untrusted markup and because plain text is what most callers
    /// want: a model reading mail is better served by the words than by the layout around them.
    /// </remarks>
    public bool IncludeSanitizedHtml { get; }

    /// <summary>Gets whether to also return the octets of each attachment, rather than only describe it.</summary>
    /// <remarks>
    /// <para>
    /// What every read answers is what the message carries: how many attachments, what each is called, what it declares
    /// itself to be, and how large it is. A caller deciding whether a file is worth asking for needs all of that, and a
    /// read that had to ask twice to learn a name would answer the first call with a number and nothing to act on.
    /// </para>
    /// <para>
    /// The octets are opt-in because they are the message's most sensitive part and by far its largest: base64 makes an
    /// ordinary attachment several times the size of the body beside it, so a caller reading mail pays for a file only
    /// where it wanted one.
    /// </para>
    /// </remarks>
    public bool IncludeAttachmentContent { get; }

    /// <summary>Creates a request from the emails a caller named.</summary>
    /// <param name="storedEmailIds">The emails to read, in the order they were named.</param>
    /// <param name="includeSanitizedHtml">Whether to also produce the sanitized HTML representation of each body.</param>
    /// <param name="includeAttachmentContent">Whether to return the octets of each attachment rather than only describe it.</param>
    /// <returns>The validated request.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="storedEmailIds" /> is <see langword="null" />.</exception>
    /// <exception cref="EmailContentReadCountOutOfRangeException">Thrown when no email is named, or more than <see cref="MaximumEmails" /> are.</exception>
    /// <exception cref="EmailContentReadDuplicateEmailException">Thrown when the same email is named more than once.</exception>
    /// <remarks>
    /// Both refusals are the request's own invariant rather than a boundary's courtesy check, so an entrypoint added
    /// later cannot reach the use case with a list nobody counted. A caller's text is checked for length before it is
    /// parsed as well, at the boundary that holds the text.
    /// </remarks>
    public static GetEmailContentRequest Create(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        bool includeSanitizedHtml = false,
        bool includeAttachmentContent = false)
    {
        ArgumentNullException.ThrowIfNull(storedEmailIds);

        if (storedEmailIds.Count is 0 || storedEmailIds.Count > MaximumEmails)
        {
            throw new EmailContentReadCountOutOfRangeException(MaximumEmails);
        }

        if (storedEmailIds.Distinct().Count() != storedEmailIds.Count)
        {
            throw new EmailContentReadDuplicateEmailException();
        }

        return new GetEmailContentRequest([.. storedEmailIds], includeSanitizedHtml, includeAttachmentContent);
    }
}
