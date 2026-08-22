// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Infrastructure.Security.Transport;

namespace MailFathom.Host;

/// <summary>What the composition settled that the request pipeline still has to be told.</summary>
/// <remarks>
/// Every value here was decided while the services were being registered, because each of them says which services
/// exist — which sockets are opened, which surface is served, what bounds a request — and that is settled before a
/// container able to resolve an options snapshot exists. Handing them on rather than reading the sections a second time
/// is what keeps the numbers a limiter is built from and the numbers a route is mapped under one reading of one
/// configuration.
/// </remarks>
/// <param name="Mcp">The protocol surface's settings, as they were read during composition.</param>
/// <param name="Admin">The administrative surface's settings, as they were read during composition.</param>
/// <param name="Client">The client surface's settings, as they were read during composition.</param>
/// <param name="Health">The probe surface's settings, as they were read during composition.</param>
/// <param name="Listeners">One entry per socket, each naming the surfaces served on it.</param>
/// <param name="McpRateLimits">What bounds a caller of the protocol surface, or <see langword="null" /> where the surface is unserved or its limiting is off.</param>
/// <param name="AdminRateLimits">What bounds a caller of the administrative surface, or <see langword="null" /> where the surface is unserved or its limiting is off.</param>
/// <param name="ClientRateLimits">What bounds a caller of the client surface, or <see langword="null" /> where the surface is unserved or its limiting is off.</param>
/// <param name="IsRateLimited">Whether any surface is bounded, which is what decides that the limiter middleware belongs in the pipeline at all.</param>
/// <param name="McpRequestTimeout">The ceiling on one protocol request, or <see langword="null" /> where the surface is unserved or its ceiling is off.</param>
/// <param name="AdminRequestTimeout">The ceiling on one administrative request, or <see langword="null" /> where the surface is unserved or its ceiling is off.</param>
/// <param name="ClientRequestTimeout">The ceiling on one client request, or <see langword="null" /> where the surface is unserved or its ceiling is off.</param>
internal sealed record ComposedHostSurfaces(
    McpEndpointOptions Mcp,
    AdminEndpointOptions Admin,
    ClientEndpointOptions Client,
    HealthEndpointOptions Health,
    ComposedListeners Listeners,
    TransportRateLimits? McpRateLimits,
    TransportRateLimits? AdminRateLimits,
    TransportRateLimits? ClientRateLimits,
    bool IsRateLimited,
    TimeSpan? McpRequestTimeout,
    TimeSpan? AdminRequestTimeout,
    TimeSpan? ClientRequestTimeout);
