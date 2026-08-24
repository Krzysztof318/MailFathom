// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Storage;

/// <summary>Covers what a placement says about a payload, which is what the row that points at it is written from.</summary>
/// <remarks>
/// The two factories are the whole of the type, and each answers a different half of one contract: the database
/// placement carries the bytes so the row can hold them, and the object placement carries a key instead because the
/// endpoint already holds them. Getting the length or the digest wrong here would be reported later as a corrupted
/// message rather than as a placement that lied.
/// </remarks>
public sealed class PlacedEmailContentTests
{
    private static readonly ReadOnlyMemory<byte> Message =
        Encoding.ASCII.GetBytes("Message-ID: <one@example.test>\r\n\r\nShall we?").AsMemory();

    /// <summary>The database placement measures the payload itself, which is what an integrity check is read against.</summary>
    [Fact]
    public void InDatabase_APayload_CarriesTheBytesWithTheLengthAndDigestMeasuredOverThem()
    {
        // Act
        var placed = PlacedEmailContent.InDatabase(Message);

        // Assert
        Assert.Equal(ContentStorageBackend.Database, placed.Backend);
        Assert.Null(placed.ObjectLocator);
        Assert.Equal(Message.ToArray(), placed.RawMime.ToArray());
        Assert.Equal(Message.Length, placed.ByteLength);
        Assert.Equal(SHA256.HashData(Message.Span), placed.Sha256Hash.ToArray());
    }

    /// <summary>An empty payload is a caller's mistake rather than a message, and a row carrying one would read as content that is gone.</summary>
    [Fact]
    public void InDatabase_AnEmptyPayload_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => PlacedEmailContent.InDatabase(ReadOnlyMemory<byte>.Empty));
    }

    /// <summary>The object placement carries no bytes, because carrying them would be a second copy nothing reads.</summary>
    [Fact]
    public void InObjectStorage_AWrittenObject_CarriesTheLocatorAndNoPayload()
    {
        // Arrange
        var digest = SHA256.HashData(Message.Span);

        // Act
        var placed = PlacedEmailContent.InObjectStorage("mailfathom/incoming/one", Message.Length, digest);

        // Assert
        Assert.Equal(ContentStorageBackend.ObjectStorage, placed.Backend);
        Assert.Equal("mailfathom/incoming/one", placed.ObjectLocator);
        Assert.True(placed.RawMime.IsEmpty);
        Assert.Equal(Message.Length, placed.ByteLength);
        Assert.Equal(digest, placed.Sha256Hash.ToArray());
    }

    /// <summary>A row's locator is the only way back to the payload, so a placement that names none is refused where it is made.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void InObjectStorage_ALocatorThatNamesNothing_IsRefused(string objectLocator)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => PlacedEmailContent.InObjectStorage(objectLocator, 1, SHA256.HashData(Message.Span)));
    }

    /// <summary>A digest of the wrong size cannot be compared against one computed on a read, so it is refused rather than stored.</summary>
    [Fact]
    public void InObjectStorage_ADigestThatIsNotSha256_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => PlacedEmailContent.InObjectStorage("mailfathom/incoming/one", 1, new byte[16]));
    }

    /// <summary>A length that describes no payload would pass an integrity check over an object nobody wrote.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InObjectStorage_ALengthThatDescribesNoPayload_IsRefused(long byteLength)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PlacedEmailContent.InObjectStorage(
                "mailfathom/incoming/one",
                byteLength,
                SHA256.HashData(Message.Span)));
    }
}
