// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit;
using MailKit.Net.Imap;
using NSubstitute;
using Xunit;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapSessionTestContext;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapWriteSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit.Writes;

/// <summary>Covers the write that puts a held account's own mail back onto the source it was drained from.</summary>
/// <remarks>
/// What the message is appended *with* is the whole of this write's contract, and each of the three parts is lost
/// silently if it goes missing: a flag set the source no longer shows leaves the person's mailbox reading differently
/// from the one they had, an arrival stamped with the moment of the restore reorders every folder it touches by date,
/// and a keyword the folder will not keep would refuse the append altogether if it were not dropped first.
/// </remarks>
public sealed class MailKitImapRestoredAppendTests
{
    private static readonly DateTimeOffset ArrivedAt = new(2026, 3, 4, 8, 15, 0, TimeSpan.Zero);

    private static readonly ReadOnlyMemory<byte> RawMime = Encoding.ASCII.GetBytes(
        "Message-ID: <held-1@mailfathom.invalid>\r\nSubject: held\r\nFrom: sender@example.test\r\n\r\nbody\r\n");

    /// <summary>The arrival is the row's rather than the run's, and the flags are what the source last showed.</summary>
    [Fact]
    public async Task AppendRestoredAsync_AHeldMessage_AppendsItWithTheFlagsKeywordsAndArrivalItWasHeldWith()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder(keptKeywords: "$LABEL1");
        IAppendRequest? sent = null;
        openFolder.AppendAsync(Arg.Any<IAppendRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sent = call.ArgAt<IAppendRequest>(0);

                return Task.FromResult<UniqueId?>(new UniqueId(11U));
            });
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        var placement = await session.AppendRestoredAsync(
            RawMime,
            new RestoredEmailState(
                IsSeen: true,
                IsAnswered: true,
                IsFlagged: true,
                IsDraft: false,
                RemoteEmailKeywords.Create(["$Label1"])),
            ArrivedAt,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(sent);
        Assert.Equal(ArrivedAt, sent.InternalDate);
        Assert.Equal(MessageFlags.Seen | MessageFlags.Answered | MessageFlags.Flagged, sent.Flags);
        Assert.Equal(["$LABEL1"], sent.Keywords);

        Assert.Equal(ImapUidValidity.Create(7U), placement.UidValidity);
        Assert.Equal(ImapUid.Create(11U), placement.Uid);
    }

    /// <summary>A message nobody had read comes back unread, which is the flag set most easily lost by defaulting.</summary>
    [Fact]
    public async Task AppendRestoredAsync_AMessageTheSourceShowedNoFlagsFor_AppendsItCarryingNone()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        openFolder.AppendAsync(Arg.Any<IAppendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UniqueId?>(new UniqueId(12U)));
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.AppendRestoredAsync(
            RawMime,
            new RestoredEmailState(
                IsSeen: false,
                IsAnswered: false,
                IsFlagged: false,
                IsDraft: true,
                RemoteEmailKeywords.None),
            ArrivedAt,
            TestContext.Current.CancellationToken);

        // Assert
        await openFolder.Received(1).AppendAsync(
            Arg.Is<IAppendRequest>(request => request != null
                && request.Flags == MessageFlags.Draft
                && request.Keywords!.Count == 0),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The message going back matters more than the labels on it, and MailFathom keeps the labels either way — so a
    /// folder that will not hold a keyword between sessions takes the append without it rather than refusing it.
    /// </summary>
    [Fact]
    public async Task AppendRestoredAsync_AFolderThatKeepsNoKeywords_AppendsTheMessageWithoutThem()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder(keepsAnyKeyword: false);
        openFolder.AppendAsync(Arg.Any<IAppendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UniqueId?>(new UniqueId(13U)));
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.AppendRestoredAsync(
            RawMime,
            new RestoredEmailState(
                IsSeen: true,
                IsAnswered: false,
                IsFlagged: false,
                IsDraft: false,
                RemoteEmailKeywords.Create(["$Label1"])),
            ArrivedAt,
            TestContext.Current.CancellationToken);

        // Assert
        await openFolder.Received(1).AppendAsync(
            Arg.Is<IAppendRequest>(request => request != null
                && request.Flags == MessageFlags.Seen
                && request.Keywords!.Count == 0),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A server naming no placement leaves nothing to write an occurrence from, so the record stands with its outcome
    /// unknown and an operator settles it. Inventing a placement would write an occurrence naming somebody else's mail.
    /// </summary>
    [Fact]
    public async Task AppendRestoredAsync_AServerNamingNoPlacement_ReportsNone()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.None };
        var openFolder = CreateWritableFolder();
        openFolder.AppendAsync(Arg.Any<IAppendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UniqueId?>(null));
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        var placement = await session.AppendRestoredAsync(
            RawMime,
            new RestoredEmailState(
                IsSeen: false,
                IsAnswered: false,
                IsFlagged: false,
                IsDraft: false,
                RemoteEmailKeywords.None),
            ArrivedAt,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(placement.IsReported);
    }
}
