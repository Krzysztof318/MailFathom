// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Names one person an author addressed a message to, exactly as they wrote it.</summary>
/// <param name="Role">The header the author wants this person named in.</param>
/// <param name="Address">The addr-spec the author supplied, unparsed and unvalidated.</param>
/// <param name="DisplayName">The name the author wants written beside the address, or nothing to write the address alone.</param>
/// <remarks>
/// <para>
/// Both text members are an author's input rather than a value this system produced, which is why they arrive as text.
/// Parsing, normalizing, and refusing them is the composer's, so the shape a caller hands over is what it was given —
/// a boundary that repaired an address before this point would compose a message to somebody nobody named.
/// </para>
/// <para>
/// The display name reaches the composed message and nothing else. The outgoing record holds addresses because a send
/// cannot be resumed without them; a name is presentation, so it stays in the stored MIME the way every other authored
/// field does.
/// </para>
/// </remarks>
public sealed record AuthoredEmailRecipient(OutgoingRecipientRole Role, string Address, string? DisplayName = null);
