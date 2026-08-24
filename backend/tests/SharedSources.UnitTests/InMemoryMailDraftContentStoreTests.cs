// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the content store several suites keep a draft's message in.</summary>
/// <remarks>
/// What the draft half has to get right is that an edit replaces the message rather than adding one, and that what
/// comes back describes the payload accurately enough for an integrity check to pass — a double recording a length or
/// a digest of something else would let a suite prove a corruption check that never ran. The other six members are
/// refusals, and a refusal that quietly answered instead would let a suite pass while reaching mail a draft never
/// touches.
/// </remarks>
public sealed class InMemoryMailDraftContentStoreTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly IPersistenceSession Session = new IgnoredPersistenceSession();

    [Fact]
    public async Task FindMailDraftContentAsync_AMessageItStored_AnswersThePayloadItWasGiven()
    {
        // Arrange
        var store = new InMemoryMailDraftContentStore();
        var draftId = NewDraftId();
        var message = "From: writer@example.test\r\n\r\nShall we?"u8.ToArray();

        await store.SaveMailDraftContentAsync(
            Session,
            draftId,
            PlacedEmailContent.InDatabase(message),
            TestContext.Current.CancellationToken);

        // Act
        var stored = await store.FindMailDraftContentAsync(draftId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(stored);
        Assert.Equal(message, stored.RawMime.ToArray());
        Assert.Equal(message, store.Peek(draftId).ToArray());
    }

    /// <summary>The recorded length and digest describe the payload, which is what an integrity check reads them for.</summary>
    [Fact]
    public async Task FindMailDraftContentAsync_AMessageItStored_RecordsALengthAndDigestOfThatMessage()
    {
        // Arrange
        var store = new InMemoryMailDraftContentStore();
        var draftId = NewDraftId();

        await store.SaveMailDraftContentAsync(
            Session,
            draftId,
            PlacedEmailContent.InDatabase("Shall we?"u8.ToArray()),
            TestContext.Current.CancellationToken);

        // Act
        var stored = await store.FindMailDraftContentAsync(draftId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(stored!.FindIntegrityDefect());
    }

    /// <summary>An edit replaces one draft's message, so what is stored is the newest revision and nothing beside it.</summary>
    [Fact]
    public async Task SaveMailDraftContentAsync_ASecondRevisionOfOneDraft_ReplacesTheMessageRatherThanAddingOne()
    {
        // Arrange
        var store = new InMemoryMailDraftContentStore();
        var draftId = NewDraftId();

        await store.SaveMailDraftContentAsync(
            Session,
            draftId,
            PlacedEmailContent.InDatabase("Shall we?"u8.ToArray()),
            TestContext.Current.CancellationToken);

        // Act
        await store.SaveMailDraftContentAsync(
            Session,
            draftId,
            PlacedEmailContent.InDatabase("Shall we make it Friday?"u8.ToArray()),
            TestContext.Current.CancellationToken);

        // Assert
        var stored = await store.FindMailDraftContentAsync(draftId, TestContext.Current.CancellationToken);
        Assert.Equal("Shall we make it Friday?"u8.ToArray(), stored!.RawMime.ToArray());
        Assert.Equal(2, store.WriteCount);
    }

    /// <summary>What the caller handed over is copied, so a buffer it reuses afterwards does not rewrite a stored draft.</summary>
    [Fact]
    public async Task SaveMailDraftContentAsync_ABufferTheCallerReuses_LeavesTheStoredMessageAsItWasWritten()
    {
        // Arrange
        var store = new InMemoryMailDraftContentStore();
        var draftId = NewDraftId();
        var buffer = "Shall we?"u8.ToArray();

        await store.SaveMailDraftContentAsync(
            Session,
            draftId,
            PlacedEmailContent.InDatabase(buffer),
            TestContext.Current.CancellationToken);

        // Act
        buffer[0] = (byte)'s';

        // Assert
        Assert.Equal("Shall we?"u8.ToArray(), store.Peek(draftId).ToArray());
    }

    [Fact]
    public async Task FindMailDraftContentAsync_ADraftNothingWasStoredFor_AnswersNothing()
    {
        // Arrange
        var store = new InMemoryMailDraftContentStore();

        // Act
        var stored = await store.FindMailDraftContentAsync(NewDraftId(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(stored);
        Assert.True(store.Peek(NewDraftId()).IsEmpty);
        Assert.Equal(0, store.WriteCount);
    }

    /// <summary>
    /// The placement answers what the database backend answers, because that is the store this double stands in for.
    /// A suite reading anything else out of it would be proving the object backend's shape against a double that never
    /// reaches one.
    /// </summary>
    [Fact]
    public async Task PlaceContentAsync_APayload_AnswersADatabasePlacementDescribingIt()
    {
        // Arrange
        var store = new InMemoryMailDraftContentStore();
        var message = "Shall we?"u8.ToArray();

        // Act
        var placed = await store.PlaceContentAsync(
            EmailContentKind.MailDraft,
            message,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContentStorageBackend.Database, placed.Backend);
        Assert.Null(placed.ObjectLocator);
        Assert.Equal(message, placed.RawMime.ToArray());
        Assert.Equal(1, store.PlacementCount);
    }

    /// <summary>
    /// The count is what a suite reads to prove a replayed unit of work placed nothing again, so it counts placements
    /// rather than writes: the two differ by exactly the number of attempts a commit took.
    /// </summary>
    [Fact]
    public async Task PlacementCount_ARevisionStoredTwiceFromOnePlacement_CountsThePlacementRatherThanTheWrites()
    {
        // Arrange
        var store = new InMemoryMailDraftContentStore();
        var draftId = NewDraftId();

        var placed = await store.PlaceContentAsync(
            EmailContentKind.MailDraft,
            "Shall we?"u8.ToArray(),
            TestContext.Current.CancellationToken);

        // Act
        await store.SaveMailDraftContentAsync(Session, draftId, placed, TestContext.Current.CancellationToken);
        await store.SaveMailDraftContentAsync(Session, draftId, placed, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, store.PlacementCount);
        Assert.Equal(2, store.WriteCount);
    }

    /// <summary>
    /// The moment a placement happens is the claim a suite about write ordering makes, and a count taken afterwards
    /// cannot tell a placement made before a unit of work from one made inside it.
    /// </summary>
    [Fact]
    public async Task Placing_APayloadBeingPlaced_RunsAtTheMomentOfThePlacement()
    {
        // Arrange
        var store = new InMemoryMailDraftContentStore();
        var countWhenObserved = -1;
        store.Placing = () => countWhenObserved = store.PlacementCount;

        // Act
        await store.PlaceContentAsync(
            EmailContentKind.MailDraft,
            "Shall we?"u8.ToArray(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, countWhenObserved);
    }

    /// <summary>Every member outside the draft half is a refusal, so a suite reaching one fails where it reached it.</summary>
    [Fact]
    public async Task EveryMemberOutsideTheDraftHalf_IsRefusedRatherThanAnsweredForSilently()
    {
        // Arrange
        var store = new InMemoryMailDraftContentStore();
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7(Moment));
        var outgoingEmailId = OutgoingEmailId.Create(Guid.CreateVersion7(Moment));
        var recurringSendId = RecurringSendId.Create(Guid.CreateVersion7(Moment));
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act, Assert
        var unusedPlacement = PlacedEmailContent.InDatabase("unreachable"u8.ToArray());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => store.SaveContentAsync(Session, storedEmailId, null!, unusedPlacement, cancellationToken));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => store.FindStoredContentAsync(storedEmailId, cancellationToken));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => store.SaveOutgoingContentAsync(Session, outgoingEmailId, unusedPlacement, cancellationToken));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => store.FindOutgoingContentAsync(outgoingEmailId, cancellationToken));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => store.SaveRecurringSendDraftAsync(Session, recurringSendId, unusedPlacement, cancellationToken));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => store.FindRecurringSendDraftAsync(recurringSendId, cancellationToken));
    }

    private static MailDraftId NewDraftId() => MailDraftId.Create(Guid.CreateVersion7(Moment));
}
