// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailMcp.Infrastructure.Certificates;

/// <summary>Recognizes the encoding of resolved certificate material from the material itself.</summary>
/// <remarks>
/// Detection exists because the three supported encodings need three different loader calls and only one of them may
/// arrive inline, so an encoding-blind loader could neither pick the right call nor explain the inline rejection. It
/// reads the first few bytes only: recognizing an encoding is not validating a certificate, and a shape that passes
/// here still has to parse.
/// </remarks>
internal static class CertificateMaterialEncodingDetector
{
    private const byte DerSequenceTag = 0x30;
    private const byte DerIntegerTag = 0x02;
    private const byte DerContextSpecificZeroTag = 0xA0;
    private const byte DerLongFormLengthMask = 0x80;

    /// <summary>Recognizes the encoding of certificate material.</summary>
    /// <param name="material">The resolved material.</param>
    /// <returns>The recognized encoding, or <see cref="CertificateMaterialEncoding.Unrecognized" />.</returns>
    internal static CertificateMaterialEncoding Detect(ReadOnlySpan<byte> material)
    {
        var significantMaterial = material.TrimStart(" \t\r\n"u8);

        if (significantMaterial.StartsWith("-----BEGIN"u8))
        {
            return CertificateMaterialEncoding.Pem;
        }

        return significantMaterial.Length > 0 && significantMaterial[0] == DerSequenceTag
            ? DetectBinaryAsn1Encoding(significantMaterial)
            : CertificateMaterialEncoding.Unrecognized;
    }

    /// <summary>Tells a DER-encoded certificate apart from a PKCS#12 bundle by the first field inside the outer sequence.</summary>
    /// <remarks>
    /// Both encodings are an ASN.1 <c>SEQUENCE</c>, so the outer tag decides nothing. A PKCS#12 <c>PFX</c> opens with a
    /// version <c>INTEGER</c>, while an X.509 <c>Certificate</c> opens with the <c>tbsCertificate</c> <c>SEQUENCE</c> —
    /// or, in the rare explicit-tag encoding some tooling emits, with a context-specific constructed tag.
    /// </remarks>
    private static CertificateMaterialEncoding DetectBinaryAsn1Encoding(ReadOnlySpan<byte> material)
    {
        if (!TryReadFirstFieldTag(material, out var firstFieldTag))
        {
            return CertificateMaterialEncoding.Unrecognized;
        }

        return firstFieldTag switch
        {
            DerIntegerTag => CertificateMaterialEncoding.Pkcs12,
            DerSequenceTag or DerContextSpecificZeroTag => CertificateMaterialEncoding.Der,
            _ => CertificateMaterialEncoding.Unrecognized,
        };
    }

    private static bool TryReadFirstFieldTag(ReadOnlySpan<byte> material, out byte firstFieldTag)
    {
        firstFieldTag = 0;

        if (material.Length < 2)
        {
            return false;
        }

        var lengthByte = material[1];
        var headerLength = (lengthByte & DerLongFormLengthMask) == 0
            ? 2
            : 2 + (lengthByte & ~DerLongFormLengthMask);

        if (material.Length <= headerLength)
        {
            return false;
        }

        firstFieldTag = material[headerLength];

        return true;
    }
}
