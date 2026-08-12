// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Cli.Administration;

/// <summary>Where a deployment answers, relative to the address the operator gave.</summary>
/// <remarks>
/// The command is configured with a host and a port and appends the rest, so these paths are the whole of what it
/// assumes about the other side. Stated together rather than beside the code that calls each, because they are one
/// agreement with the service: the deployment publishes its administrative routes beneath the prefix and refuses to
/// start unless its resource identifier names that same prefix, which is what puts the metadata document exactly where
/// RFC 9728 says to look for it.
/// </remarks>
internal static class AdminEndpointRoutes
{
    /// <summary>The prefix every administrative route is served beneath.</summary>
    internal const string Prefix = "/api/admin";

    /// <summary>Where a deployment reports who a presented credential makes the caller.</summary>
    internal const string SessionPath = $"{Prefix}/session";

    /// <summary>Where a deployment accepts the refresh token it should keep for one of its mail accounts.</summary>
    internal const string MailboxRefreshTokenPath = $"{Prefix}/mailbox/refresh-token";

    /// <summary>Where a deployment reports whether semantic search is working and how far behind it is.</summary>
    internal const string EmbeddingStatusPath = $"{Prefix}/embeddings";

    /// <summary>Where a deployment reports what activating its declaration would cost, and where that activation is performed.</summary>
    /// <remarks>
    /// One path read with <c>GET</c> and performed with <c>POST</c>, which is what keeps the figure an operator confirms
    /// and the figure the deployment weighs the same figure rather than two counts that happen to agree.
    /// </remarks>
    internal const string EmbeddingActivationPath = $"{Prefix}/embeddings/activation";

    /// <summary>Where a deployment is asked to stop the reindex it has under way.</summary>
    internal const string EmbeddingReindexCancellationPath = $"{Prefix}/embeddings/reindex/cancellation";

    /// <summary>Where a deployment reports the mail rules it has loaded, and whether its rule file was accepted.</summary>
    internal const string RulesPath = $"{Prefix}/rules";

    /// <summary>Where a whole-mailbox rule run is asked for, and where the one an account has is read.</summary>
    /// <remarks>
    /// One path read with <c>GET</c> and asked for with <c>POST</c>, which is what keeps the run an operator started and
    /// the run they come back to watch the same run rather than two answers that happen to agree.
    /// </remarks>
    internal const string RuleRunsPath = $"{Prefix}/rules/runs";

    /// <summary>Where a deployment reports what its rules concluded about the mail they were run over.</summary>
    internal const string RuleHistoryPath = $"{Prefix}/rules/history";

    /// <summary>Where a deployment publishes the document naming its authorization servers, resource, and required scopes.</summary>
    /// <remarks>
    /// Composed rather than discovered from a challenge, because a client that knows which routes it is about to call
    /// already knows enough: RFC 9728 places the document under a well-known segment with the resource's path appended,
    /// and the deployment refuses to start unless its resource path is <see cref="Prefix" />. One request rather than
    /// two, and no dependence on the wording of a refusal.
    /// </remarks>
    internal const string ProtectedResourceMetadataPath = $"/.well-known/oauth-protected-resource{Prefix}";
}
