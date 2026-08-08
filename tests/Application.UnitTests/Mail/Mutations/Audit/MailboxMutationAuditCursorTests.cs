// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Audit;

/// <summary>Covers the boundary one page of an audit trail hands to the next.</summary>
public sealed class MailboxMutationAuditCursorTests
{
    private const string Fingerprint = "0123456789abcdef";

    private static readonly DateTimeOffset CompletedAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A cursor survives the round trip through the opaque text a caller presents.</summary>
    [Fact]
    public void TryDecode_TextThisVersionEncoded_RestoresTheBoundary()
    {
        // Arrange
        var entry = Entry();
        var encoded = MailboxMutationAuditCursor.After(entry, Fingerprint).Encode();

        // Act
        var decoded = MailboxMutationAuditCursor.TryDecode(encoded, out var cursor);

        // Assert
        Assert.Equal(
            (true, entry.CompletedAt, entry.Id, Fingerprint),
            (decoded, cursor.CompletedAt, cursor.EntryId, cursor.FilterFingerprint));
    }

    /// <summary>An instant written in another offset encodes identically, so a boundary never depends on the offset.</summary>
    [Fact]
    public void Encode_SameInstantInAnotherOffset_ProducesTheSameCursor()
    {
        // Arrange
        var entry = Entry();
        var shifted = entry with { CompletedAt = entry.CompletedAt.ToOffset(TimeSpan.FromHours(2)) };

        // Act
        var encodings = new[]
        {
            MailboxMutationAuditCursor.After(entry, Fingerprint).Encode(),
            MailboxMutationAuditCursor.After(shifted, Fingerprint).Encode(),
        };

        // Assert
        Assert.Equal(encodings[0], encodings[1]);
    }

    /// <summary>Text this system never issued names no boundary, and is reported rather than partly decoded.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not a cursor")]
    [InlineData("MC5ub3BlLm5vcGUubm9wZQ")]
    public void TryDecode_TextThisSystemNeverIssued_IsRefused(string text)
    {
        // Act
        var decoded = MailboxMutationAuditCursor.TryDecode(text, out _);

        // Assert
        Assert.False(decoded);
    }

    /// <summary>A cursor longer than any this version issues is refused before it is decoded at all.</summary>
    [Fact]
    public void TryDecode_TextLongerThanAnyCursorThisVersionIssues_IsRefusedUnread()
    {
        // Arrange
        var overlong = new string('A', MailboxMutationAuditCursor.MaximumEncodedLength + 1);

        // Act
        var decoded = MailboxMutationAuditCursor.TryDecode(overlong, out _);

        // Assert
        Assert.False(decoded);
    }

    private static MailboxMutationAuditEntry Entry() => new()
    {
        Id = MailboxMutationAuditEntryId.Create(Guid.CreateVersion7(CompletedAt)),
        MutationRecordId = MailboxMutationRecordId.Create(Guid.CreateVersion7(CompletedAt)),
        AccountId = MailAccountId.Create("work"),
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7(CompletedAt)),
        Mutation = MailboxMutation.Relocate,
        SourceFolderPath = RemoteFolderPath.Create("INBOX"),
        SourceUidValidity = ImapUidValidity.Create(1),
        SourceUid = ImapUid.Create(41),
        DestinationFolderPath = RemoteFolderPath.Create("Archive", '/'),
        Placement = RemoteEmailPlacement.NotReported(),
        DesiredSeenState = null,
        Requester = MailboxMutationRequester.Rule("file-newsletters", 3),
        RequestedAt = CompletedAt.AddMinutes(-1),
        CompletedAt = CompletedAt,
        Outcome = MailboxMutationAuditOutcome.Performed,
        Failure = (MailFathomErrorCode?)null,
    };
}
