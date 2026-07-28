// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Domain.Emails;

/// <summary>Pairs one address with the header role it was written in.</summary>
/// <param name="Role">Which header carried the address.</param>
/// <param name="Address">The normalized address.</param>
public sealed record EmailParticipant(EmailAddressRole Role, EmailAddress Address);
