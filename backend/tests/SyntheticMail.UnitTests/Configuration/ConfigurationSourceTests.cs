// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.SyntheticMail.Configuration;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Configuration;

/// <summary>What a run reads out of the <c>dotnet user-secrets</c> store, which writes flat keys and string values.</summary>
public sealed class ConfigurationSourceTests
{
    private const string Origin = "user secrets";

    [Fact]
    public void Nest_AStoreWrittenByTheTooling_ReadsAsACompleteAccount()
    {
        // Arrange
        const string secrets = """
            {
              "host": "smtp.example.test",
              "port": "2525",
              "security": "ImplicitTls",
              "address": "throwaway@example.test",
              "password": "not-a-real-password"
            }
            """;

        // Act
        var account = Nested(secrets, SendingAccountFile.ReadFrom);

        // Assert
        Assert.Equal("smtp.example.test", account.Host);
        Assert.Equal(2525, account.Port);
        Assert.Equal(MailTransportSecurity.ImplicitTls, account.Security);
        Assert.Equal("not-a-real-password", account.Password);
    }

    [Fact]
    public void Nest_ColonSeparatedKeys_BecomeTheBlockTheyName()
    {
        // Arrange
        const string secrets = """
            {
              "mailbox:host": "imap.example.test",
              "mailbox:port": "1143",
              "mailbox:security": "StartTls",
              "mailbox:address": "watched@example.test",
              "mailbox:password": "not-a-real-password"
            }
            """;

        // Act
        var mailbox = Nested(secrets, SendingAccountFile.ReadWatchedMailboxFrom);

        // Assert
        Assert.Equal("imap.example.test", mailbox.Host);
        Assert.Equal(1143, mailbox.Port);
        Assert.Equal("watched@example.test", mailbox.Address.Address);
    }

    [Fact]
    public void Nest_AHandWrittenNestedBlock_IsLeftAsItIs()
    {
        // Arrange
        const string secrets = """
            {
              "mailbox": { "host": "imap.example.test", "address": "watched@example.test", "password": "not-a-real-password" }
            }
            """;

        // Act
        var mailbox = Nested(secrets, SendingAccountFile.ReadWatchedMailboxFrom);

        // Assert
        Assert.Equal("imap.example.test", mailbox.Host);
    }

    [Fact]
    public void Nest_ContentsThatAreNotAnObject_AreRefused()
    {
        // Arrange
        using var flattened = new MemoryStream("[]"u8.ToArray());

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => ConfigurationSource.Nest(flattened));

        // Assert
        Assert.Contains("is not a JSON object", failure.Message, StringComparison.Ordinal);
    }

    private static TConfigured Nested<TConfigured>(string secrets, Func<Stream, string, TConfigured> read)
    {
        using var flattened = new MemoryStream(Encoding.UTF8.GetBytes(secrets));
        using var nested = ConfigurationSource.Nest(flattened);

        return read(nested, Origin);
    }
}
