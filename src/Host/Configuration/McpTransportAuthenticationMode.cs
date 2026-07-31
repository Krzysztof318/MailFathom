// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Host.Configuration;

/// <summary>What a deployment requires of a client before it serves an MCP request.</summary>
/// <remarks>
/// <para>
/// A deployment that enables the endpoint states one of these explicitly. There is no default, because the only
/// candidate for one would be the unauthenticated posture, and a security control nobody chose is exactly the outcome
/// this setting exists to prevent: a misspelled key, an absent section, or a value that failed to bind would all end up
/// serving mail to anything that can reach the address.
/// </para>
/// <para>
/// <see cref="ApiKey" /> is declared first so that it, rather than <see cref="None" />, is the value a defaulted field
/// of this type carries. Nothing relies on that today — the setting is nullable and validated — but the ordering costs
/// nothing and keeps the accident-prone direction closed.
/// </para>
/// </remarks>
internal enum McpTransportAuthenticationMode
{
    /// <summary>A request presents one of the configured API keys as an HTTP <c>Bearer</c> credential.</summary>
    ApiKey = 0,

    /// <summary>No transport authentication at all, which is a deliberate choice for a local or network-isolated deployment and warns at startup.</summary>
    None = 1,
}
