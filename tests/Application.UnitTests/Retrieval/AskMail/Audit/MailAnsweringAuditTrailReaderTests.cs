// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval.AskMail.Audit;

/// <summary>Covers reading one account's record of the questions answered from its mailbox.</summary>
public sealed class MailAnsweringAuditTrailReaderTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private readonly IMailAnsweringAuditEntryStore entries = Substitute.For<IMailAnsweringAuditEntryStore>();

    [Fact]
    public async Task ReadPageAsync_ACallerGrantedTheAuditRead_IsServedThePageTheStoreHolds()
    {
        // Arrange
        var page = new MailAnsweringAuditPage([], NextCursor: null);
        this.entries.ReadPageAsync(Arg.Any<MailAnsweringAuditQuery>(), Arg.Any<CancellationToken>()).Returns(page);
        var trail = this.TrailFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminAuditRead));

        // Act
        var read = await trail.ReadPageAsync(WholeAccount(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(page, read);
    }

    /// <summary>Which of a person's messages a question reached is derived personal data, so the state read grants nothing towards it.</summary>
    [Fact]
    public async Task ReadPageAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var trail = this.TrailFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            trail.ReadPageAsync(WholeAccount(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminAuditRead, refusal.RequiredPermission);
        await this.entries.DidNotReceive().ReadPageAsync(Arg.Any<MailAnsweringAuditQuery>(), Arg.Any<CancellationToken>());
    }

    private static MailAnsweringAuditQuery WholeAccount() => MailAnsweringAuditQuery.Create(
        Account,
        completedFrom: null,
        completedBefore: null,
        pageSize: null,
        cursor: null).Query!;

    private MailAnsweringAuditTrailReader TrailFor(AccessAuthorization authorization) =>
        new(this.entries, authorization);
}
