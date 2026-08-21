// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Mail.MailKit.Delivery;
using MailKit;
using MailKit.Net.Smtp;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit.Delivery;

/// <summary>Covers the one invariant every SMTP client this deployment opens is constructed under.</summary>
/// <remarks>
/// A submission session carries the account's credential and, once something sends, every recipient of every message.
/// MailKit writes all of it to the protocol logger the client was constructed with and to nothing else, so a client
/// holding the null logger writes it nowhere — a property of construction rather than of a log level or a category
/// filter, which is what makes it something no setting can turn back on.
/// </remarks>
public sealed class MailKitSmtpClientFactoryTests
{
    [Fact]
    public void CreateWithoutProtocolLogging_AnyClient_WritesProtocolTrafficNowhere()
    {
        // Act
        using var client = MailKitSmtpClientFactory.CreateWithoutProtocolLogging();

        // Assert
        Assert.IsType<NullProtocolLogger>(client.ProtocolLogger);
    }

    /// <summary>
    /// The control the assertion above needs: a client built with a writing logger reports that logger, so the absence
    /// asserted there is a property being read rather than one MailKit could have stopped reporting.
    /// </summary>
    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the logger passes to the client, which disposes it along with the stream it writes to.")]
    public void CreateWithoutProtocolLogging_ComparedWithAClientThatLogs_ReadsWhatTheClientWasBuiltWith()
    {
        // Arrange
        var written = new MemoryStream();

        // Act
        using var logging = new SmtpClient(new ProtocolLogger(written));

        // Assert
        Assert.IsNotType<NullProtocolLogger>(logging.ProtocolLogger);
    }
}
