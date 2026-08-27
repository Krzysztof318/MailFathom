// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Globalization;
using System.Text;
using MailFathom.Application.Emails.BrowseSearch;
using MailFathom.Application.Emails.Search;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.BrowseSearch;

/// <summary>Covers the cases the ranked cursor's own format decides, beside the rules stated over every input.</summary>
/// <remarks>
/// The properties beside this file say that what a cursor carries survives the encoding. What an example is for is the
/// text a caller may present that no encoder here produced — an earlier format's, a damaged one, a boundary composed
/// by hand — and the arguments a caller of the application layer may pass that name no place in a ranking.
/// </remarks>
public sealed class RankedSearchCursorTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private const string Fingerprint = "0123456789abcdef";

    /// <summary>The three fields a boundary is, read back exactly as the page that ended on it held them.</summary>
    [Fact]
    public void Encode_ABoundaryOverDatedMail_ReadsBackTheSameScorePositionAndFingerprint()
    {
        // Arrange
        var candidate = new RankedEmailCandidate(
            new EmailTimelinePosition(FirstJuly, StoredEmailId.Create(Guid.CreateVersion7())),
            0.078_431_37f);

        // Act
        var read = RankedSearchCursor.TryDecode(RankedSearchCursor.After(candidate, Fingerprint).Encode(), out var cursor);

        // Assert
        Assert.True(read);
        Assert.Equal(candidate.Score, cursor.Score);
        Assert.Equal(candidate.Position, cursor.Position);
        Assert.Equal(Fingerprint, cursor.FilterFingerprint);
    }

    /// <summary>A message no header could date still matches a query, so a boundary over one has to survive the round trip.</summary>
    [Fact]
    public void Encode_ABoundaryOverUndatedMail_ReadsBackAPositionWithNoInstant()
    {
        // Arrange
        var candidate = new RankedEmailCandidate(
            new EmailTimelinePosition(ReceivedAt: null, StoredEmailId.Create(Guid.CreateVersion7())),
            0.5f);

        // Act
        var read = RankedSearchCursor.TryDecode(RankedSearchCursor.After(candidate, Fingerprint).Encode(), out var cursor);

        // Assert
        Assert.True(read);
        Assert.Null(cursor.Position.ReceivedAt);
        Assert.Equal(candidate.Position.StoredEmailId, cursor.Position.StoredEmailId);
    }

    /// <summary>The boundary is handed back in the ranking's own words, which is what a continuation compares candidates against.</summary>
    [Fact]
    public void Boundary_ADecodedCursor_NamesThePlaceTheRankingPutTheLastResult()
    {
        // Arrange
        var candidate = new RankedEmailCandidate(
            new EmailTimelinePosition(FirstJuly, StoredEmailId.Create(Guid.CreateVersion7())),
            0.25f);

        // Act
        RankedSearchCursor.TryDecode(RankedSearchCursor.After(candidate, Fingerprint).Encode(), out var cursor);

        // Assert
        Assert.Equal(candidate, cursor.Boundary);
    }

    /// <summary>A cursor is opaque, so anything at all may arrive here — and what is not one this version issued is refused rather than half read.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not a cursor")]
    [InlineData("...")]
    public void TryDecode_TextThisVersionNeverIssued_IsRefused(string text)
    {
        // Act
        var read = RankedSearchCursor.TryDecode(text, out _);

        // Assert
        Assert.False(read);
    }

    /// <summary>The version leads the payload so a later change to the fields refuses these cursors instead of misreading them.</summary>
    [Fact]
    public void TryDecode_ACursorOfAnotherFormatVersion_IsRefused()
    {
        // Arrange
        var identity = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        var payload = $"2.1065353216.{FirstJuly.UtcTicks}.{identity}.{Fingerprint}";

        // Act
        var read = RankedSearchCursor.TryDecode(Encoded(payload), out _);

        // Assert
        Assert.False(read);
    }

    /// <summary>Every field is validated before a cursor is produced, so a boundary cannot reach a query having decoded only partly.</summary>
    [Theory]
    [InlineData("1.1065353216.0.00000000000000000000000000000000.fingerprint")]
    [InlineData("1.-1.0.6f2f1f0e6a1a4a2a9d1f0f5a1b2c3d4e.fingerprint")]
    [InlineData("1.4286578688.0.6f2f1f0e6a1a4a2a9d1f0f5a1b2c3d4e.fingerprint")]
    [InlineData("1.1065353216.-1.6f2f1f0e6a1a4a2a9d1f0f5a1b2c3d4e.fingerprint")]
    [InlineData("1.1065353216.0.6f2f1f0e6a1a4a2a9d1f0f5a1b2c3d4e.")]
    [InlineData("1.1065353216.0.6f2f1f0e6a1a4a2a9d1f0f5a1b2c3d4e")]
    public void TryDecode_APayloadWithAFieldNamingNoBoundary_IsRefused(string payload)
    {
        // Act
        var read = RankedSearchCursor.TryDecode(Encoded(payload), out _);

        // Assert
        Assert.False(read);
    }

    /// <summary>The bound is applied before a byte is decoded, because a decoder is the wrong place to discover an input is absurd.</summary>
    [Fact]
    public void TryDecode_TextLongerThanAnyCursorThisVersionIssues_IsRefusedUnread()
    {
        // Act
        var read = RankedSearchCursor.TryDecode(new string('a', 4096), out _);

        // Assert
        Assert.False(read);
    }

    /// <summary>A candidate with no identity names no boundary, because the identity is what makes the ranked order total.</summary>
    /// <remarks>The position is the struct default, which is the one way such a candidate is reachable: the domain type refuses an empty identifier at its own boundary.</remarks>
    [Fact]
    public void After_ACandidateWithNoStoredIdentity_IsRefused()
    {
        // Arrange
        var candidate = new RankedEmailCandidate(default, 0.5f);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => RankedSearchCursor.After(candidate, Fingerprint));
    }

    /// <summary>Every ranking here scores finitely and never below zero, so a value outside that was composed rather than ranked.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-0.5f)]
    public void After_ACandidateScoredOutsideWhatARankingProduces_IsRefused(float score)
    {
        // Arrange
        var candidate = new RankedEmailCandidate(
            new EmailTimelinePosition(FirstJuly, StoredEmailId.Create(Guid.CreateVersion7())),
            score);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => RankedSearchCursor.After(candidate, Fingerprint));
    }

    /// <summary>A boundary with no list behind it would be accepted by whichever search happened to receive it.</summary>
    [Fact]
    public void After_ABlankFingerprint_IsRefused()
    {
        // Arrange
        var candidate = new RankedEmailCandidate(
            new EmailTimelinePosition(FirstJuly, StoredEmailId.Create(Guid.CreateVersion7())),
            0.5f);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => RankedSearchCursor.After(candidate, "  "));
    }

    private static string Encoded(string payload) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
}
