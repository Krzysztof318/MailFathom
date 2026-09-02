// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

    /// <summary>A header a transport folded is read as the name it is, wherever the line break landed.</summary>
    /// <remarks>
    /// The two rows are the two places folding occurs and they are not the same claim: the first breaks between tags,
    /// which any reading survives, and the second breaks inside the value itself, which is what the whitespace removal
    /// exists for — a domain name carries no whitespace of its own, so every space in the value is the transport's.
    /// </remarks>
    [Theory]
    [InlineData("v=1;\r\n\td=signer.example.test;\r\n\ts=mailfathom")]
    [InlineData("v=1; d=signer.exa\r\n\tmple.test; s=mailfathom")]
    public void TryReadSigningDomain_AFoldedHeader_ReadsTheSigningDomain(string headerValue)
    {
        // Act
        var read = DkimSignatureTags.TryReadSigningDomain(headerValue, out var domain);

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
