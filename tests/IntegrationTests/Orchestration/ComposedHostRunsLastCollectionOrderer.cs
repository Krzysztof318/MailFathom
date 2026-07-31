// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Xunit.Sdk;
using Xunit.v3;

namespace MailMcp.IntegrationTests.Orchestration;

/// <summary>Runs the composed-host collection after every other collection in the assembly.</summary>
/// <remarks>
/// <para>
/// The order is a correctness requirement rather than a preference. Starting MailMcp connects a synchronization-free
/// but otherwise complete application to the database the rest of the suite writes to and asserts on, and its startup
/// alone opens connections, verifies the migration history, and begins the extraction backfill. Running that beside a
/// test that is counting rows would make the host part of that test's environment, and the failure would look like a
/// flake rather than like the ordering mistake it is.
/// </para>
/// <para>
/// Ordering collections is the only lever that reaches this, because xUnit orders test cases within a class and
/// collections within an assembly, but not classes within a collection. Both collections disable parallelization, so
/// xUnit already runs them one after another; this decides which one is second.
/// </para>
/// <para>
/// Everything else keeps the order xUnit produced, because <see cref="Enumerable.OrderBy{TSource, TKey}(IEnumerable{TSource}, Func{TSource, TKey})" />
/// is stable. This orderer states one constraint and imposes nothing else.
/// </para>
/// </remarks>
public sealed class ComposedHostRunsLastCollectionOrderer : ITestCollectionOrderer
{
    /// <inheritdoc />
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(
        IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : ITestCollection =>
        [.. testCollections.OrderBy(RunsLast)];

    private static int RunsLast<TTestCollection>(TTestCollection testCollection)
        where TTestCollection : ITestCollection => string.Equals(
            testCollection.TestCollectionDisplayName,
            ComposedHostCollectionDefinition.Name,
            StringComparison.Ordinal)
        ? 1
        : 0;
}
