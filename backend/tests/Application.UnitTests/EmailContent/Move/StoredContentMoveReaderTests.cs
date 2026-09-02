// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Move;

/// <summary>Covers what an operator is shown about the move, and the grant they are shown it under.</summary>
public sealed class StoredContentMoveReaderTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryStoredContentMoveRunStore runs = new();
    private readonly InMemoryStoredContentMoveStore content = new();

    /// <summary>The move and what is left are read together, because neither answers the operator's question alone.</summary>
    [Fact]
    public async Task ReadAsync_MoveUnderWay_ReportsItBesideTheBacklog()
    {
        // Arrange
        this.runs.Arrange(new StoredContentMoveRun
        {
            RequestedAt = Moment,
            State = StoredContentMoveState.Running,
            Kind = EmailContentKind.IncomingMessage,
            CopiedPayloadCount = 12,
        });

        this.content.Arrange(EmailContentKind.IncomingMessage, Guid.NewGuid(), [1, 2, 3, 4]);
        this.content.Arrange(EmailContentKind.MailDraft, Guid.NewGuid(), [5, 6]);

        // Act
        var progress = await this.ReaderOver().ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredContentMoveState.Running, progress.Run?.State);
        Assert.Equal(2, progress.Backlog.PayloadCount);
        Assert.Equal(6, progress.Backlog.ByteCount);
    }

    /// <summary>The backlog is answered before any move exists, because it is the figure a switch is weighed against.</summary>
    [Fact]
    public async Task ReadAsync_NoMoveYet_StillReportsWhatTheDatabaseHolds()
    {
        // Arrange
        this.content.Arrange(EmailContentKind.IncomingMessage, Guid.NewGuid(), [1, 2, 3, 4]);

        // Act
        var progress = await this.ReaderOver().ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(progress.Run);
        Assert.Equal(1, progress.Backlog.PayloadCount);
        Assert.Equal(4, progress.Backlog.ByteCount);
    }

    /// <summary>Reading where a deployment keeps its mail is reading what it holds, and asks for that grant.</summary>
    [Fact]
    public async Task ReadAsync_WithoutTheReadGrant_IsRefused()
    {
        // Arrange
        var reader = this.ReaderOver(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.ReadAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
    }

    private StoredContentMoveReader ReaderOver(AccessAuthorization? authorization = null) => new(
        this.runs,
        this.content,
        authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));
}
