// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

/// <summary>Covers which spellings may identify a secret, and why the accepted set is narrow.</summary>
/// <remarks>
/// The name is written into logs, metric labels, and audit records without escaping, so what it may contain is a safety
/// question rather than a matter of taste. A name that could carry a newline, a quotation mark, or a control character
/// would let a configuration file decide how a log line parses.
/// </remarks>
public sealed class SecretNameTests
{
    [Theory]
    [InlineData("primary")]
    [InlineData("imap-primary-password")]
    [InlineData("chatgpt.connector")]
    [InlineData("workstation_key")]
    [InlineData("key2")]
    [InlineData("2027")]
    public void TryCreate_ALetterOrDigitFollowedByTheAcceptedCharacters_IsAccepted(string configuredValue)
    {
        // Arrange, Act
        var created = SecretName.TryCreate(configuredValue, out var name);

        // Assert
        Assert.True(created);
        Assert.True(name.IsSpecified);
        Assert.Equal(configuredValue, name.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" primary")]
    [InlineData("primary ")]
    [InlineData("-primary")]
    [InlineData(".primary")]
    [InlineData("primary key")]
    [InlineData("primary/key")]
    [InlineData("primary\nkey")]
    [InlineData("primary\"key")]
    [InlineData("klucz-główny")]
    public void TryCreate_AnythingElse_IsRefusedAsAnIdentityNothingCouldSafelyRecord(string? configuredValue)
    {
        // Arrange, Act
        var created = SecretName.TryCreate(configuredValue, out var name);

        // Assert
        Assert.False(created);
        Assert.False(name.IsSpecified);
    }

    [Fact]
    public void TryCreate_AtTheLengthLimit_IsAcceptedAndOneCharacterBeyondItIsNot()
    {
        // Arrange
        var atTheLimit = new string('a', SecretName.MaximumLength);

        // Act
        var acceptedAtTheLimit = SecretName.TryCreate(atTheLimit, out _);
        var acceptedBeyondIt = SecretName.TryCreate(atTheLimit + "a", out _);

        // Assert
        Assert.True(acceptedAtTheLimit);
        Assert.False(acceptedBeyondIt);
    }

    [Fact]
    public void ToString_TheStructDefault_SaysSoRatherThanReturningAnEmptyName()
    {
        // Arrange, Act, Assert
        Assert.Equal("(unnamed)", default(SecretName).ToString());
    }

    [Fact]
    public void ToString_ANamedSecret_ReturnsTheNameSoALogLineCanCarryIt()
    {
        // Arrange
        SecretName.TryCreate("imap-primary-password", out var name);

        // Act, Assert
        Assert.Equal("imap-primary-password", name.ToString());
    }
}
