// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.BrowseThread;
using MailFathom.Application.Paging;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.BrowseThread;

/// <summary>Covers the boundary one page of a conversation ends on, and what it refuses to be built from.</summary>
/// <remarks>
/// The encoding is <see cref="KeysetCursorPayload" />'s and is covered once beside it, refusals included. What is
/// asserted here is this reading's own half: the recorded text a client may be part-way through, the conversation a
/// boundary belongs to, and the fact that a place in a conversation is not an instant.
/// </remarks>
public sealed class EmailThreadCursorTests
{
    private const string RecordedCursor =
        "MS4tLjAxOTg5M2U1NmFkMDdiZDA5ZjExNmMzYTFkNWU0YjI2LjhhODM2NjVmMzc5ODcyN2Y";

    private static readonly EmailThreadId Conversation =
        EmailThreadId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    private static readonly EmailThreadId OtherConversation =
        EmailThreadId.Create(new Guid("22222222-2222-2222-2222-222222222222"));

    private static readonly StoredEmailId BoundaryId =
        StoredEmailId.Create(new Guid("019893e5-6ad0-7bd0-9f11-6c3a1d5e4b26"));

    /// <summary>A client holds this text between two pages, so it is pinned rather than only compared with itself.</summary>
    [Fact]
    public void Encode_ARecordedBoundary_RoundTripsThroughTheTextThisReadingHasAlwaysIssued()
    {
        // Arrange
        var fingerprint = EmailThreadCursor.FingerprintOf(Conversation);

        // Act
        var encoded = EmailThreadCursor.After(BoundaryId, fingerprint).Encode();
        var read = EmailThreadCursor.TryDecode(RecordedCursor, out var cursor);

        // Assert
        Assert.Equal(RecordedCursor, encoded);
        Assert.True(read);
        Assert.Equal(BoundaryId, cursor.StoredEmailId);
        Assert.Equal(fingerprint, cursor.ThreadFingerprint);
    }

    /// <summary>A boundary belongs to one conversation, so two conversations must not reduce to one fingerprint.</summary>
    [Fact]
    public void FingerprintOf_TwoConversations_ReducesEachToAFingerprintOfItsOwn()
    {
        // Act
        var fingerprint = EmailThreadCursor.FingerprintOf(Conversation);
        var other = EmailThreadCursor.FingerprintOf(OtherConversation);

        // Assert
        Assert.NotEqual(fingerprint, other);
        Assert.Equal(fingerprint, EmailThreadCursor.FingerprintOf(Conversation));
    }

    /// <summary>The place a message holds in a conversation is not an instant, so a payload carrying one was built rather than issued.</summary>
    [Fact]
    public void TryDecode_APayloadCarryingAnInstant_IsRefused()
    {
        // Arrange
        var carryingAnInstant = KeysetCursorPayload
            .At(new DateTimeOffset(2026, 8, 19, 9, 30, 0, TimeSpan.Zero), BoundaryId.Value, "8a83665f3798727f")
            .Encode();

        // Act
        var read = EmailThreadCursor.TryDecode(carryingAnInstant, out var cursor);

        // Assert
        Assert.False(read);
        Assert.Equal(default, cursor);
    }

    /// <summary>Text no page of this reading ever issued names no boundary, whatever else it looks like.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    public void TryDecode_TextThisReadingNeverIssued_IsRefused(string? text)
    {
        // Act
        var read = EmailThreadCursor.TryDecode(text, out _);

        // Assert
        Assert.False(read);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void After_BlankFingerprint_IsRejected(string fingerprint)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => EmailThreadCursor.After(BoundaryId, fingerprint));
    }

    [Fact]
    public void After_NoFingerprint_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => EmailThreadCursor.After(BoundaryId, null!));
    }
}
