// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings;

/// <summary>
/// Covers what the profile table's unique index is over. Every property that decides whether two vectors can be
/// compared has to reach the digest, because a property that does not would let two incomparable spaces resolve to one
/// registered profile and be searched as though they were the same.
/// </summary>
public sealed class EmbeddingProfileFingerprintTests
{
    /// <summary>Re-declaring the same geometry has to resolve to the profile already registered rather than a second one.</summary>
    [Fact]
    public void Compute_TheSameGeometry_ProducesTheSameDigest()
    {
        // Act
        var first = EmbeddingProfileFingerprint.Compute(Identity());
        var second = EmbeddingProfileFingerprint.Compute(Identity());

        // Assert
        Assert.Equal(first, second);
    }

    /// <summary>
    /// Each of these changes what a vector means, so each has to be a different profile. A digest that missed one would
    /// silently file vectors of a second space under the first profile's identifier.
    /// </summary>
    [Theory]
    [MemberData(nameof(IdentitiesDifferingFromTheReference))]
    public void Compute_AGeometryDifferingInOneProperty_ProducesADifferentDigest(EmbeddingProfileIdentity changed)
    {
        // Act
        var reference = EmbeddingProfileFingerprint.Compute(Identity());
        var other = EmbeddingProfileFingerprint.Compute(changed);

        // Assert
        Assert.NotEqual(reference, other);
    }

    /// <summary>
    /// An absent optional value is written as a presence marker rather than skipped, so a model exposing no version
    /// cannot fingerprint the same as one whose version happens to be the empty encoding of the next field.
    /// </summary>
    [Fact]
    public void Compute_AnAbsentOptionalValue_IsDistinguishedFromAPresentOne()
    {
        // Act
        var absent = EmbeddingProfileFingerprint.Compute(Identity(modelVersion: null));
        var present = EmbeddingProfileFingerprint.Compute(Identity(modelVersion: "2026-01-01"));

        // Assert
        Assert.NotEqual(absent, present);
    }

    /// <summary>
    /// Every field is length-prefixed so the encoding is one-to-one: moving a character from the end of one field to
    /// the start of the next must not leave the two hashing alike.
    /// </summary>
    [Fact]
    public void Compute_AFieldBoundaryMovedBetweenTwoNames_ProducesADifferentDigest()
    {
        // Act
        var first = EmbeddingProfileFingerprint.Compute(Identity(provider: "ab", modelIdentifier: "c"));
        var second = EmbeddingProfileFingerprint.Compute(Identity(provider: "a", modelIdentifier: "bc"));

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>The digest is a fixed-width value the schema stores and activation compares, in one spelling only.</summary>
    [Fact]
    public void Compute_AnyGeometry_ProducesLowercaseHexadecimalOfTheDeclaredLength()
    {
        // Act
        var fingerprint = EmbeddingProfileFingerprint.Compute(Identity());

        // Assert
        Assert.Equal(EmbeddingProfileFingerprint.Length, fingerprint.Value.Length);
        Assert.All(fingerprint.Value, character => Assert.True(
            "0123456789abcdef".Contains(character, StringComparison.Ordinal),
            "A digest is written in lowercase hexadecimal only."));
    }

    /// <summary>
    /// The profile table is unique on this value, so the encoding is pinned rather than merely self-consistent: a
    /// deployment upgraded to a build whose digest writer moved must resolve its registered profile rather than write a
    /// second row and re-embed a whole mailbox against it. Only a digest written down outside the code can say so, which
    /// is why this expectation is a literal and not a second computation.
    /// </summary>
    [Fact]
    public void Compute_TheReferenceGeometry_ProducesTheDigestAlreadyRegisteredRowsCarry()
    {
        // Arrange
        const string registeredDigest = "dcf17af40c4cd8b674567539820590850813269ddd23b43009c3189e8f931238";

        // Act
        var fingerprint = EmbeddingProfileFingerprint.Compute(Identity());

        // Assert
        Assert.Equal(registeredDigest, fingerprint.Value);
    }

    /// <summary>A fingerprint read back from a registered profile is the one that was written.</summary>
    [Fact]
    public void Create_ADigestThatWasComputed_ReadsBackAsTheSameValue()
    {
        // Arrange
        var computed = EmbeddingProfileFingerprint.Compute(Identity());

        // Act
        var readBack = EmbeddingProfileFingerprint.Create(computed.Value);

        // Assert
        Assert.Equal(computed, readBack);
        Assert.Equal(computed.Value, readBack.ToString());
    }

    /// <summary>Anything that is not a digest would compare unequal to every real one without ever saying why.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("A0B1C2D3E4F50617A8B9CADBECFD0E1FA2B3C4D5E6F708192A3B4C5D6E7F8091")]
    [InlineData("zz00000000000000000000000000000000000000000000000000000000000000")]
    public void Create_AValueThatIsNotADigest_IsRefused(string value)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => EmbeddingProfileFingerprint.Create(value));
    }

    /// <summary>Nothing can be identified from arguments that are not there.</summary>
    [Fact]
    public void Compute_MissingArgument_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => EmbeddingProfileFingerprint.Compute(null!));
        Assert.Throws<ArgumentNullException>(() => EmbeddingProfileFingerprint.Create(null!));
    }

    public static TheoryData<EmbeddingProfileIdentity> IdentitiesDifferingFromTheReference() =>
    [
        Identity(provider: "other-vendor"),
        Identity(modelIdentifier: "text-embedding-3-large"),
        Identity(modelVersion: "2026-02-01"),
        Identity(dimension: 3072),
        Identity(distanceMetric: EmbeddingDistanceMetric.InnerProduct),
        Identity(inputCharacterLimit: 4000),
        Identity(passageInstruction: "Represent this passage for retrieval:"),
        Identity(normalizesVector: false),
    ];

    private static EmbeddingProfileIdentity Identity(
        string provider = "example-vendor",
        string modelIdentifier = "text-embedding-3-small",
        string? modelVersion = "2026-01-01",
        int dimension = 1536,
        EmbeddingDistanceMetric distanceMetric = EmbeddingDistanceMetric.Cosine,
        int inputCharacterLimit = 8000,
        string? passageInstruction = null,
        bool normalizesVector = true) =>
        EmbeddingProfileIdentity.Create(
            provider,
            modelIdentifier,
            modelVersion,
            dimension,
            distanceMetric,
            EmbeddingInputPreparation.Create(inputCharacterLimit, passageInstruction, normalizesVector));
}
