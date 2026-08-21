// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.Evaluation;

/// <summary>Covers watching a whole-mailbox rule run, which is a grant of its own and not the one that starts one.</summary>
public sealed class MailRuleEvaluationRunReaderTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private readonly IMailRuleEvaluationRunStore runs = Substitute.For<IMailRuleEvaluationRunStore>();

    [Fact]
    public async Task FindLatestAsync_AnAccountNeverAskedForARun_IsServedNone()
    {
        // Arrange
        this.runs.FindLatestAsync(Account, Arg.Any<CancellationToken>()).Returns((MailRuleEvaluationRun?)null);
        var reader = this.ReaderFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var run = await reader.FindLatestAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(run);
    }

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint that passed no filter meets the same refusal.</summary>
    [Fact]
    public async Task FindLatestAsync_ACallerGrantedOnlyTheAdministrativeOperate_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var reader = this.ReaderFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.FindLatestAsync(Account, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
        await this.runs.DidNotReceive().FindLatestAsync(Arg.Any<MailAccountId>(), Arg.Any<CancellationToken>());
    }

    private MailRuleEvaluationRunReader ReaderFor(AccessAuthorization authorization) => new(this.runs, authorization);
}
