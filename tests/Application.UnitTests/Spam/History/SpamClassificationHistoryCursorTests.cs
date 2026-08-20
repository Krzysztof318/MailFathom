// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;
using MailFathom.Application.Spam.History;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.History;

/// <summary>Covers the boundary one page of an account's classifications hands to the next, over its own identity.</summary>
/// <remarks>
/// The encoding is <see cref="KeysetCursorPayload" />'s and is covered once beside it, refusals included. What is
/// asserted here is this reading's own half: the recorded text a client may be part-way through, the occurrence identity
/// it reads back, and the boundary shape this reading has no row for.
/// </remarks>
public sealed class SpamClassificationHistoryCursorTests
{
    private const string Fingerprint = "abcdef0123456789";

    private const string RecordedCursor =
        "MS42MzkyMjcyODYwMDAwMDAwMDAuMDE5ODkzZTU2YWQwN2JkMDlmMTE2YzNhMWQ1ZTRiMjIuYWJjZGVmMDEyMzQ1Njc4OQ";

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 19, 9, 30, 0, TimeSpan.Zero);

    private static readonly StoredEmailId Email =
        StoredEmailId.Create(new Guid("019893e5-6ad0-7bd0-9f11-6c3a1d5e4b22"));

    /// <summary>A client holds this text between two pages, so it is pinned rather than only compared with itself.</summary>
    [Fact]
    public void Encode_ARecordedBoundary_RoundTripsThroughTheTextThisReadingHasAlwaysIssued()
    {
        // Act
        var encoded = SpamClassificationHistoryCursor.After(EvaluatedAt, Email, Fingerprint).Encode();
        var read = SpamClassificationHistoryCursor.TryDecode(RecordedCursor, out var cursor);

        // Assert
        Assert.Equal(RecordedCursor, encoded);
        Assert.True(read);
        Assert.NotNull(cursor);
        Assert.Equal(EvaluatedAt, cursor.Value.EvaluatedAt);
        Assert.Equal(Email, cursor.Value.EmailId);
        Assert.Equal(Fingerprint, cursor.Value.FilterFingerprint);
    }

    /// <summary>Every classification this reading returns was evaluated at a known instant, so a payload with none names nothing here.</summary>
    [Fact]
    public void TryDecode_APayloadCarryingNoPosition_IsRefused()
    {
        // Arrange
        var withoutPosition = KeysetCursorPayload.At(null, Email.Value, Fingerprint).Encode();

        // Act
        var read = SpamClassificationHistoryCursor.TryDecode(withoutPosition, out var cursor);

        // Assert
        Assert.False(read);
        Assert.Null(cursor);
    }

    /// <summary>A cursor proves which walk it belongs to, so it is refused without the fingerprint that says so.</summary>
    [Fact]
    public void After_ABlankFingerprint_IsRefused()
    {
        // Act, Assert
        var failure = Assert.Throws<ArgumentException>(
            () => SpamClassificationHistoryCursor.After(EvaluatedAt, Email, "   "));

        Assert.Equal("filterFingerprint", failure.ParamName);
    }
}
