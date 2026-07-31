// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Xunit;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Groups every test that needs the composed MailFathom host running.</summary>
/// <remarks>
/// <para>
/// A separate collection from <see cref="OrchestratedInfrastructureCollectionDefinition" /> rather than a member of it,
/// because the two share a resource in opposite directions. Those tests need the database and the mailbox to hold only
/// what they put there; these need a whole MailFathom connected to the same database. Naming the difference is what lets
/// <see cref="ComposedHostRunsLastCollectionOrderer" /> put this one after the other, which is the entire mechanism
/// keeping a starting host out of another test's environment.
/// </para>
/// <para>
/// Parallelization is off for the same reason it is off for the other collection: xUnit runs collections that disable
/// it sequentially, after the parallel-capable ones, which is what makes ordering these two against each other mean
/// anything at all.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ComposedHostCollectionDefinition
{
    /// <summary>The collection name every test class needing the composed host joins.</summary>
    public const string Name = "Composed MailFathom host";
}
