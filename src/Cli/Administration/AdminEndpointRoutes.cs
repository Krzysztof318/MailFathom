// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration;

/// <summary>Where a deployment answers, relative to the address the operator gave.</summary>
/// <remarks>
/// The command is configured with a host and a port and appends the rest, so these two paths are the whole of what it
/// assumes about the other side. Stated together rather than beside the code that calls each, because they are one
/// agreement with the service: the deployment publishes its administrative routes beneath the first and refuses to
/// start unless its resource identifier names that same prefix, which is what puts the second exactly where RFC 9728
/// says to look for it.
/// </remarks>
internal static class AdminEndpointRoutes
{
    /// <summary>The prefix every administrative route is served beneath.</summary>
    internal const string Prefix = "/api/admin";

    /// <summary>Where a deployment reports who a presented credential makes the caller.</summary>
    internal const string SessionPath = $"{Prefix}/session";

    /// <summary>Where a deployment publishes the document naming its authorization servers, resource, and required scopes.</summary>
    /// <remarks>
    /// Composed rather than discovered from a challenge, because a client that knows which routes it is about to call
    /// already knows enough: RFC 9728 places the document under a well-known segment with the resource's path appended,
    /// and the deployment refuses to start unless its resource path is <see cref="Prefix" />. One request rather than
    /// two, and no dependence on the wording of a refusal.
    /// </remarks>
    internal const string ProtectedResourceMetadataPath = $"/.well-known/oauth-protected-resource{Prefix}";
}
