// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.SyntheticMail.Configuration;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Configuration;

/// <summary>What the command demands of the <c>mailbox</c> block before an exchange will read or file anything.</summary>
public sealed class WatchedMailboxFileTests
{
    private const string Origin = "synthetic-mail.local.json";

    private const string Complete = """
        {
          "host": "smtp.example.test",
          "address": "throwaway@example.test",
          "password": "not-a-real-password",
          "mailbox": {
            "host": "imap.example.test",
            "port": 1143,
            "security": "StartTls",
            "address": "Developer <developer@example.test>",
            "userName": "developer",
            "password": "another-not-a-real-password",
            "sentFolder": "INBOX.Sent"
          }
        }
        """;

    [Fact]
    public void ReadWatchedMailboxFrom_ACompleteBlock_ReadsEveryValue()
    {
        // Arrange, Act
        var mailbox = Read(Complete);

        // Assert
        Assert.Equal("imap.example.test", mailbox.Host);
        Assert.Equal(1143, mailbox.Port);
        Assert.Equal(MailTransportSecurity.StartTls, mailbox.Security);
        Assert.Equal("developer@example.test", mailbox.Address.Address);
        Assert.Equal("developer", mailbox.UserName);
        Assert.Equal("another-not-a-real-password", mailbox.Password);
        Assert.Equal("INBOX.Sent", mailbox.SentFolder);
    }

    [Fact]
    public void ReadWatchedMailboxFrom_AFileWithoutTheBlock_IsRefusedNamingWhatAnExchangeNeedsItFor()
    {
        // Arrange
        var contents = """{ "host": "h", "address": "a@example.test", "password": "p" }""";

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Read(contents));

        // Assert
        // The message says which key is missing and why the mode cannot run without it, because a developer who has
        // only ever run a flat batch has no reason to know the block exists.
        Assert.Contains($"'mailbox' is not set in '{Origin}'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("identifier that mailbox's server assigned", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mailbox.host", """{ "mailbox": { "address": "a@example.test", "password": "p" } }""")]
    [InlineData("mailbox.address", """{ "mailbox": { "host": "h", "password": "p" } }""")]
    [InlineData("mailbox.password", """{ "mailbox": { "host": "h", "address": "a@example.test" } }""")]
    public void ReadWatchedMailboxFrom_ABlockMissingARequiredValue_IsRefusedNamingTheKey(string key, string contents)
    {
        // Arrange, Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Read(contents));

        // Assert
        Assert.Contains($"'{key}' is not set in '{Origin}'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadWatchedMailboxFrom_ABlockNamingNoSecurity_DefaultsToTheImmediateHandshakeOnItsOwnPort()
    {
        // Arrange
        var contents = """{ "mailbox": { "host": "h", "address": "a@example.test", "password": "p" } }""";

        // Act
        var mailbox = Read(contents);

        // Assert
        // Implicit TLS on 993 rather than the submission default, because that is what an IMAP server offers and a
        // block that inherited the submission answer would default to a port nothing listens on.
        Assert.Equal(MailTransportSecurity.ImplicitTls, mailbox.Security);
        Assert.Equal(993, mailbox.Port);
    }

    [Fact]
    public void ReadWatchedMailboxFrom_ABlockNamingTheUpgradingSecurity_DefaultsToTheUpgradingPort()
    {
        // Arrange
        var contents = """{ "mailbox": { "host": "h", "security": "StartTls", "address": "a@example.test", "password": "p" } }""";

        // Act
        var mailbox = Read(contents);

        // Assert
        Assert.Equal(143, mailbox.Port);
    }

    [Fact]
    public void ReadWatchedMailboxFrom_ABlockNamingNoSentFolder_LeavesItToTheServerToAdvertise()
    {
        // Arrange
        var contents = """{ "mailbox": { "host": "h", "address": "a@example.test", "password": "p", "sentFolder": "  " } }""";

        // Act
        var mailbox = Read(contents);

        // Assert
        Assert.Null(mailbox.SentFolder);
    }

    [Fact]
    public void ReadWatchedMailboxFrom_ABlockNamingSomethingThatIsNotASecurity_IsRefusedNamingTheKey()
    {
        // Arrange
        var contents = """{ "mailbox": { "host": "h", "security": "None", "address": "a@example.test", "password": "p" } }""";

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Read(contents));

        // Assert
        Assert.Contains($"'mailbox.security' in '{Origin}'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("no unsecured option", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadWatchedMailboxFrom_ABlockNamingSomethingThatIsNotAnAddress_IsRefusedNamingTheKey()
    {
        // Arrange
        var contents = """{ "mailbox": { "host": "h", "address": "not an address", "password": "p" } }""";

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => Read(contents));

        // Assert
        Assert.Contains($"'mailbox.address' in '{Origin}' is not a mail address", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_AMailbox_PrintsEveryValueButThePassword()
    {
        // Arrange
        var mailbox = Read(Complete);

        // Act
        var printed = mailbox.ToString();

        // Assert
        // The record's synthesized printer would put a real credential into a log line, a debugger view, or an
        // interpolation of the whole record, which is what the hand-written one exists to stop.
        Assert.Contains("imap.example.test", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("another-not-a-real-password", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadWatchedMailboxFrom_ANullArgument_IsRefused()
    {
        // Arrange
        using var contents = new MemoryStream();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => SendingAccountFile.ReadWatchedMailboxFrom(null!, Origin));
        Assert.Throws<ArgumentNullException>(() => SendingAccountFile.ReadWatchedMailboxFrom(contents, null!));
    }

    private static WatchedMailboxAccount Read(string contents)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(contents));

        return SendingAccountFile.ReadWatchedMailboxFrom(stream, Origin);
    }
}
