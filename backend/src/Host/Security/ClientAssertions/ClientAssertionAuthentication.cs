// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Security.ClientAssertions;

/// <summary>The names the client assertion authentication scheme publishes, whichever surface it protects.</summary>
/// <remarks>
/// There is no HTTP scheme or realm of its own here, and that is the point of presenting an assertion as a bearer
/// credential: the challenge a client receives, the header it answers with, and the refusal it reads are the ones every
/// other method already produces. What this adds is the identity a verified assertion leaves behind.
/// </remarks>
internal static class ClientAssertionAuthentication
{
    /// <summary>The claim type carrying the name of the public key that verified a request's assertion.</summary>
    /// <remarks>
    /// A private claim type rather than a registered one, for the reason the API key's is: the value is MailFathom's own
    /// configuration identity for a credential rather than a subject any other system issued. It is what an audit
    /// record, a diagnostic, and the rate-limit partition name the caller by, and it is the only thing the principal
    /// carries — nothing out of the assertion itself reaches it.
    /// <para>
    /// Distinct from the API key's claim type although both name a configured credential, because which kind of
    /// credential authenticated is a fact worth keeping: the two are provisioned differently, rotated differently, and
    /// carry different consequences if the material behind one is lost.
    /// </para>
    /// </remarks>
    internal const string KeyNameClaimType = "urn:mailfathom:client-assertion-key-name";

    /// <summary>The claim type a role check reads on an assertion's identity, which nothing ever issues.</summary>
    /// <remarks>Named rather than left empty, because an identity given an empty role type silently reverts to the framework's default; a claim type no mapping writes is what actually makes a role check answer no.</remarks>
    internal const string RoleClaimType = "urn:mailfathom:client-assertion-role";
}
