// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that an assertion identifier one host served is refused by a second host against the same database.</summary>
/// <remarks>
/// <para>
/// This is the property the store exists for and the one nothing below a real server can establish. The spend is one
/// composed <c>INSERT ... ON CONFLICT DO NOTHING</c> whose reported row count is the answer, so a conflict target naming
/// the wrong column, a statement that reported an affected row where none was written, or an identifier that drifted
/// from the entity would all pass in a unit test and would reach an operator as authentication that silently stopped
/// refusing replays.
/// </para>
/// <para>
/// Two hosts rather than two calls on one, because a per-process store passes the second-call form and is exactly what
/// this change replaced. Each host composes its own service graph over the one orchestrated database, which is what a
/// second replica is.
/// </para>
/// <para>
/// The credential keys are this class's own, because the table is keyed by the credential and the suite shares one
/// database.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedClientAssertionReplayTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The credential this class spends against, stated so nothing else in the suite writes the rows it reads.</summary>
    private const string CredentialKey = "orchestrated-replay-tests";

    /// <summary>A second credential on the same deployment, which is what makes the scoping to a credential decidable.</summary>
    private const string OtherCredentialKey = "orchestrated-replay-tests-other";

    /// <summary>The credential the expiry case spends against, kept apart so a failure there names its own rows.</summary>
    private const string ExpiringCredentialKey = "orchestrated-replay-tests-expiring";

    /// <summary>The expiry every case but the removal spends under, far enough ahead that the removal cannot reach it.</summary>
    /// <remarks>The removal is deployment-wide by construction — it drops whatever has expired, whoever spent it — so the two groups of instants are ordered rather than merely distinct, and this class is the only writer of the table.</remarks>
    private static readonly DateTimeOffset OutlivesTheSuite = new(2226, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The instant the removal case judges expiry against, chosen a century before the value above.</summary>
    private static readonly DateTimeOffset RemovedAt = new(2126, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// One identifier served by one host is refused by another, which is the whole of what raising the replica count
    /// had silently cost while the record was a field in a process.
    /// </summary>
    [Fact]
    public async Task TrySpendAsync_AnIdentifierAnotherHostAlreadyServed_IsRefused()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var oneHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var anotherHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var identifier = FreshIdentifier();

        // Act
        var served = await SpendAsync(oneHost, CredentialKey, identifier, cancellationToken);
        var replayed = await SpendAsync(anotherHost, CredentialKey, identifier, cancellationToken);

        // Assert
        Assert.True(served);
        Assert.False(replayed);
    }

    /// <summary>
    /// Two hosts presenting one identifier at the same instant leave one served and one refused, settled by the unique
    /// key rather than by a check either of them makes between two statements.
    /// </summary>
    /// <remarks>
    /// The two spends are dispatched together rather than awaited one after the other, because a sequential pair
    /// establishes only that the second caller can see what the first committed — which is the half that was never in
    /// doubt. What decides the concurrent case is the index, and nothing a substitute could stand in for.
    /// </remarks>
    [Fact]
    public async Task TrySpendAsync_TwoHostsPresentingOneIdentifierAtOnce_ServesExactlyOne()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var oneHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var anotherHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var identifier = FreshIdentifier();

        // Act
        var outcomes = await Task.WhenAll(
            SpendAsync(oneHost, CredentialKey, identifier, cancellationToken),
            SpendAsync(anotherHost, CredentialKey, identifier, cancellationToken));

        // Assert
        Assert.Single(outcomes, served => served);
    }

    /// <summary>
    /// Identifiers are the client's own, so one credential spending a value must never refuse another that happens to
    /// choose it. The key is the pair, and only a real insert against the real index establishes that.
    /// </summary>
    [Fact]
    public async Task TrySpendAsync_OneIdentifierUnderTwoCredentials_ServesBoth()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var identifier = FreshIdentifier();

        // Act
        var first = await SpendAsync(host, CredentialKey, identifier, cancellationToken);
        var second = await SpendAsync(host, OtherCredentialKey, identifier, cancellationToken);

        // Assert
        Assert.True(first);
        Assert.True(second);
    }

    /// <summary>
    /// The removal drops what has expired and nothing else, which is what bounds the table without ever reopening a
    /// window: the identifier whose assertion is still usable is refused after the removal, and the expired one is not.
    /// </summary>
    [Fact]
    public async Task RemoveExpiredAsync_RecordsPastTheirAssertionsExpiry_DropsOnlyThose()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var expired = FreshIdentifier();
        var stillUsable = FreshIdentifier();

        Assert.True(await SpendAsync(
            host,
            ExpiringCredentialKey,
            expired,
            cancellationToken,
            RemovedAt.AddMinutes(-1)));
        Assert.True(await SpendAsync(
            host,
            ExpiringCredentialKey,
            stillUsable,
            cancellationToken,
            RemovedAt.AddMinutes(1)));

        // Act
        await host.InScopeAsync(
            async (scope, token) =>
            {
                await SpendStoreOf(scope).RemoveExpiredAsync(RemovedAt, token);

                return true;
            },
            cancellationToken);

        // Assert
        Assert.True(await SpendAsync(
            host,
            ExpiringCredentialKey,
            expired,
            cancellationToken,
            RemovedAt.AddMinutes(-1)));
        Assert.False(await SpendAsync(
            host,
            ExpiringCredentialKey,
            stillUsable,
            cancellationToken,
            RemovedAt.AddMinutes(1)));
    }

    /// <summary>Spends one identifier through the host's own registered store, the way an authentication handler does.</summary>
    private static Task<bool> SpendAsync(
        OrchestratedMailFathomServices host,
        string credentialKey,
        string identifier,
        CancellationToken cancellationToken,
        DateTimeOffset? expiresAt = null) =>
        host.InScopeAsync(
            (scope, token) => SpendStoreOf(scope).TrySpendAsync(
                credentialKey,
                identifier,
                expiresAt ?? OutlivesTheSuite,
                token),
            cancellationToken);

    private static IClientAssertionSpendStore SpendStoreOf(IServiceProvider scope) =>
        scope.GetRequiredService<IClientAssertionSpendStore>();

    /// <summary>Mints an identifier no other test and no earlier run has spent, because the table is never truncated between them.</summary>
    private static string FreshIdentifier() => Guid.NewGuid().ToString("N");
}
