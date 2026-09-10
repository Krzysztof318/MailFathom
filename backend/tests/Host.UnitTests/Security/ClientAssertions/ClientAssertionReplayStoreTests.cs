// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Common.ClientAssertions;
using MailFathom.Host.Security.ClientAssertions;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Secrets;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.ClientAssertions;

/// <summary>Covers what the store refuses a second time, and what it stops holding.</summary>
/// <remarks>
/// <para>
/// Two properties matter and they pull against each other. Nothing may be served twice inside its lifetime, which is the
/// replay the method exists to refuse; and nothing may be remembered indefinitely, because an authenticated client would
/// otherwise grow the record one identifier per request for as long as the deployment runs.
/// </para>
/// <para>
/// What the record itself is, and whether PostgreSQL settles two replicas presenting one identifier at the same instant,
/// is <c>OrchestratedClientAssertionReplayTests</c>'s to prove against a real server. What is decidable here is
/// everything above the port: the scoping to the verifying credential, and the sweep interval that decides when expired
/// records are asked for.
/// </para>
/// </remarks>
public sealed class ClientAssertionReplayStoreTests
{
    private static readonly DateTimeOffset SpentAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TrySpendAsync_AnIdentifierNotSeenBefore_IsServed()
    {
        // Arrange
        var store = new ClientAssertionReplayStore(
            new InMemoryClientAssertionSpendStore(),
            new FakeTimeProvider(SpentAt));

        // Act
        var served = await store.TrySpendAsync(
            KeyNamed("nightly"),
            "an-identifier",
            SpentAt.AddMinutes(1),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(served);
    }

    [Fact]
    public async Task TrySpendAsync_TheSameIdentifierTwice_RefusesTheSecond()
    {
        // Arrange
        var store = new ClientAssertionReplayStore(
            new InMemoryClientAssertionSpendStore(),
            new FakeTimeProvider(SpentAt));

        // Act
        var first = await SpendAsync(store, "nightly", "an-identifier", SpentAt.AddMinutes(1));
        var second = await SpendAsync(store, "nightly", "an-identifier", SpentAt.AddMinutes(1));

        // Assert
        Assert.True(first);
        Assert.False(second);
    }

    /// <summary>An identifier already served by another replica is refused here, which is the whole reason the record left this process.</summary>
    /// <remarks>The two stores share one spend store and hold their own sweep intervals, which is what a second replica against one database is.</remarks>
    [Fact]
    public async Task TrySpendAsync_AnIdentifierAnotherReplicaAlreadyServed_IsRefused()
    {
        // Arrange
        var deployment = new InMemoryClientAssertionSpendStore();
        var oneReplica = new ClientAssertionReplayStore(deployment, new FakeTimeProvider(SpentAt));
        var anotherReplica = new ClientAssertionReplayStore(deployment, new FakeTimeProvider(SpentAt));

        // Act
        var served = await SpendAsync(oneReplica, "nightly", "an-identifier", SpentAt.AddMinutes(1));
        var replayed = await SpendAsync(anotherReplica, "nightly", "an-identifier", SpentAt.AddMinutes(1));

        // Assert
        Assert.True(served);
        Assert.False(replayed);
    }

    /// <summary>Identifiers are the client's own, so one client spending a value must never be able to refuse another that happens to choose it.</summary>
    [Fact]
    public async Task TrySpendAsync_OneIdentifierUnderTwoKeys_ServesBoth()
    {
        // Arrange
        var store = new ClientAssertionReplayStore(
            new InMemoryClientAssertionSpendStore(),
            new FakeTimeProvider(SpentAt));

        // Act
        var first = await SpendAsync(store, "nightly", "an-identifier", SpentAt.AddMinutes(1));
        var second = await SpendAsync(store, "reporting", "an-identifier", SpentAt.AddMinutes(1));

        // Assert
        Assert.True(first);
        Assert.True(second);
    }

    /// <summary>
    /// Nothing is remembered indefinitely, which is what bounds the table: a record is dropped once the assertion
    /// carrying it could no longer be accepted anyway. Without the sweep an authenticated client grows it one identifier
    /// per request for the life of the deployment, which is the only way this can be made to cost storage.
    /// </summary>
    [Fact]
    public async Task TrySpendAsync_PastThePermittedLifetime_StopsHoldingTheExpiredRecord()
    {
        // Arrange
        var clock = new FakeTimeProvider(SpentAt);
        var store = new ClientAssertionReplayStore(new InMemoryClientAssertionSpendStore(), clock);

        await SpendAsync(store, "nightly", "an-identifier", SpentAt.AddMinutes(1));

        // Act
        clock.Advance(ClientAssertion.MaximumLifetime + TimeSpan.FromMinutes(1));
        await SpendAsync(store, "nightly", "a-later-identifier", clock.GetUtcNow().AddMinutes(1));

        // Assert
        Assert.True(await SpendAsync(store, "nightly", "an-identifier", clock.GetUtcNow().AddMinutes(1)));
    }

    /// <summary>The sweep must not drop a record whose assertion is still usable, which would reopen the replay window it was recording.</summary>
    [Fact]
    public async Task TrySpendAsync_AfterASweep_StillRefusesAnUnexpiredIdentifier()
    {
        // Arrange
        var clock = new FakeTimeProvider(SpentAt);
        var store = new ClientAssertionReplayStore(new InMemoryClientAssertionSpendStore(), clock);

        await SpendAsync(store, "nightly", "an-identifier", SpentAt + (ClientAssertion.MaximumLifetime * 3));

        // Act
        clock.Advance(ClientAssertion.MaximumLifetime + TimeSpan.FromMinutes(1));

        // Assert
        Assert.False(await SpendAsync(
            store,
            "nightly",
            "an-identifier",
            SpentAt + (ClientAssertion.MaximumLifetime * 3)));
    }

    /// <summary>
    /// An assertion is accepted for the permitted skew past its own expiry, so a record dropped at that expiry is
    /// dropped while the assertion carrying it is still being served. This is the boundary the test above never
    /// reaches: the sweep lands inside that tail, and a record removed there admits the replay on the next
    /// presentation of the same captured assertion.
    /// </summary>
    [Fact]
    public async Task TrySpendAsync_ASweepInsideThePermittedSkew_StillRefusesAnIdentifierValidationWouldAccept()
    {
        // Arrange
        var clock = new FakeTimeProvider(SpentAt);
        var store = new ClientAssertionReplayStore(new InMemoryClientAssertionSpendStore(), clock);
        var expiresAt = SpentAt + ClientAssertion.MaximumLifetime;

        await SpendAsync(store, "nightly", "an-identifier", expiresAt);

        // Act
        clock.Advance(ClientAssertion.MaximumLifetime + (ClientAssertionValidation.PermittedClockSkew / 2));

        // Assert
        Assert.True(clock.GetUtcNow() < expiresAt + ClientAssertionValidation.PermittedClockSkew);
        Assert.False(await SpendAsync(store, "nightly", "an-identifier", expiresAt));
    }

    /// <summary>
    /// The removal is asked for at most once per permitted lifetime rather than per request, which is what keeps the
    /// second statement off the authentication path: a deployment answering a request a second would otherwise issue a
    /// delete a second to drop records no client could have presented anyway.
    /// </summary>
    [Fact]
    public async Task TrySpendAsync_ManySpendsInsideOneLifetime_AsksForOneRemoval()
    {
        // Arrange
        var clock = new FakeTimeProvider(SpentAt);
        var deployment = new InMemoryClientAssertionSpendStore();
        var store = new ClientAssertionReplayStore(deployment, clock);

        // Act
        clock.Advance(ClientAssertion.MaximumLifetime);

        foreach (var ordinal in Enumerable.Range(0, 5))
        {
            await SpendAsync(store, "nightly", $"an-identifier-{ordinal}", clock.GetUtcNow().AddMinutes(1));
        }

        // Assert
        Assert.Equal(1, deployment.RemovalCount);
    }

    /// <summary>
    /// A store that cannot say whether an identifier has been served is a store whose caller must not serve. Answering
    /// <see langword="true" /> on a database failure would reopen the replay window this exists to close, and answering
    /// <see langword="false" /> would report a replay that did not happen — so the failure leaves this method rather
    /// than being turned into either answer.
    /// </summary>
    [Fact]
    public async Task TrySpendAsync_ARecordThatCannotBeWritten_RefusesToAnswerRatherThanServing()
    {
        // Arrange
        var store = new ClientAssertionReplayStore(
            new UnreachableClientAssertionSpendStore(),
            new FakeTimeProvider(SpentAt));

        // Act
        var refusal = await Assert.ThrowsAsync<ClientAssertionSpendUnrecordableException>(
            () => SpendAsync(store, "nightly", "an-identifier", SpentAt.AddMinutes(1)));

        // Assert
        Assert.NotNull(refusal.InnerException);
    }

    /// <summary>The removal reaches the same database over the same pool, so a sweep that cannot run takes the request that triggered it rather than being swallowed into a spend that then answers.</summary>
    [Fact]
    public async Task TrySpendAsync_ASweepThatCannotRun_RefusesToAnswerRatherThanServing()
    {
        // Arrange
        var clock = new FakeTimeProvider(SpentAt);
        var store = new ClientAssertionReplayStore(new UnreachableClientAssertionSpendStore(), clock);

        // Act
        clock.Advance(ClientAssertion.MaximumLifetime);

        // Assert
        await Assert.ThrowsAsync<ClientAssertionSpendUnrecordableException>(
            () => SpendAsync(store, "nightly", "an-identifier", clock.GetUtcNow().AddMinutes(1)));
    }

    private static Task<bool> SpendAsync(
        ClientAssertionReplayStore store,
        string keyName,
        string identifier,
        DateTimeOffset expiresAt) =>
        store.TrySpendAsync(KeyNamed(keyName), identifier, expiresAt, TestContext.Current.CancellationToken);

    private static SecretName KeyNamed(string name) =>
        SecretName.TryCreate(name, out var keyName) ? keyName : throw new InvalidOperationException(name);
}
