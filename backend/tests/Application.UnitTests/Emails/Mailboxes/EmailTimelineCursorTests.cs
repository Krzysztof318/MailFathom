// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Paging;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Mailboxes;

/// <summary>Covers the continuation cursor over its own position type, dated and undated alike.</summary>
/// <remarks>
/// The encoding is <see cref="KeysetCursorPayload" />'s and is covered once beside it, refusals included. What is
/// asserted here is this reading's own half: the recorded text a client may be part-way through, and the fact that this
/// is the one timeline whose rows need not carry an instant — a message no header could date is still on it.
/// </remarks>
public sealed class EmailTimelineCursorTests
{
    private const string Fingerprint = "AAECAwQFBgcICQoLDA0ODw";

    private const string RecordedDatedCursor =
        "MS42MzkyMjcyODYwMDAwMDAwMDAuMDE5ODkzZTU2YWQwN2JkMDlmMTE2YzNhMWQ1ZTRiMjYuQUFFQ0F3UUZCZ2NJQ1FvTERBME9Edw";

    private const string RecordedUndatedCursor =
        "MS4tLjAxOTg5M2U1NmFkMDdiZDA5ZjExNmMzYTFkNWU0YjI2LkFBRUNBd1FGQmdjSUNRb0xEQTBPRHc";

    private static readonly DateTimeOffset ReceivedAt = new(2026, 8, 19, 9, 30, 0, TimeSpan.Zero);

    private static readonly StoredEmailId BoundaryId =
        StoredEmailId.Create(new Guid("019893e5-6ad0-7bd0-9f11-6c3a1d5e4b26"));

    /// <summary>A client holds this text between two pages, so it is pinned rather than only compared with itself.</summary>
    [Fact]
    public void Encode_ARecordedDatedPosition_RoundTripsThroughTheTextThisTimelineHasAlwaysIssued()
    {
        // Arrange
        var position = new EmailTimelinePosition(ReceivedAt, BoundaryId);

        // Act
        var encoded = EmailTimelineCursor.After(position, Fingerprint).Encode();
        var read = EmailTimelineCursor.TryDecode(RecordedDatedCursor, out var cursor);

        // Assert
        Assert.Equal(RecordedDatedCursor, encoded);
        Assert.True(read);
        Assert.Equal(position, cursor.Position);
        Assert.Equal(Fingerprint, cursor.FilterFingerprint);
    }

    /// <summary>A message no header could date still sits on the timeline, so its boundary encodes and reads back too.</summary>
    [Fact]
    public void Encode_ARecordedUndatedPosition_RoundTripsThroughTheSentinelTextItHasAlwaysIssued()
    {
        // Arrange
        var position = new EmailTimelinePosition(null, BoundaryId);

        // Act
        var encoded = EmailTimelineCursor.After(position, Fingerprint).Encode();
        var read = EmailTimelineCursor.TryDecode(RecordedUndatedCursor, out var cursor);

        // Assert
        Assert.Equal(RecordedUndatedCursor, encoded);
        Assert.True(read);
        Assert.Null(cursor.Position.ReceivedAt);
        Assert.Equal(BoundaryId, cursor.Position.StoredEmailId);
    }

    [Fact]
    public void After_PositionWithoutAStoredEmailIdentity_IsRejected()
    {
        // Act, Assert
        var failure = Assert.Throws<ArgumentException>(() =>
            EmailTimelineCursor.After(new EmailTimelinePosition(null, default), Fingerprint));

        Assert.Equal("position", failure.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void After_BlankFingerprint_IsRejected(string fingerprint)
    {
        // Arrange
        var position = new EmailTimelinePosition(null, BoundaryId);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => EmailTimelineCursor.After(position, fingerprint));
    }

    [Fact]
    public void After_NoFingerprint_IsRejected()
    {
        // Arrange
        var position = new EmailTimelinePosition(null, BoundaryId);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => EmailTimelineCursor.After(position, null!));
    }
}
