// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that what a period has spent against an embedding provider is added rather than overwritten.</summary>
/// <remarks>
/// <para>
/// The ledger is one raw <c>INSERT ... ON CONFLICT DO UPDATE</c> whose whole purpose is a total two workers in separate
/// transactions can both add to. Nothing below a real server can establish that it does: the statement never executes in
/// a unit test, so a conflict target naming the wrong column, an assignment that replaced the total instead of adding to
/// it, or an identifier that drifted from the entity would all pass there and would reach an operator as a spend ceiling
/// that quietly stopped counting — which is money rather than a wrong answer.
/// </para>
/// <para>
/// The period this class charges is its own, because the table is keyed by the period's start and the suite shares one
/// database.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmbeddingSpendLedgerTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The period this class charges, stated so nothing else in the suite writes the row it reads.</summary>
    private static readonly DateTimeOffset ChargedPeriodStart = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A period this class never charges, which is what makes the "reads as zero" claim decidable.</summary>
    private static readonly DateTimeOffset UnchargedPeriodStart = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    private const long FirstSpend = 1_100;

    private const long SecondSpend = 700;

    /// <summary>
    /// Two workers charging one period at the same time, each in a transaction of its own, and the period ends up
    /// holding both. A read-modify-write would leave whichever committed last, so the sum is what separates the
    /// statement that exists from the one it replaced.
    /// </summary>
    /// <remarks>
    /// The two charges are dispatched together rather than awaited one after the other, and that is a requirement rather
    /// than a way to be quick: the first statement holds the row until its transaction ends, so a test that awaited the
    /// second while the first was still open would wait for a lock nothing was going to release.
    /// </remarks>
    [Fact]
    public async Task RecordSpendAsync_TwoConcurrentSessionsChargingOnePeriod_AddsBothAndLosesNeither()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // A period nobody has charged has no row at all, and the port answers zero rather than an absence — which is
        // what makes the first call of a new period ordinary. Read before the act, so the total below is what the two
        // charges produced rather than what an earlier run left.
        Assert.Equal(0, await ConsumedAsync(services, ChargedPeriodStart, cancellationToken));

        // Act
        var commits = await services.InTwoScopesAsync(
            (firstScope, secondScope, token) => Task.WhenAll(
                ChargeAsync(firstScope, FirstSpend, token),
                ChargeAsync(secondScope, SecondSpend, token)),
            cancellationToken);

        // Assert
        Assert.Equal([PersistenceCommitResult.Committed, PersistenceCommitResult.Committed], commits);
        Assert.Equal(FirstSpend + SecondSpend, await ConsumedAsync(services, ChargedPeriodStart, cancellationToken));

        // The row the two charges wrote belongs to the period they named and to no other, which is what makes a ceiling
        // a bound on one window rather than on the deployment's whole history.
        Assert.Equal(0, await ConsumedAsync(services, UnchargedPeriodStart, cancellationToken));
    }

    /// <summary>Charges one period from one scope, in a session of its own, the way a worker that just embedded does.</summary>
    private static async Task<PersistenceCommitResult> ChargeAsync(
        IServiceProvider scope,
        long inputCharacterCount,
        CancellationToken cancellationToken)
    {
        await using var session = await scope.GetRequiredService<IPersistenceSessionFactory>()
            .BeginSessionAsync(cancellationToken);

        await scope.GetRequiredService<IEmbeddingSpendLedger>().RecordSpendAsync(
            session,
            ChargedPeriodStart,
            inputCharacterCount,
            cancellationToken);

        return await session.CommitAsync(cancellationToken);
    }

    private static Task<long> ConsumedAsync(
        OrchestratedMailFathomServices services,
        DateTimeOffset periodStart,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmbeddingSpendLedger>()
                .ReadConsumedInputCharactersAsync(periodStart, token),
            cancellationToken);
}
