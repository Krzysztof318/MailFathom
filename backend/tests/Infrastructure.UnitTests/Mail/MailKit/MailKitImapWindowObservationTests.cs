// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using NSubstitute;
using Xunit;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit;

/// <summary>
/// Covers the three ways a reconciliation window is read: the full scan, the modification-sequence-limited fetch beside
/// a vanished report, and the same fetch beside a UID search. What every one of them has to produce is the same
/// statement about which stored occurrences the folder still holds.
/// </summary>
public sealed class MailKitImapWindowObservationTests
{
    /// <summary>The window every test asks about, which is wider than the set the server is made to answer for.</summary>
    private static readonly ImapUid[] Window =
    [
        ImapUid.Create(10),
        ImapUid.Create(11),
        ImapUid.Create(12),
    ];

    /// <summary>A server with neither capability is the ordinary one, and it is asked about the whole window.</summary>
    [Fact]
    public async Task ObserveWindowWithoutSettingSeenAsync_ServerAdvertisesNeitherCapability_DescribesTheWholeWindow()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.None };
        var folder = CreateSelectedFolder();
        AnswerFetchWith(folder, DescribedUids(10, 12));
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var observation = await session.ObserveWindowWithoutSettingSeenAsync(
            Window,
            reconciledThroughModSeq: 40UL,
            CancellationToken.None);

        // Assert
        Assert.Equal([10U, 12U], observation.Observations.Select(described => described.Uid.Value));
        Assert.Empty(observation.UnchangedUids);

        // Nothing was narrowed, because a narrowed answer would leave the middle UID indistinguishable from a deleted
        // one, and no UID search was issued because the fetch already answers about every UID in the window.
        Assert.Null(RequestedChangedSince(folder));
        await folder.DidNotReceive().SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Without a sequence to reconcile from there is nothing to narrow by, however capable the server is.</summary>
    [Fact]
    public async Task ObserveWindowWithoutSettingSeenAsync_NoStoredSequence_DescribesTheWholeWindow()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.CondStore | ImapCapabilities.QuickResync };
        var folder = CreateSelectedFolder(highestModSeq: 91UL);
        AnswerFetchWith(folder, DescribedUids(10, 11, 12));
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var observation = await session.ObserveWindowWithoutSettingSeenAsync(
            Window,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal([10U, 11U, 12U], observation.Observations.Select(described => described.Uid.Value));
        Assert.Null(RequestedChangedSince(folder));

        // The folder's own sequence is still reported, because it is what a completed pass records so that the next one
        // can narrow at all.
        Assert.Equal(91UL, observation.FolderHighestModSeq);
    }

    /// <summary>
    /// The optimization QRESYNC exists for: the server describes only what changed and names what vanished, and
    /// everything else in the window is a message it has just confirmed unchanged.
    /// </summary>
    [Fact]
    public async Task ObserveWindowWithoutSettingSeenAsync_QuickResyncServer_FetchesOnlyChangesAndConfirmsTheRest()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.CondStore | ImapCapabilities.QuickResync };
        var folder = CreateSelectedFolder(highestModSeq: 91UL);
        AnswerFetchWith(folder, DescribedUids(10), vanishedUids: [12U]);
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var observation = await session.ObserveWindowWithoutSettingSeenAsync(
            Window,
            reconciledThroughModSeq: 40UL,
            CancellationToken.None);

        // Assert
        Assert.Equal([10U], observation.Observations.Select(described => described.Uid.Value));
        Assert.Equal([11U], observation.UnchangedUids.Select(uid => uid.Value));
        Assert.Equal(40UL, RequestedChangedSince(folder));
        Assert.Equal(91UL, observation.FolderHighestModSeq);

        // One command answered both halves, so establishing what still exists cost no second round trip.
        await folder.DidNotReceive().SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A server with modification sequences and no vanished report cannot say what is gone, so existence comes from a
    /// UID search that returns identifiers and no message data.
    /// </summary>
    [Fact]
    public async Task ObserveWindowWithoutSettingSeenAsync_CondStoreWithoutQuickResync_SearchesForSurvivorsAndFetchesOnlyChanges()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.CondStore };
        var folder = CreateSelectedFolder(highestModSeq: 91UL);
        AnswerSearchWith(folder, 10U, 11U);
        AnswerFetchWith(folder, DescribedUids(10));
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var observation = await session.ObserveWindowWithoutSettingSeenAsync(
            Window,
            reconciledThroughModSeq: 40UL,
            CancellationToken.None);

        // Assert
        Assert.Equal([10U], observation.Observations.Select(described => described.Uid.Value));
        Assert.Equal([11U], observation.UnchangedUids.Select(uid => uid.Value));
        Assert.Equal(40UL, RequestedChangedSince(folder));

        // The UID the search did not return is accounted for by neither list, which is what a deleted message looks
        // like to the caller.
        Assert.DoesNotContain(12U, observation.UnchangedUids.Select(uid => uid.Value));
    }

    /// <summary>
    /// The claim this adapter makes about the optimization: whichever path a server's capabilities select, the
    /// caller learns the same thing about which occurrences survived and which are gone.
    /// </summary>
    [Fact]
    public async Task ObserveWindowWithoutSettingSeenAsync_EveryCapabilityCombination_ReportsTheSameSurvivingOccurrences()
    {
        // Arrange
        using var fullScanResilience = CreateSingleAttemptResilience();
        var fullScanClient = new FakeImapClient { Capabilities = ImapCapabilities.None };
        var fullScanFolder = CreateSelectedFolder(highestModSeq: 91UL);
        AnswerFetchWith(fullScanFolder, DescribedUids(10, 11));
        await using var fullScanSession = await OpenSessionAsync(fullScanResilience, fullScanClient, fullScanFolder);

        using var quickResyncResilience = CreateSingleAttemptResilience();
        var quickResyncClient = new FakeImapClient { Capabilities = ImapCapabilities.CondStore | ImapCapabilities.QuickResync };
        var quickResyncFolder = CreateSelectedFolder(highestModSeq: 91UL);
        AnswerFetchWith(quickResyncFolder, DescribedUids(10), vanishedUids: [12U]);
        await using var quickResyncSession = await OpenSessionAsync(quickResyncResilience, quickResyncClient, quickResyncFolder);

        using var condStoreResilience = CreateSingleAttemptResilience();
        var condStoreClient = new FakeImapClient { Capabilities = ImapCapabilities.CondStore };
        var condStoreFolder = CreateSelectedFolder(highestModSeq: 91UL);
        AnswerSearchWith(condStoreFolder, 10U, 11U);
        AnswerFetchWith(condStoreFolder, DescribedUids(10));
        await using var condStoreSession = await OpenSessionAsync(condStoreResilience, condStoreClient, condStoreFolder);

        // Act
        var fullScan = await fullScanSession.ObserveWindowWithoutSettingSeenAsync(Window, 40UL, CancellationToken.None);
        var quickResync = await quickResyncSession.ObserveWindowWithoutSettingSeenAsync(Window, 40UL, CancellationToken.None);
        var condStore = await condStoreSession.ObserveWindowWithoutSettingSeenAsync(Window, 40UL, CancellationToken.None);

        // Assert
        Assert.Equal([10U, 11U], SurvivingUids(fullScan));
        Assert.Equal(SurvivingUids(fullScan), SurvivingUids(quickResync));
        Assert.Equal(SurvivingUids(fullScan), SurvivingUids(condStore));
    }

    /// <summary>
    /// A folder whose server sends no modification sequence reports zero, which is the absence of one rather than a
    /// sequence every later comparison would read as older than everything.
    /// </summary>
    [Fact]
    public async Task ObserveWindowWithoutSettingSeenAsync_FolderReportsNoModificationSequence_RecordsNoneToReconcileFrom()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.CondStore };
        var folder = CreateSelectedFolder();
        AnswerFetchWith(folder, DescribedUids(10));
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var observation = await session.ObserveWindowWithoutSettingSeenAsync(
            Window,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Null(observation.FolderHighestModSeq);
    }

    /// <summary>
    /// RFC 7162 lets a server volunteer summaries for messages another client touched. They belong to no window this
    /// pass selected, so they are dropped rather than reported against a local identity that was never chosen.
    /// </summary>
    [Fact]
    public async Task ObserveWindowWithoutSettingSeenAsync_ServerVolunteersAnUnrequestedSummary_LeavesItOutOfTheAnswer()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.CondStore | ImapCapabilities.QuickResync };
        var folder = CreateSelectedFolder(highestModSeq: 91UL);
        AnswerFetchWith(folder, DescribedUids(10, 99));
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var observation = await session.ObserveWindowWithoutSettingSeenAsync(
            Window,
            reconciledThroughModSeq: 40UL,
            CancellationToken.None);

        // Assert
        Assert.Equal([10U], observation.Observations.Select(described => described.Uid.Value));
        Assert.Equal([11U, 12U], observation.UnchangedUids.Select(uid => uid.Value));
    }

    /// <summary>
    /// The keywords arrive in the same FLAGS answer the five system flags do, so reading them costs no wider request
    /// and no second round trip. What reaches the snapshot is the normalized set rather than the strings the server
    /// happened to write.
    /// </summary>
    [Fact]
    public async Task ObserveWindowWithoutSettingSeenAsync_ServerReportsKeywords_CarriesThemIntoTheSnapshot()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.None };
        var folder = CreateSelectedFolder();
        AnswerFetchWith(folder, [DescribedUid(10, "$Junk", "nonjunk", "$junk")]);
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var observation = await session.ObserveWindowWithoutSettingSeenAsync(
            Window,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(
            ["$JUNK", "NONJUNK"],
            observation.Observations.Single().Snapshot.Keywords.Values);
    }

    /// <summary>A server that reports no keyword leaves the set empty rather than absent, which is what an email carrying none holds.</summary>
    [Fact]
    public async Task ObserveWindowWithoutSettingSeenAsync_ServerReportsNoKeyword_LeavesTheSetEmpty()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.None };
        var folder = CreateSelectedFolder();
        AnswerFetchWith(folder, DescribedUids(10));
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var observation = await session.ObserveWindowWithoutSettingSeenAsync(
            Window,
            reconciledThroughModSeq: null,
            CancellationToken.None);

        // Assert
        Assert.Empty(observation.Observations.Single().Snapshot.Keywords.Values);
    }

    /// <summary>Every occurrence the folder still holds, however the server said so, in the order the window asked.</summary>
    private static IReadOnlyList<uint> SurvivingUids(RemoteFolderWindowObservation observation) =>
    [
        .. observation.Observations
            .Select(described => described.Uid.Value)
            .Concat(observation.UnchangedUids.Select(uid => uid.Value))
            .Order(),
    ];

    private static IMessageSummary[] DescribedUids(params uint[] uids) =>
        [.. uids.Select(uid => DescribedUid(uid))];

    /// <summary>Describes one UID the way a server does, with the system flags and the keywords in one answer.</summary>
    private static IMessageSummary DescribedUid(uint uid, params string[] keywords)
    {
        var summary = Substitute.For<IMessageSummary>();
        summary.UniqueId.Returns(new UniqueId(uid));
        summary.Flags.Returns(MessageFlags.Seen);
        summary.Keywords.Returns(new HashSet<string>(keywords, StringComparer.Ordinal));

        return summary;
    }

    /// <summary>Answers the fetch with the supplied summaries, and reports the vanished messages while it runs.</summary>
    /// <remarks>
    /// The vanished report is raised from inside the fetch on purpose: that is where MailKit raises it, and a test that
    /// raised it before the command would pass against an adapter that never subscribed at all.
    /// </remarks>
    private static void AnswerFetchWith(
        IMailFolder folder,
        IMessageSummary[] summaries,
        uint[]? vanishedUids = null)
    {
        folder
            .FetchAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IFetchRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (vanishedUids is { Length: > 0 })
                {
                    folder.MessagesVanished += Raise.EventWith(
                        folder,
                        new MessagesVanishedEventArgs([.. vanishedUids.Select(uid => new UniqueId(uid))], earlier: true));
                }

                return Task.FromResult<IList<IMessageSummary>>([.. summaries]);
            });
    }

    private static void AnswerSearchWith(IMailFolder folder, params uint[] survivingUids) =>
        folder
            .SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IList<UniqueId>>(
                [.. survivingUids.Select(uid => new UniqueId(uid))]));

    /// <summary>Reads the modification sequence the adapter narrowed its fetch by, out of the request it issued.</summary>
    private static ulong? RequestedChangedSince(IMailFolder folder) => folder
        .ReceivedCalls()
        .Where(call => call.GetMethodInfo().Name == nameof(IMailFolder.FetchAsync))
        .Select(call => ((IFetchRequest)call.GetArguments()[1]!).ChangedSince)
        .Single();
}
