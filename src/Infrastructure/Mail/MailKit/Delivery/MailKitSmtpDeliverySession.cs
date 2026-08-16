// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery;

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>A submission server reached over one established connection, and what that server said it will accept.</summary>
/// <remarks>
/// The session is the port and the connection is the transport, kept apart for the reason the mailbox adapter keeps
/// them apart: what a caller may ask for is decided by this type's surface, while how the server is reached, bounded,
/// and given up on belongs to the connection underneath. The connection is owned here and closed with the session.
/// </remarks>
internal sealed class MailKitSmtpDeliverySession(MailKitSmtpConnection connection) : IMailDeliverySession
{
    /// <inheritdoc />
    public MailDeliveryCapabilities Capabilities => connection.Capabilities;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
