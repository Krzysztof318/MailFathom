// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>One attachment's decoded octets, or the bound that kept them out of the read.</summary>
/// <remarks>
/// <para>
/// Content is all or nothing. A partially returned file is worse than an absent one: it has the size of a file, it
/// opens as damage, and nothing downstream can tell which of the two it received. So a bound removes the content
/// entirely and names itself, where the equivalent bound on a body cuts the text and reports the cut.
/// </para>
/// <para>
/// The octets are message content in full and inherit every classification, retention, access, and erasure constraint
/// of the mail they were read from. They are never logged, never persisted anywhere new, and exist only for as long as
/// the read that produced them.
/// </para>
/// </remarks>
public sealed record EmailAttachmentContent
{
    private EmailAttachmentContent(EmailAttachmentContentAvailability availability, ReadOnlyMemory<byte> octets)
    {
        this.Availability = availability;
        this.Octets = octets;
    }

    /// <summary>Gets whether the content is present, and which bound stopped it when it is not.</summary>
    public EmailAttachmentContentAvailability Availability { get; }

    /// <summary>Gets the decoded octets, which are empty for every availability but <see cref="EmailAttachmentContentAvailability.Returned" />.</summary>
    public ReadOnlyMemory<byte> Octets { get; }

    /// <summary>Gets the content of an attachment nothing asked to read.</summary>
    public static EmailAttachmentContent NotRequested { get; } =
        new(EmailAttachmentContentAvailability.NotRequested, ReadOnlyMemory<byte>.Empty);

    /// <summary>Carries the content a read returned in full.</summary>
    /// <param name="octets">The part's decoded octets.</param>
    /// <returns>The returned content.</returns>
    public static EmailAttachmentContent Returned(ReadOnlyMemory<byte> octets) =>
        new(EmailAttachmentContentAvailability.Returned, octets);

    /// <summary>Records that a bound kept the content out of the read.</summary>
    /// <param name="availability">The bound that stopped it.</param>
    /// <returns>The withheld content.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="availability" /> names no bound.</exception>
    public static EmailAttachmentContent Withheld(EmailAttachmentContentAvailability availability)
    {
        if (availability is EmailAttachmentContentAvailability.Returned
            or EmailAttachmentContentAvailability.NotRequested)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availability),
                availability,
                "Withheld content names the bound that stopped it, which neither returned nor unrequested content has.");
        }

        return new EmailAttachmentContent(availability, ReadOnlyMemory<byte>.Empty);
    }
}
