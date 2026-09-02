// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit;
using MailKit.Net.Imap;
using NSubstitute;
using Xunit;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapSessionTestContext;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapWriteSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit.Writes;

/// <summary>Covers the one write that puts a message MailFathom composed into a folder, and the one that takes it back.</summary>
public sealed class MailKitImapOutgoingCopyTests
{
    private static readonly DateTimeOffset AppendedAt = new(2026, 8, 19, 9, 30, 0, TimeSpan.Zero);

    private static readonly ReadOnlyMemory<byte> RawMime = Encoding.ASCII.GetBytes(
        "Message-ID: <mint-1@mailfathom.invalid>\r\nSubject: synthetic\r\nFrom: me@example.test\r\n\r\nbody\r\n");

    /// <summary>The flags and the internal date are the caller's, and the bytes are the ones the submission carried.</summary>
    [Fact]
    public async Task AppendAsync_ASentCopy_AppendsTheStoredMimeAsSeenAtTheGivenInstant()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        openFolder.AppendAsync(Arg.Any<IAppendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UniqueId?>(new UniqueId(7U)));
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        var copy = await session.AppendAsync(
            RawMime,
            AppendedMailFlags.Seen,
            AppendedAt,
            TestContext.Current.CancellationToken);

        // Assert
        await openFolder.Received(1).AppendAsync(
            Arg.Is<IAppendRequest>(request => request != null
                && request.Flags == MessageFlags.Seen
                && request.InternalDate == AppendedAt),
            Arg.Any<CancellationToken>());

        Assert.Equal(ImapUidValidity.Create(7U), copy.Placement.UidValidity);
        Assert.Equal(ImapUid.Create(7U), copy.Placement.Uid);
        Assert.Equal("mint-1@mailfathom.invalid", copy.InternetMessageId);
    }

    /// <summary>A mirrored message has not left, so its copy carries <c>\Draft</c> and never reads as sent.</summary>
    [Fact]
    public async Task AppendAsync_AMirroredCopy_AppendsItAsADraft()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        openFolder.AppendAsync(Arg.Any<IAppendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UniqueId?>(new UniqueId(9U)));
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.AppendAsync(
            RawMime,
            AppendedMailFlags.Draft,
            AppendedAt,
            TestContext.Current.CancellationToken);

        // Assert
        await openFolder.Received(1).AppendAsync(
            Arg.Is<IAppendRequest>(request => request != null && request.Flags == MessageFlags.Draft),
            Arg.Any<CancellationToken>());

        await openFolder.DidNotReceive().AppendAsync(
            Arg.Is<IAppendRequest>(request => request != null && request.Flags.HasFlag(MessageFlags.Seen)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A server advertising no <c>UIDPLUS</c> answers an append with nothing, and the identity in the appended bytes is
    /// what is left to recognize the copy by. Reporting a placement it never gave would invent one.
    /// </summary>
    [Fact]
    public async Task AppendAsync_AServerNamingNoPlacement_ReportsTheMessageIdentityAlone()
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
        var copy = await session.AppendAsync(
            RawMime,
            AppendedMailFlags.Seen,
            AppendedAt,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(copy.Placement.IsReported);
        Assert.Equal("mint-1@mailfathom.invalid", copy.InternetMessageId);
    }

    /// <summary>The copy is removed by identity, which is what <c>UID EXPUNGE</c> is and why a bare expunge is never issued.</summary>
    [Fact]
    public async Task WithdrawAppendedAsync_AnAppendedCopy_DeletesAndExpungesThatUidAlone()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.WithdrawAppendedAsync(
            ImapUidValidity.Create(7U),
            ImapUid.Create(9U),
            TestContext.Current.CancellationToken);

        // Assert
        await openFolder.Received(1).StoreAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 9U),
            Arg.Is<IStoreFlagsRequest>(request => request != null
                && request.Action == StoreAction.Add
                && request.Flags == MessageFlags.Deleted),
            Arg.Any<CancellationToken>());

        await openFolder.Received(1).ExpungeAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 9U),
            Arg.Any<CancellationToken>());

        await openFolder.DidNotReceive().ExpungeAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A folder recreated between the append and the withdrawal renumbered everything in it, so the recorded UID names
    /// a different message and removing it would delete somebody's mail.
    /// </summary>
    [Fact]
    public async Task WithdrawAppendedAsync_AFolderRecreatedSinceTheAppend_RemovesNothing()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder(uidValidity: 8U);
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act & Assert
        await Assert.ThrowsAsync<MailboxFolderRecreatedException>(() => session.WithdrawAppendedAsync(
            ImapUidValidity.Create(7U),
            ImapUid.Create(9U),
            TestContext.Current.CancellationToken));

        await openFolder.DidNotReceiveWithAnyArgs().ExpungeAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Without <c>UIDPLUS</c> the only expunge available is the folder's, which would remove mail nobody asked about.</summary>
    [Fact]
    public async Task WithdrawAppendedAsync_AServerWithoutUidPlus_IsRefusedRatherThanExpungingTheFolder()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.None };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act & Assert
        var refusal = await Assert.ThrowsAsync<MailboxMutationUnsupportedException>(
            () => session.WithdrawAppendedAsync(
                ImapUidValidity.Create(7U),
                ImapUid.Create(9U),
                TestContext.Current.CancellationToken));

        Assert.Equal(MailFathomErrorCode.MailboxMutationUnsupported, refusal.ErrorCode);

        // Withdrawing a copy this deployment filed is not one of the mutations, so the refusal names the withdrawal
        // rather than a delete an operator reading it would go looking for a rule or a request behind.
        Assert.Equal("withdraw-outgoing-copy", refusal.Operation);
        Assert.DoesNotContain(MailboxMutation.Delete.Name, refusal.Message, StringComparison.Ordinal);
        await openFolder.DidNotReceiveWithAnyArgs().ExpungeAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().ExpungeAsync(Arg.Any<CancellationToken>());
    }
}
