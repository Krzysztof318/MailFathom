// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.ClientAssertions;
using MailFathom.Host.Security.ClientAssertions;
using MailFathom.Infrastructure.Secrets;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.ClientAssertions;

/// <summary>Covers what the store refuses a second time, and what it stops holding.</summary>
/// <remarks>
/// Two properties matter and they pull against each other. Nothing may be served twice inside its lifetime, which is the
/// replay the method exists to refuse; and nothing may be remembered indefinitely, because an authenticated client would
/// otherwise grow the store one identifier per request for as long as the process runs.
/// </remarks>
public sealed class ClientAssertionReplayStoreTests
{
    private static readonly DateTimeOffset SpentAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TrySpend_AnIdentifierNotSeenBefore_IsServed()
    {
        // Arrange
        var store = new ClientAssertionReplayStore(new FakeTimeProvider(SpentAt));

        // Act
        var served = store.TrySpend(KeyNamed("nightly"), "an-identifier", SpentAt.AddMinutes(1));

        // Assert
        Assert.True(served);
    }

    [Fact]
    public void TrySpend_TheSameIdentifierTwice_RefusesTheSecond()
    {
        // Arrange
        var store = new ClientAssertionReplayStore(new FakeTimeProvider(SpentAt));

        // Act
        var first = store.TrySpend(KeyNamed("nightly"), "an-identifier", SpentAt.AddMinutes(1));
        var second = store.TrySpend(KeyNamed("nightly"), "an-identifier", SpentAt.AddMinutes(1));

        // Assert
        Assert.True(first);
        Assert.False(second);
    }

    /// <summary>Identifiers are the client's own, so one client spending a value must never be able to refuse another that happens to choose it.</summary>
    [Fact]
    public void TrySpend_OneIdentifierUnderTwoKeys_ServesBoth()
    {
        // Arrange
        var store = new ClientAssertionReplayStore(new FakeTimeProvider(SpentAt));

        // Act
        var first = store.TrySpend(KeyNamed("nightly"), "an-identifier", SpentAt.AddMinutes(1));
        var second = store.TrySpend(KeyNamed("reporting"), "an-identifier", SpentAt.AddMinutes(1));

        // Assert
        Assert.True(first);
        Assert.True(second);
    }

    /// <summary>
    /// Nothing is remembered indefinitely, which is what bounds the store: an entry is dropped once the assertion
    /// carrying it could no longer be accepted anyway. Without the sweep an authenticated client grows the store one
    /// identifier per request for the life of the process, which is the only way this can be made to cost memory.
    /// </summary>
    [Fact]
    public void TrySpend_PastThePermittedLifetime_StopsHoldingTheExpiredEntry()
    {
        // Arrange
        var clock = new FakeTimeProvider(SpentAt);
        var store = new ClientAssertionReplayStore(clock);

        store.TrySpend(KeyNamed("nightly"), "an-identifier", SpentAt.AddMinutes(1));

        // Act
        clock.Advance(ClientAssertion.MaximumLifetime + TimeSpan.FromMinutes(1));
        store.TrySpend(KeyNamed("nightly"), "a-later-identifier", clock.GetUtcNow().AddMinutes(1));

        // Assert
        Assert.True(store.TrySpend(KeyNamed("nightly"), "an-identifier", clock.GetUtcNow().AddMinutes(1)));
    }

    /// <summary>The sweep must not drop an entry whose assertion is still usable, which would reopen the replay window it was recording.</summary>
    [Fact]
    public void TrySpend_AfterASweep_StillRefusesAnUnexpiredIdentifier()
    {
        // Arrange
        var clock = new FakeTimeProvider(SpentAt);
        var store = new ClientAssertionReplayStore(clock);

        store.TrySpend(KeyNamed("nightly"), "an-identifier", SpentAt + ClientAssertion.MaximumLifetime * 3);

        // Act
        clock.Advance(ClientAssertion.MaximumLifetime + TimeSpan.FromMinutes(1));

        // Assert
        Assert.False(store.TrySpend(KeyNamed("nightly"), "an-identifier", SpentAt + ClientAssertion.MaximumLifetime * 3));
    }

    private static SecretName KeyNamed(string name) =>
        SecretName.TryCreate(name, out var keyName) ? keyName : throw new InvalidOperationException(name);
}
