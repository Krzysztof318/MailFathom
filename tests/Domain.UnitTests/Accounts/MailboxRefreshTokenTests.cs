// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Domain.Accounts;
using Xunit;

namespace MailFathom.Domain.UnitTests.Accounts;

/// <summary>Covers the credential MailFathom holds on a mailbox owner's behalf, and the promises made about erasing it.</summary>
/// <remarks>
/// The erasure assertions read the buffer directly rather than inferring erasure from an accessor throwing, because a
/// type that threw after disposal while leaving the bytes in memory would pass every behavioural test and break the one
/// guarantee this type exists for.
/// </remarks>
public sealed class MailboxRefreshTokenTests
{
    [Fact]
    public void Create_TheIssuedBytes_RevealsThemUnchanged()
    {
        // Arrange
        var issued = Encoding.UTF8.GetBytes("a-refresh-token");

        // Act
        using var token = MailboxRefreshToken.Create(issued);

        // Assert
        Assert.Equal(issued, token.RevealBytes().ToArray());
        Assert.Equal("a-refresh-token", token.RevealAsString());
    }

    /// <summary>The seeding path hands over a value that already arrived as text, so both accessors describe one token.</summary>
    [Fact]
    public void FromText_AnIssuedToken_RevealsTheSameValueAsTextAndAsBytes()
    {
        // Act
        using var token = MailboxRefreshToken.FromText("a-rotated-refresh-token");

        // Assert
        Assert.Equal("a-rotated-refresh-token", token.RevealAsString());
        Assert.Equal(Encoding.UTF8.GetBytes("a-rotated-refresh-token"), token.RevealBytes().ToArray());
    }

    /// <summary>A trailing newline is kept, unlike a resolved secret's: this value comes out of a token response or a sealed column, never out of a file somebody edited.</summary>
    [Fact]
    public void Create_MaterialEndingInANewline_KeepsIt()
    {
        // Arrange
        var issued = Encoding.UTF8.GetBytes("a-refresh-token\n");

        // Act
        using var token = MailboxRefreshToken.Create(issued);

        // Assert
        Assert.Equal(issued, token.RevealBytes().ToArray());
    }

    [Fact]
    public void Create_EmptyMaterial_IsRefused() =>
        Assert.Throws<ArgumentException>(() => MailboxRefreshToken.Create([]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromText_MaterialThatNamesNoToken_IsRefused(string? material) =>
        Assert.ThrowsAny<ArgumentException>(() => MailboxRefreshToken.FromText(material!));

    [Fact]
    public void Create_TheCallerOwnedMaterial_IsCopiedRatherThanAliased()
    {
        // Arrange
        var issued = Encoding.UTF8.GetBytes("a-refresh-token");
        using var token = MailboxRefreshToken.Create(issued);

        // Act — a caller erasing its own copy must not empty the token's.
        Array.Clear(issued);

        // Assert
        Assert.Equal("a-refresh-token", token.RevealAsString());
    }

    [Fact]
    public void Dispose_AnOwnedToken_ErasesTheMaterialAndRefusesEveryAccessor()
    {
        // Arrange
        var token = MailboxRefreshToken.FromText("a-refresh-token");

        // Act
        token.Dispose();

        // Assert
        Assert.True(token.IsMaterialErased);
        Assert.Throws<ObjectDisposedException>(() => token.RevealAsString());
        Assert.Throws<ObjectDisposedException>(() => _ = token.RevealBytes().Length);
    }

    [Fact]
    public void Dispose_CalledTwice_IsAccepted()
    {
        // Arrange
        var token = MailboxRefreshToken.FromText("a-refresh-token");

        // Act
        token.Dispose();
        token.Dispose();

        // Assert
        Assert.True(token.IsMaterialErased);
    }

    /// <summary>Redaction is what keeps a log template or a synthesized record printing from carrying the credential.</summary>
    [Fact]
    public void ToString_ALiveToken_RedactsTheMaterial()
    {
        // Arrange
        using var token = MailboxRefreshToken.FromText("a-refresh-token");

        // Assert
        Assert.Equal("***", token.ToString());
    }
}
