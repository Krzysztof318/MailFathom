// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Xunit.Sdk;
using Xunit.v3;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Runs the two host collections after every other collection in the assembly, and after each other.</summary>
/// <remarks>
/// <para>
/// The order is a correctness requirement rather than a preference. Starting MailFathom connects a synchronization-free
/// but otherwise complete application to the database the rest of the suite writes to and asserts on, and its startup
/// alone opens connections, verifies the migration history, and begins the extraction backfill. Running that beside a
/// test that is counting rows would make the host part of that test's environment, and the failure would look like a
/// flake rather than like the ordering mistake it is.
/// </para>
/// <para>
/// The two host collections are ordered against each other for a second reason: the mutual-TLS collection starts a
/// whole second MailFathom, and one of the tests in the collection before it measures how many requests a rate limiter
/// refuses in a burst. Leaving which of them runs first to discovery order would let that measurement be taken while a
/// project process is starting, which is a slower machine than the one the limit was chosen for.
/// </para>
/// <para>
/// Ordering collections is the only lever that reaches this, because xUnit orders test cases within a class and
/// collections within an assembly, but not classes within a collection. All three collections disable parallelization,
/// so xUnit already runs them one after another; this decides in which order.
/// </para>
/// <para>
/// Everything else keeps the order xUnit produced, because <see cref="Enumerable.OrderBy{TSource, TKey}(IEnumerable{TSource}, Func{TSource, TKey})" />
/// is stable. This orderer states those two constraints and imposes nothing else.
/// </para>
/// </remarks>
public sealed class ComposedHostsRunLastCollectionOrderer : ITestCollectionOrderer
{
    /// <inheritdoc />
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(
        IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : ITestCollection =>
        [.. testCollections.OrderBy(RunsAfter)];

    private static int RunsAfter<TTestCollection>(TTestCollection testCollection)
        where TTestCollection : ITestCollection => testCollection.TestCollectionDisplayName switch
        {
            ComposedHostCollectionDefinition.Name => 1,
            MutualTlsHostCollectionDefinition.Name => 2,
            _ => 0,
        };
}
