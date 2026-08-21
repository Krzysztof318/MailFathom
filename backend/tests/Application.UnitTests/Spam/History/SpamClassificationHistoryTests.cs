// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Spam.History;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.History;

/// <summary>Covers reading what classification concluded about an account's mail, which is derived from that mail.</summary>
public sealed class SpamClassificationHistoryTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("acct-1");

    private readonly ISpamClassificationHistoryReader classifications =
        Substitute.For<ISpamClassificationHistoryReader>();

    [Fact]
    public async Task ReadPageAsync_ACallerGrantedTheAuditRead_IsServedThePageTheReaderHolds()
    {
        // Arrange
        var page = new SpamClassificationHistoryPage([], NextCursor: null);
        this.classifications
            .ReadPageAsync(Arg.Any<SpamClassificationHistoryQuery>(), Arg.Any<CancellationToken>())
            .Returns(page);
        var history = this.HistoryFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminAuditRead));

        // Act
        var read = await history.ReadPageAsync(WholeAccount(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(page, read);
    }

    /// <summary>A verdict about a message is derived from that message, so the state read grants nothing towards it.</summary>
    [Fact]
    public async Task ReadPageAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var history = this.HistoryFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            history.ReadPageAsync(WholeAccount(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminAuditRead, refusal.RequiredPermission);
        await this.classifications.DidNotReceive()
            .ReadPageAsync(Arg.Any<SpamClassificationHistoryQuery>(), Arg.Any<CancellationToken>());
    }

    private static SpamClassificationHistoryQuery WholeAccount() => SpamClassificationHistoryQuery.Create(
        Account,
        storedEmailId: null,
        verdict: null,
        evaluatedFrom: null,
        evaluatedBefore: null,
        pageSize: null,
        cursor: null).Query!;

    private SpamClassificationHistory HistoryFor(AccessAuthorization authorization) =>
        new(this.classifications, authorization);
}
