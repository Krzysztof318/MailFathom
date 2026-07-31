// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Xunit;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Groups every test that needs the MailFathom host served over HTTPS behind mutual TLS running.</summary>
/// <remarks>
/// <para>
/// A collection of its own rather than a member of <see cref="ComposedHostCollectionDefinition" />, because starting a
/// second MailFathom is what these tests cost and the tests in that collection must not pay it. xUnit orders collections
/// within an assembly but not classes within a collection, so sharing one would leave which host starts first — and
/// therefore how loaded the machine is while the other collection measures a rate limit — decided by discovery order.
/// </para>
/// <para>
/// <see cref="ComposedHostsRunLastCollectionOrderer" /> places it after that collection, which is after every other.
/// Parallelization is off for the reason it is off there: xUnit runs collections that disable it sequentially, which is
/// what makes ordering them against each other mean anything at all.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MutualTlsHostCollectionDefinition
{
    /// <summary>The collection name every test class needing the mutual-TLS host joins.</summary>
    public const string Name = "Mutual TLS MailFathom host";
}
