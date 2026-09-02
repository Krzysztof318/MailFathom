// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

public sealed class MimeKitEmailSpamHeaderReaderTests
{
    [Fact]
    public async Task ReadAsync_AMessageAServerAuthenticated_ReadsEveryOutcomeItStated()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: anna@example.test",
            "Authentication-Results: mail.example.test; spf=pass smtp.mailfrom=example.test; dkim=fail header.d=example.test",
            "Subject: Invoice",
            string.Empty,
            "Body");

        // Act
        var facts = await new MimeKitEmailSpamHeaderReader().ReadAsync(content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("spf", "pass"), ("dkim", "fail")],
            facts.AuthenticationResults.Select(result => (result.Method, result.Result)));
        Assert.All(facts.AuthenticationResults, result => Assert.False(result.IsForwarded));
    }

    /// <summary>The properties say whose domain an outcome was about, which is the whole reason a record keeps them.</summary>
    [Fact]
    public async Task ReadAsync_AnOutcomeWithProperties_RendersThemAsItsDetail()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: anna@example.test",
            "Authentication-Results: mail.example.test; dmarc=fail header.from=example.test",
            string.Empty,
            "Body");

        // Act
        var facts = await new MimeKitEmailSpamHeaderReader().ReadAsync(content, TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(facts.AuthenticationResults);

        Assert.Equal("dmarc", result.Method);
        Assert.Equal("header.from=example.test", result.Detail);
    }

    /// <summary>An ARC outcome is a relay's signed claim, so it is kept apart from what this mailbox's own server saw.</summary>
    [Fact]
    public async Task ReadAsync_AnArcAuthenticationResult_IsReadAsForwarded()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: anna@example.test",
            "ARC-Authentication-Results: i=1; relay.example.test; spf=pass smtp.mailfrom=example.test",
            string.Empty,
            "Body");

        // Act
        var facts = await new MimeKitEmailSpamHeaderReader().ReadAsync(content, TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(facts.AuthenticationResults);

        Assert.True(result.IsForwarded);
        Assert.Equal("spf", result.Method);
    }

    /// <summary>A message carries the header once per authenticating hop, so every one of them is read.</summary>
    [Fact]
    public async Task ReadAsync_TheHeaderWrittenBySeveralHops_ReadsAllOfThem()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: anna@example.test",
            "Authentication-Results: mail.example.test; spf=pass smtp.mailfrom=example.test",
            "Authentication-Results: relay.example.test; dkim=pass header.d=example.test",
            string.Empty,
            "Body");

        // Act
        var facts = await new MimeKitEmailSpamHeaderReader().ReadAsync(content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["spf", "dkim"], facts.AuthenticationResults.Select(result => result.Method));
    }

    [Fact]
    public async Task ReadAsync_AMessageAProviderScored_ReadsEveryRecognizedProviderHeader()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: anna@example.test",
            "X-Spam-Flag: YES",
            "X-Spam-Status: Yes, score=15.2 required=5.0 tests=BAYES_99",
            "X-Spam-Score: 15.2",
            "X-Spam-Level: ***************",
            "X-Mailer: something else entirely",
            string.Empty,
            "Body");

        // Act
        var facts = await new MimeKitEmailSpamHeaderReader().ReadAsync(content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["X-Spam-Flag", "X-Spam-Status", "X-Spam-Score", "X-Spam-Level"],
            facts.ProviderHeaders.Select(header => header.FieldName));
        Assert.Equal("YES", facts.ProviderHeaders[0].Value);
    }

    /// <summary>A message that carries neither kind of header is an ordinary message, not a failed read.</summary>
    [Fact]
    public async Task ReadAsync_AMessageCarryingNeitherKindOfHeader_ReadsNothing()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: anna@example.test",
            "Subject: Invoice",
            string.Empty,
            "Body");

        // Act
        var facts = await new MimeKitEmailSpamHeaderReader().ReadAsync(content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(facts.AuthenticationResults);
        Assert.Empty(facts.ProviderHeaders);
    }

    /// <summary>RFC 8601's grammar is written loosely in the wild, so a hop nobody can parse contributes nothing.</summary>
    [Fact]
    public async Task ReadAsync_AnUnparsableAuthenticationHeader_IsSkippedRatherThanFailingTheRead()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: anna@example.test",
            "Authentication-Results: ;;;=",
            "X-Spam-Flag: YES",
            string.Empty,
            "Body");

        // Act
        var facts = await new MimeKitEmailSpamHeaderReader().ReadAsync(content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(facts.AuthenticationResults);
        Assert.Single(facts.ProviderHeaders);
    }

    /// <summary>One unreadable message must not stop a run, so damage is classified from the folder alone.</summary>
    [Fact]
    public async Task ReadAsync_ContentThatIsNotAMessage_ReadsNothing()
    {
        // Arrange
        var content = MimeFixtures.StoredRawContent([0x00, 0x01, 0x02, 0x03]);

        // Act
        var facts = await new MimeKitEmailSpamHeaderReader().ReadAsync(content, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(facts.AuthenticationResults);
        Assert.Empty(facts.ProviderHeaders);
    }

    [Fact]
    public async Task ReadAsync_NoContent_IsRefused()
    {
        // Arrange, Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new MimeKitEmailSpamHeaderReader().ReadAsync(null!, TestContext.Current.CancellationToken));
    }
}
