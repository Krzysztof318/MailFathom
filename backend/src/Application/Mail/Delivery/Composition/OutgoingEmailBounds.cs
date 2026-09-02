// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>States how large a message this deployment is willing to compose.</summary>
/// <remarks>
/// <para>
/// Every one of these is the operator's number rather than whatever an author happened to pass, and each is checked
/// before any of the message is built. A bound discovered at the submission server is a whole transmission spent to be
/// told no, and a bound discovered nowhere at all is a deployment that will compose whatever it is handed.
/// </para>
/// <para>
/// The submission server's own advertised size is a second bound rather than a replacement for this one. A server that
/// advertises none still has whatever the deployment decided, and a server that advertises a smaller number than the
/// deployment configured is the one that decides — so both are checked and the message must satisfy each.
/// </para>
/// </remarks>
public sealed record OutgoingEmailBounds
{
    /// <summary>Gets the greatest number of people one message may be addressed to.</summary>
    /// <remarks>
    /// It is bounded from above by <see cref="OutgoingEmailRequest.MaximumRecipientCount" />, which is what the record
    /// itself will hold. Configuring more would let a message be composed that no record can be written for.
    /// </remarks>
    public required int MaxRecipientCount { get; init; }

    /// <summary>Gets the greatest number of characters either body may carry.</summary>
    /// <remarks>It applies to the plain text and to the HTML alternative separately, so a long body of one kind cannot decide what the other may be.</remarks>
    public required int MaxBodyCharacters { get; init; }

    /// <summary>Gets the greatest number of files one message may attach.</summary>
    public required int MaxAttachmentCount { get; init; }

    /// <summary>Gets the greatest number of octets one attached file may be made of.</summary>
    public required long MaxAttachmentBytes { get; init; }

    /// <summary>Gets the greatest number of octets the composed message may be transmitted as.</summary>
    /// <remarks>
    /// It is measured on the composed bytes rather than summed from the parts, because transfer encoding decides the
    /// answer: base64 costs roughly a third more than the octets it carries, and headers, boundaries, and folding are
    /// the rest of the difference between what an author supplied and what a server is offered.
    /// </remarks>
    public required long MaxMessageBytes { get; init; }
}
