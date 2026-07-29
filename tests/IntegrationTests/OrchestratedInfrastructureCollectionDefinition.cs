// Copyright © 2026 Krzysztof Kasprowicz

using Xunit;

namespace MailMcp.IntegrationTests;

/// <summary>Groups every test that runs against the one orchestrated database and the one orchestrated mailbox.</summary>
/// <remarks>
/// <para>
/// Both are single shared resources, and neither can be isolated per test the way a unit test isolates itself: the
/// mailbox has one INBOX whose UIDs advance globally, and a folder recreated by one test changes what another would
/// select. Running two such tests at once would make each one's arrangement part of the other's environment, so
/// parallelization is switched off for the collection rather than worked around with retries or with mailbox names
/// nobody can keep unique.
/// </para>
/// <para>
/// Declared as a collection rather than as an assembly-wide setting, so it stays a statement about what these tests
/// share. A future test that needs neither the mailbox nor the database is free to stay outside it and run alongside.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OrchestratedInfrastructureCollectionDefinition
{
    /// <summary>The collection name every test class sharing the orchestrated infrastructure joins.</summary>
    public const string Name = "Orchestrated infrastructure";
}
