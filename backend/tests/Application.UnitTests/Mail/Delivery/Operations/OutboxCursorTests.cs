// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Paging;
using MailFathom.Domain.Delivery;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Operations;

/// <summary>Covers what an outbox cursor carries across an encoding, over its own position and its own identity.</summary>
/// <remarks>
/// The encoding is <see cref="KeysetCursorPayload" />'s and is covered once beside it, refusals included. What is
/// asserted here is this reading's own half: the recorded text a client may be part-way through, the send identity it
/// reads back, and the boundary shape this reading has no row for.
/// </remarks>
public sealed class OutboxCursorTests
{
    private const string Fingerprint = "abcdef0123456789";

    private const string RecordedCursor =
        "MS42MzkyMjcyODYwMDAwMDAwMDAuMDE5ODkzZTU2YWQwN2JkMDlmMTE2YzNhMWQ1ZTRiMjAuYWJjZGVmMDEyMzQ1Njc4OQ";

    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 19, 9, 30, 0, TimeSpan.Zero);

    private static readonly OutgoingEmailId Send =
        OutgoingEmailId.Create(new Guid("019893e5-6ad0-7bd0-9f11-6c3a1d5e4b20"));

    /// <summary>A client holds this text between two pages, so it is pinned rather than only compared with itself.</summary>
    [Fact]
    public void Encode_ARecordedBoundary_RoundTripsThroughTheTextThisReadingHasAlwaysIssued()
    {
        // Act
        var encoded = OutboxCursor.After(RecordedAt, Send, Fingerprint).Encode();
        var read = OutboxCursor.TryDecode(RecordedCursor, out var cursor);

        // Assert
        Assert.Equal(RecordedCursor, encoded);
        Assert.True(read);
        Assert.NotNull(cursor);
        Assert.Equal(RecordedAt, cursor.Value.RecordedAt);
        Assert.Equal(Send, cursor.Value.OutgoingEmailId);
        Assert.Equal(Fingerprint, cursor.Value.FilterFingerprint);
    }

    /// <summary>Every send this reading returns was written down at a known instant, so a payload with none names nothing here.</summary>
    [Fact]
    public void TryDecode_APayloadCarryingNoPosition_IsRefused()
    {
        // Arrange
        var withoutPosition = KeysetCursorPayload.At(null, Send.Value, Fingerprint).Encode();

        // Act
        var read = OutboxCursor.TryDecode(withoutPosition, out var cursor);

        // Assert
        Assert.False(read);
        Assert.Null(cursor);
    }

    /// <summary>A cursor proves which walk it belongs to, so it is refused without the fingerprint that says so.</summary>
    [Fact]
    public void After_ABlankFingerprint_IsRefused()
    {
        // Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => OutboxCursor.After(RecordedAt, Send, "  "));

        Assert.Equal("filterFingerprint", failure.ParamName);
    }
}
