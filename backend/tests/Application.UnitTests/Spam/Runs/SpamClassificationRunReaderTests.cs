// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Spam.Runs;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Runs;

/// <summary>Covers watching a whole-mailbox classification run, which is a grant of its own and not the one that starts one.</summary>
public sealed class SpamClassificationRunReaderTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("acct-1");

    private readonly ISpamClassificationRunStore runs = Substitute.For<ISpamClassificationRunStore>();

    [Fact]
    public async Task FindLatestAsync_AnAccountNeverAskedForARun_IsServedNone()
    {
        // Arrange
        this.runs.FindLatestAsync(Account, Arg.Any<CancellationToken>()).Returns((SpamClassificationRun?)null);
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

    private SpamClassificationRunReader ReaderFor(AccessAuthorization authorization) => new(this.runs, authorization);
}
