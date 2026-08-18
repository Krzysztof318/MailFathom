// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail.Dkim;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Dkim;

public sealed class DkimSignatureTagsTests
{
    /// <summary>The signing domain is read from the tag RFC 6376 writes it in, and normalized for comparison.</summary>
    [Fact]
    public void TryReadSigningDomain_AnOrdinarySignature_ReadsTheSigningDomain()
    {
        // Act
        var read = DkimSignatureTags.TryReadSigningDomain(
            "v=1; a=rsa-sha256; d=Signer.Example.Test; s=mailfathom; h=from:subject; b=AbC=",
            out var domain);

        // Assert
        Assert.True(read);
        Assert.Equal("SIGNER.EXAMPLE.TEST", domain.NormalizedValue);
    }

    /// <summary>A base64 tag carrying equals signs is not mistaken for the tag being looked for.</summary>
    [Fact]
    public void TryReadSigningDomain_ASignatureWhoseBase64CarriesEqualsSigns_ReadsTheSigningDomain()
    {
        // Act
        var read = DkimSignatureTags.TryReadSigningDomain(
            "v=1; a=rsa-sha256; bh=d=not=a=domain=; d=signer.example.test; b=Zm9v==",
            out var domain);

        // Assert
        Assert.True(read);
        Assert.Equal("SIGNER.EXAMPLE.TEST", domain.NormalizedValue);
    }

    /// <summary>A value written across a folded header is read as the name it is, not refused for the transport's line break.</summary>
    [Fact]
    public void TryReadSigningDomain_AFoldedValue_ReadsTheSigningDomain()
    {
        // Act
        var read = DkimSignatureTags.TryReadSigningDomain("v=1;\r\n\td=signer.example.test;\r\n\ts=mailfathom", out var domain);

        // Assert
        Assert.True(read);
        Assert.Equal("SIGNER.EXAMPLE.TEST", domain.NormalizedValue);
    }

    /// <summary>An internationalized signing domain is held in the encoding the wire already uses.</summary>
    [Fact]
    public void TryReadSigningDomain_AnInternationalizedDomain_ReadsItsAsciiForm()
    {
        // Act
        var read = DkimSignatureTags.TryReadSigningDomain("v=1; d=bücher.example; s=mailfathom", out var domain);

        // Assert
        Assert.True(read);
        Assert.Equal("XN--BCHER-KVA.EXAMPLE", domain.NormalizedValue);
    }

    /// <summary>A signature naming no domain, or an unusable one, contributes no identity rather than a repaired one.</summary>
    [Theory]
    [InlineData("v=1; a=rsa-sha256; s=mailfathom; b=AbC=")]
    [InlineData("v=1; d=; s=mailfathom")]
    [InlineData("v=1; d=signer..example.test; s=mailfathom")]
    [InlineData("v=1; d=signer@example.test; s=mailfathom")]
    [InlineData("")]
    public void TryReadSigningDomain_ASignatureNamingNoUsableDomain_IsRefused(string headerValue)
    {
        // Act, Assert
        Assert.False(DkimSignatureTags.TryReadSigningDomain(headerValue, out _));
    }

    /// <summary>A header past the bound is passed over unread, because the value arrives from whoever wrote the message.</summary>
    [Fact]
    public void TryReadSigningDomain_AnOverLongHeader_IsPassedOver()
    {
        // Arrange
        var headerValue = $"v=1; d=signer.example.test; b={new string('A', 4096)}";

        // Act, Assert
        Assert.False(DkimSignatureTags.TryReadSigningDomain(headerValue, out _));
    }

    /// <summary>The tag name is compared the way RFC 6376's grammar is, which is without regard to case.</summary>
    [Fact]
    public void TryReadSigningDomain_ATagWrittenInUpperCase_IsStillRead()
    {
        // Act
        var read = DkimSignatureTags.TryReadSigningDomain("v=1; D=signer.example.test; s=mailfathom", out var domain);

        // Assert
        Assert.True(read);
        Assert.Equal("SIGNER.EXAMPLE.TEST", domain.NormalizedValue);
    }
}
