// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Authorization;

namespace MailFathom.Client.UnitTests.Backend.Authorization;

/// <summary>What a typed username and password have to be before anything is sent, and what one may never say about itself.</summary>
public sealed class OwnerCredentialTests
{
    [Theory]
    [InlineData("", "a-long-password")]
    [InlineData("   ", "a-long-password")]
    [InlineData("ada", "")]
    [InlineData("ada", "   ")]
    public void Constructor_AHalfNobodyTyped_IsRefused(string username, string password)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => new OwnerCredential(username, password));
    }

    /// <summary>
    /// RFC 7617 splits the decoded field at the first colon, so a username carrying one would be presented as a
    /// shorter name with a longer password — a silent mis-authentication rather than a refusal anybody could read.
    /// </summary>
    [Fact]
    public void Constructor_AUsernameCarryingAColon_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>("username", () => new OwnerCredential("ada:lovelace", "a-long-password"));
    }

    /// <summary>The password is the half that may carry anything, which is why only the username is judged.</summary>
    [Fact]
    public void Constructor_APasswordCarryingAColon_IsAccepted()
    {
        // Arrange, Act
        var credential = new OwnerCredential("ada", "a:long:password");

        // Assert
        Assert.Equal("a:long:password", credential.Password);
    }

    /// <summary>
    /// A record prints every member, so anything that renders one — an interpolated message, a log's fallback
    /// formatter, a debugger watch — would carry the password unless the type says otherwise itself.
    /// </summary>
    [Fact]
    public void ToString_ACredential_NamesNeitherHalfOfIt()
    {
        // Arrange
        var credential = new OwnerCredential("ada", "a-long-password");

        // Act
        var rendered = $"{credential}";

        // Assert
        Assert.DoesNotContain("a-long-password", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("ada", rendered, StringComparison.Ordinal);
    }
}
