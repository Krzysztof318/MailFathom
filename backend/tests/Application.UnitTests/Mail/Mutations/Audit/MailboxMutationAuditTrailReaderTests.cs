// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Audit;

/// <summary>Covers reading one account's record of what MailFathom did to its mailbox, which is derived from that mailbox.</summary>
public sealed class MailboxMutationAuditTrailReaderTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private readonly IMailboxMutationAuditEntryStore entries = Substitute.For<IMailboxMutationAuditEntryStore>();

    [Fact]
    public async Task ReadPageAsync_ACallerGrantedTheAuditRead_IsServedThePageTheStoreHolds()
    {
        // Arrange
        var page = new MailboxMutationAuditPage([], NextCursor: null);
        this.entries.ReadPageAsync(Arg.Any<MailboxMutationAuditQuery>(), Arg.Any<CancellationToken>()).Returns(page);
        var trail = this.TrailFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminAuditRead));

        // Act
        var read = await trail.ReadPageAsync(WholeAccount(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(page, read);
    }

    /// <summary>Where a person's mail has been is derived personal data, so the state read grants nothing towards it.</summary>
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
        await this.entries.DidNotReceive().ReadPageAsync(Arg.Any<MailboxMutationAuditQuery>(), Arg.Any<CancellationToken>());
    }

    private static MailboxMutationAuditQuery WholeAccount() => MailboxMutationAuditQuery.Create(
        Account,
        default,
        completedFrom: null,
        completedBefore: null,
        pageSize: null,
        cursor: null).Query!;

    private MailboxMutationAuditTrailReader TrailFor(AccessAuthorization authorization) =>
        new(this.entries, authorization);
}
