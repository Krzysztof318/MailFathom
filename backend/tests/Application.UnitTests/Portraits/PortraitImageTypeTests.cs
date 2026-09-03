// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Portraits;
using Xunit;

namespace MailFathom.Application.UnitTests.Portraits;

/// <summary>
/// Covers what decides whether octets are a portrait at all. The whole of the check is the signature the format opens
/// with, which is what makes an upload judged by what it is rather than by what its request claimed — so what has to
/// hold here is that the two published kinds are recognized, that everything else is refused, and that nothing shorter
/// than a signature is read past its end.
/// </summary>
public sealed class PortraitImageTypeTests
{
    [Fact]
    public void TryDetect_OctetsOpeningAsAJpeg_ReadsThemAsOne()
    {
        // Act
        var detected = PortraitImageType.TryDetect([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10], out var type);

        // Assert
        Assert.True(detected);
        Assert.Equal("image/jpeg", type.MediaType);
    }

    [Fact]
    public void TryDetect_OctetsOpeningAsAPng_ReadsThemAsOne()
    {
        // Act
        var detected = PortraitImageType.TryDetect(
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00],
            out var type);

        // Assert
        Assert.True(detected);
        Assert.Equal("image/png", type.MediaType);
    }

    /// <summary>A GIF is an image and is still refused, because what this publishes is two kinds rather than whatever a decoder would accept.</summary>
    [Theory]
    [InlineData((byte)'G', (byte)'I', (byte)'F', (byte)'8')]
    [InlineData((byte)'%', (byte)'P', (byte)'D', (byte)'F')]
    [InlineData((byte)'<', (byte)'s', (byte)'v', (byte)'g')]
    [InlineData((byte)'P', (byte)'K', 0x03, 0x04)]
    public void TryDetect_OctetsOfSomethingElse_AreNoPortraitKind(byte first, byte second, byte third, byte fourth)
    {
        // Act
        var detected = PortraitImageType.TryDetect([first, second, third, fourth], out var type);

        // Assert
        Assert.False(detected);
        Assert.False(type.IsSpecified);
    }

    /// <summary>An upload shorter than a signature is read to its end rather than past it, which is the case a fixed-length comparison would fault on.</summary>
    [Fact]
    public void TryDetect_FewerOctetsThanASignature_AreNoPortraitKindRatherThanARead()
    {
        // Act
        var detected = PortraitImageType.TryDetect([0x89, 0x50], out _);

        // Assert
        Assert.False(detected);
    }

    [Fact]
    public void TryDetect_NoOctetsAtAll_AreNoPortraitKind()
    {
        // Act
        var detected = PortraitImageType.TryDetect([], out _);

        // Assert
        Assert.False(detected);
    }

    /// <summary>The struct default is reachable and is not a kind, so it answers for no media type rather than for an empty one.</summary>
    [Fact]
    public void MediaType_TheStructDefault_RefusesToNameOne()
    {
        // Assert
        Assert.Throws<InvalidOperationException>(() => default(PortraitImageType).MediaType);
    }

    [Fact]
    public void All_TheSetThisBuildStores_IsTheTwoImageKinds()
    {
        // Assert
        Assert.Equal(["image/jpeg", "image/png"], PortraitImageType.All.Select(kind => kind.MediaType));
    }
}
