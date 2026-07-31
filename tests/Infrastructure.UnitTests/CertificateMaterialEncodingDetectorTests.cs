// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Text;
using MailFathom.Infrastructure.Certificates;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

public sealed class CertificateMaterialEncodingDetectorTests
{
    [Fact]
    public void Detect_PemText_RecognizesPem()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");

        // Act
        var encoding = CertificateMaterialEncodingDetector.Detect(TestCertificates.ToPem(authority));

        // Assert
        Assert.Equal(CertificateMaterialEncoding.Pem, encoding);
    }

    /// <summary>A credential file routinely arrives with leading whitespace or a newline the operator never sees.</summary>
    [Fact]
    public void Detect_PemTextBehindLeadingWhitespace_StillRecognizesPem()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");
        var indentedPem = Encoding.UTF8.GetBytes($"\n  \t{authority.ExportCertificatePem()}");

        // Act
        var encoding = CertificateMaterialEncodingDetector.Detect(indentedPem);

        // Assert
        Assert.Equal(CertificateMaterialEncoding.Pem, encoding);
    }

    [Fact]
    public void Detect_DerEncodedCertificate_RecognizesDer()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");

        // Act
        var encoding = CertificateMaterialEncodingDetector.Detect(TestCertificates.ToDer(authority));

        // Assert
        Assert.Equal(CertificateMaterialEncoding.Der, encoding);
    }

    /// <summary>A bundle and a bare certificate are both an ASN.1 sequence, so the outer tag alone decides nothing.</summary>
    [Fact]
    public void Detect_Pkcs12Bundle_RecognizesItAsABundleRatherThanACertificate()
    {
        // Arrange
        using var authority = TestCertificates.CreateCertificateAuthority("MailFathom Test Root");

        // Act
        var encoding = CertificateMaterialEncodingDetector.Detect(TestCertificates.ToBundle(authority, "bundle-password"));

        // Assert
        Assert.Equal(CertificateMaterialEncoding.Pkcs12, encoding);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not certificate material at all")]
    [InlineData("{\"looks\":\"like configuration\"}")]
    public void Detect_MaterialThatIsNeitherPemNorAsn1_ReportsUnrecognized(string material)
    {
        // Act
        var encoding = CertificateMaterialEncodingDetector.Detect(Encoding.UTF8.GetBytes(material));

        // Assert
        Assert.Equal(CertificateMaterialEncoding.Unrecognized, encoding);
    }

    /// <summary>A sequence header that promises more bytes than the material holds must not be read past its end.</summary>
    [Fact]
    public void Detect_TruncatedAsn1Sequence_ReportsUnrecognizedRatherThanReadingPastTheMaterial()
    {
        // Act
        var encoding = CertificateMaterialEncodingDetector.Detect([0x30, 0x82, 0x01]);

        // Assert
        Assert.Equal(CertificateMaterialEncoding.Unrecognized, encoding);
    }
}
