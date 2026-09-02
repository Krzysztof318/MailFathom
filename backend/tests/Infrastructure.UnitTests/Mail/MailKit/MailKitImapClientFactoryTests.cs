// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Mail.MailKit;
using MailKit;
using MailKit.Net.Imap;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit;

/// <summary>Covers the one invariant every IMAP client this deployment opens is constructed under.</summary>
/// <remarks>
/// MailKit writes a session's commands, responses, envelopes, and payloads to the protocol logger it was constructed
/// with and to nothing else, so a client holding the null logger writes them nowhere. That is what makes the rule a
/// property of construction rather than of a log level or a category filter: no setting can turn protocol traffic back
/// on, because there is nothing for it to be turned on towards.
/// </remarks>
public sealed class MailKitImapClientFactoryTests
{
    [Fact]
    public void CreateWithoutProtocolLogging_AnyClient_WritesProtocolTrafficNowhere()
    {
        // Act
        using var client = MailKitImapClientFactory.CreateWithoutProtocolLogging();

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
        using var logging = new ImapClient(new ProtocolLogger(written));

        // Assert
        Assert.IsNotType<NullProtocolLogger>(logging.ProtocolLogger);
    }
}
