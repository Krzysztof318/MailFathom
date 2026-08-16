// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery;
using MailKit.Net.Smtp;

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>Reads what a submission server advertised as the facts a caller decides on.</summary>
/// <remarks>
/// This is the only place that translates the mail library's capability flags into MailFathom's own vocabulary, so a
/// caller above never inspects an extension name and never parses a greeting. Everything advertised beyond the three
/// facts below is deliberately dropped here rather than carried up as a flag set nobody reads.
/// </remarks>
internal static class MailKitSmtpCapabilityMapping
{
    /// <summary>Reads the capabilities the connected client negotiated with its server.</summary>
    /// <param name="client">The connected client, whose advertised extensions have already been exchanged.</param>
    /// <returns>The facts that decide whether a message may be sent at all.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A server that advertises the size extension without a number is stating that it enforces no fixed maximum, and
    /// the mail library reports that as zero. It is mapped to an absent bound rather than to a limit of nothing, which
    /// would refuse every message the deployment ever composed.
    /// </remarks>
    internal static MailDeliveryCapabilities ToDeliveryCapabilities(this ISmtpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var advertised = client.Capabilities;

        var declaredMaximum = advertised.HasFlag(SmtpCapabilities.Size) && client.MaxSize > 0
            ? (long?)client.MaxSize
            : null;

        return new MailDeliveryCapabilities(
            declaredMaximum,
            advertised.HasFlag(SmtpCapabilities.EightBitMime),
            advertised.HasFlag(SmtpCapabilities.UTF8));
    }
}
