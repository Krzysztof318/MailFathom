// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Secrets;

namespace MailMcp.Infrastructure.Security;

/// <summary>Whose capacity an MCP request spends.</summary>
/// <remarks>
/// <para>
/// A per-client limit needs a name to keep the count under, and which name it is decides whether the limit protects the
/// endpoint or becomes the way to bring it down. Every partition a request can name is a dictionary entry the process
/// keeps for as long as the limiter does, so a key an attacker chooses is memory an attacker allocates. This type
/// therefore admits exactly one source of names: the identity authentication established. Nothing a caller writes into
/// a header, a path, a query string, an <c>Origin</c>, or a user agent reaches a key, and no forwarded address does
/// either, because a proxy header is chosen by whoever is upstream and trusting one is a separate design with its own
/// review.
/// </para>
/// <para>
/// Everything else shares one anonymous partition. That is deliberately coarse: under
/// <c>None</c> there is no identity to tell one caller from another, so a single bucket is the only bound that cannot be
/// grown by asking for it, and unauthenticated callers being able to exhaust each other's capacity is a smaller problem
/// than unauthenticated callers being able to exhaust the process's memory. The same partition absorbs a request whose
/// credential was refused, so a flood of bad credentials is counted and limited rather than served for free.
/// </para>
/// <para>
/// The remote address is deliberately not part of any key, not even as a fallback. It is spoofable on the traffic this
/// is aimed at, and one client behind a shared address would otherwise be limited by another's behaviour.
/// </para>
/// </remarks>
public static class McpRateLimitPartitions
{
    /// <summary>The one partition every request without an authenticated identity is counted under.</summary>
    /// <remarks>
    /// <para>
    /// The angle brackets are what keep it from being a client's partition as well. An authenticated request is counted
    /// under the configured name itself, and <see cref="SecretName" /> accepts letters, digits, dots, hyphens, and
    /// underscores — so a sentinel spelled in those characters could be claimed by an operator who named a key after it,
    /// which would hand that one client the whole unauthenticated stream's capacity and hand unauthenticated callers
    /// theirs. A bracketed value cannot be spelled as a name at all, and a test holds that grammar to it.
    /// </para>
    /// <para>
    /// The value never appears in a response. It is a dictionary key inside one process, named so a log or a test can
    /// refer to it.
    /// </para>
    /// </remarks>
    public const string AnonymousKey = "<anonymous>";

    /// <summary>Names the partition a request's capacity is taken from.</summary>
    /// <param name="authenticatedClientName">The name of the credential the request authenticated with, or <see langword="null" /> when it authenticated with none.</param>
    /// <returns>The configured name of the authenticated client, or <see cref="AnonymousKey" />.</returns>
    /// <remarks>
    /// The name is MailMcp's own configured identity for a credential, never the credential. It is chosen by the
    /// operator who wrote the configuration, so the set of partitions an authenticated deployment keeps is as large as
    /// the key list and no larger. A blank name is treated as no name at all rather than as a partition of its own.
    /// </remarks>
    public static string KeyFor(string? authenticatedClientName) =>
        string.IsNullOrWhiteSpace(authenticatedClientName) ? AnonymousKey : authenticatedClientName;
}
