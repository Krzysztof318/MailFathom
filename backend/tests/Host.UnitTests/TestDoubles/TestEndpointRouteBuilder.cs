// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>The smallest thing the routing builder API accepts, so a mapping can be exercised without a web host.</summary>
/// <remarks>
/// What a mapping decides — which routes it produces, which verbs each answers, and what metadata each carries — is
/// readable from the endpoints it builds, and building them needs a route builder and nothing else. Starting a server
/// to read that back would prove the framework's routing rather than this repository's mapping.
/// </remarks>
internal sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
{
    public IServiceProvider ServiceProvider { get; } = serviceProvider;

    public ICollection<EndpointDataSource> DataSources { get; } = [];

    public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(this.ServiceProvider);

    /// <summary>Reads back every endpoint the mappings under test produced.</summary>
    /// <returns>The endpoints, in the order their data sources report them.</returns>
    internal IReadOnlyList<Endpoint> Materialize() => [.. this.DataSources.SelectMany(source => source.Endpoints)];
}
