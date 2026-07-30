// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel;
using MailMcp.Domain.Emails;

namespace MailMcp.Mcp.Tools;

/// <summary>Publishes one address a message wrote, paired with the header it appeared in.</summary>
/// <remarks>
/// The participants travel as one list carrying their roles rather than as a field per header, because a message can
/// address hundreds of mailboxes across five headers and a caller reading it asks who took part rather than which
/// header each of them sat in. The role is still published, so the question can be asked of any one of them.
/// </remarks>
[Description("One address the email wrote, and the header it was written in.")]
internal sealed record EmailHeaderParticipant
{
    /// <summary>Gets which header carried the address.</summary>
    [Description("The header the address was written in: sender, from, replyTo, to, cc, or bcc. A bcc address is present only in a copy the sender kept of their own message.")]
    public required EmailHeaderRole Role { get; init; }

    /// <summary>Gets the address as the message wrote it.</summary>
    [Description("The mail address as the message wrote it, trimmed. Addresses no mail parser could read are absent rather than repaired, so this is always a usable address.")]
    public required string Address { get; init; }

    /// <summary>Gets the display name the message carried, or <see langword="null" /> when it carried none.</summary>
    [Description("The display name the message wrote for the address, or null when it wrote none. A sender chooses it freely, so it names nobody reliably; the address is the identifying part.")]
    public string? DisplayName { get; init; }

    /// <summary>Publishes one participant.</summary>
    /// <param name="participant">The participant the headers carried.</param>
    /// <returns>The wire representation of <paramref name="participant" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="participant" /> is <see langword="null" />.</exception>
    public static EmailHeaderParticipant From(EmailParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        return new EmailHeaderParticipant
        {
            Role = PublishedRole(participant.Role),
            Address = participant.Address.Address,
            DisplayName = participant.Address.DisplayName,
        };
    }

    /// <summary>Reads the published value the domain role names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a domain role has no published value, which means a header was added to the domain without deciding
    /// how a client reads it.
    /// </exception>
    private static EmailHeaderRole PublishedRole(EmailAddressRole role) => role switch
    {
        EmailAddressRole.Sender => EmailHeaderRole.Sender,
        EmailAddressRole.From => EmailHeaderRole.From,
        EmailAddressRole.ReplyTo => EmailHeaderRole.ReplyTo,
        EmailAddressRole.To => EmailHeaderRole.To,
        EmailAddressRole.Cc => EmailHeaderRole.Cc,
        EmailAddressRole.Bcc => EmailHeaderRole.Bcc,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "The address role has no published protocol value."),
    };
}
