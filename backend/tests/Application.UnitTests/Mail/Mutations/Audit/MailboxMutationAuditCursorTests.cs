// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Paging;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Audit;

/// <summary>Covers the boundary one page of an audit trail hands to the next, over its own identity.</summary>
/// <remarks>
/// The encoding is <see cref="KeysetCursorPayload" />'s and is covered once beside it, refusals included. What is
/// asserted here is this reading's own half: the recorded text a client may be part-way through, the entry identity it
/// reads back, and the boundary shape this reading has no row for.
/// </remarks>
public sealed class MailboxMutationAuditCursorTests
{
    private const string Fingerprint = "abcdef0123456789";

    private const string RecordedCursor =
        "MS42MzkyMjcyODYwMDAwMDAwMDAuMDE5ODkzZTU2YWQwN2JkMDlmMTE2YzNhMWQ1ZTRiMjQuYWJjZGVmMDEyMzQ1Njc4OQ";

    private static readonly DateTimeOffset CompletedAt = new(2026, 8, 19, 9, 30, 0, TimeSpan.Zero);

    private static readonly MailboxMutationAuditEntryId EntryId =
        MailboxMutationAuditEntryId.Create(new Guid("019893e5-6ad0-7bd0-9f11-6c3a1d5e4b24"));

    /// <summary>A client holds this text between two pages, so it is pinned rather than only compared with itself.</summary>
    /// <remarks>The entry overload is what a page actually advances by, so it is the one the recorded text is issued from.</remarks>
    [Fact]
    public void Encode_ARecordedBoundary_RoundTripsThroughTheTextThisTrailHasAlwaysIssued()
    {
        // Act
        var encoded = MailboxMutationAuditCursor.After(Entry(), Fingerprint).Encode();
        var read = MailboxMutationAuditCursor.TryDecode(RecordedCursor, out var cursor);

        // Assert
        Assert.Equal(RecordedCursor, encoded);
        Assert.True(read);
        Assert.NotNull(cursor);
        Assert.Equal(CompletedAt, cursor.Value.CompletedAt);
        Assert.Equal(EntryId, cursor.Value.EntryId);
        Assert.Equal(Fingerprint, cursor.Value.FilterFingerprint);
    }

    /// <summary>Every entry this trail returns completed at a known instant, so a payload with none names nothing here.</summary>
    [Fact]
    public void TryDecode_APayloadCarryingNoPosition_IsRefused()
    {
        // Arrange
        var withoutPosition = KeysetCursorPayload.At(null, EntryId.Value, Fingerprint).Encode();

        // Act
        var read = MailboxMutationAuditCursor.TryDecode(withoutPosition, out var cursor);

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
            () => MailboxMutationAuditCursor.After(CompletedAt, EntryId, "   "));

        Assert.Equal("filterFingerprint", failure.ParamName);
    }

    /// <summary>A page advances by the rows it read, so the overload taking one is refused nothing to advance from.</summary>
    [Fact]
    public void After_NoEntry_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => MailboxMutationAuditCursor.After(null!, Fingerprint));
    }

    private static MailboxMutationAuditEntry Entry() => new()
    {
        Id = EntryId,
        MutationRecordId = MailboxMutationRecordId.Create(Guid.CreateVersion7(CompletedAt)),
        Owner = SyntheticMailOwner.Deployment,
        AccountId = MailAccountId.Create("work"),
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7(CompletedAt)),
        Mutation = MailboxMutation.Relocate,
        SourceFolderPath = RemoteFolderPath.Create("INBOX"),
        SourceUidValidity = ImapUidValidity.Create(1),
        SourceUid = ImapUid.Create(41),
        DestinationFolderPath = RemoteFolderPath.Create("Archive", '/'),
        Placement = RemoteEmailPlacement.NotReported(),
        DesiredSeenState = null,
        Requester = MailboxMutationRequester.Rule("file-newsletters", "3"),
        RequestedAt = CompletedAt.AddMinutes(-1),
        CompletedAt = CompletedAt,
        Outcome = MailboxMutationAuditOutcome.Performed,
        Failure = (MailFathomErrorCode?)null,
    };
}
