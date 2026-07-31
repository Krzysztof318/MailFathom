// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Host.Security;

/// <summary>The names the MCP API key authentication scheme publishes.</summary>
/// <remarks>
/// They are constants rather than settings. The scheme name is an internal handle the composition root and the endpoint
/// convention have to agree on, and the challenge is what an HTTP client reads: <c>Bearer</c>, because that is the
/// scheme the credential is presented under and the one an MCP client already knows how to answer.
/// </remarks>
internal static class McpApiKeyAuthentication
{
    /// <summary>The authentication scheme the MCP endpoint is protected by.</summary>
    internal const string SchemeName = "MailMcpApiKey";

    /// <summary>The HTTP authentication scheme a credential is presented under, and the one a challenge names.</summary>
    internal const string HttpAuthenticationScheme = "Bearer";

    /// <summary>The protection space a challenge names, which stays constant so a client can cache a credential against it.</summary>
    internal const string Realm = "MailMcp";

    /// <summary>The claim type carrying the name of the API key a request authenticated with.</summary>
    /// <remarks>
    /// A private claim type rather than a registered one: the value is MailMcp's own configuration identity for a
    /// credential, not a subject any other system issued or would recognize. It exists so an audit record and a
    /// diagnostic can name which key was used, and it is the only thing the principal carries.
    /// </remarks>
    internal const string ApiKeyNameClaimType = "urn:mailmcp:api-key-name";

    /// <summary>The claim type a role check reads on a key's identity, which nothing ever issues.</summary>
    /// <remarks>Named rather than left empty, because an identity given an empty role type silently reverts to the framework's default; a claim type no mapping writes is what actually makes a role check answer no.</remarks>
    internal const string RoleClaimType = "urn:mailmcp:api-key-role";
}
