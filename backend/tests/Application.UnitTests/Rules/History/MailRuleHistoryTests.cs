// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Rules.History;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.History;

/// <summary>Covers reading what the rules concluded about an account's mail, which is derived from that mail.</summary>
public sealed class MailRuleHistoryTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private readonly IMailRuleExecutionStore executions = Substitute.For<IMailRuleExecutionStore>();

    [Fact]
    public async Task ReadPageAsync_ACallerGrantedTheAuditRead_IsServedThePageTheStoreHolds()
    {
        // Arrange
        var page = new MailRuleExecutionPage([], NextCursor: null);
        this.executions.ReadPageAsync(Arg.Any<MailRuleExecutionQuery>(), Arg.Any<CancellationToken>()).Returns(page);
        var history = this.HistoryFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminAuditRead));

        // Act
        var read = await history.ReadPageAsync(WholeAccount(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(page, read);
    }

    /// <summary>What the rules concluded about somebody's messages is derived personal data, so the state read grants nothing towards it.</summary>
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
        await this.executions.DidNotReceive().ReadPageAsync(Arg.Any<MailRuleExecutionQuery>(), Arg.Any<CancellationToken>());
    }

    private static MailRuleExecutionQuery WholeAccount() => MailRuleExecutionQuery.Create(
        Account,
        ruleName: null,
        storedEmailId: null,
        evaluatedFrom: null,
        evaluatedBefore: null,
        pageSize: null,
        cursor: null).Query!;

    private MailRuleHistory HistoryFor(AccessAuthorization authorization) => new(this.executions, authorization);
}
