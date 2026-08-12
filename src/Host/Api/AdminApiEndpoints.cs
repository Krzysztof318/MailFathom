// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.ApiKeys;
using MailFathom.Versioning;

namespace MailFathom.Host.Api;

/// <summary>Maps the administrative routes the <c>mailfathom</c> command reaches.</summary>
/// <remarks>
/// <para>
/// The first is the one <c>mailfathom login</c> exists for: a client that has just been handed a credential needs to
/// know whether this deployment accepts it before it stores it and reports success. Answering that is what turns a
/// stored credential from something an operator hopes is right into something the service confirmed.
/// </para>
/// <para>
/// It reports what the deployment knows about the caller and nothing else. There is no configuration, no account list,
/// and no mailbox here: the response names the credential that authenticated and the product version, which is what a
/// client needs to tell "signed in" from "reached something else that answers HTTP".
/// </para>
/// <para>
/// The second is the surface's only write, and <see cref="MailboxRefreshTokenEndpoint" /> states what that costs.
/// </para>
/// <para>
/// The third reads one account's record of the changes MailFathom made to its mailbox, which
/// <see cref="MailboxMutationAuditEndpoint" /> describes. It is here rather than on the MCP surface because its answer
/// is an operator's accountability evidence rather than anything a model reasons over, and because the credential that
/// bounds administrative access is what bounds who may read where a person's mail has been.
/// </para>
/// <para>
/// The fourth reads one account's record of the questions this deployment answered from its mailbox, which
/// <see cref="MailAnsweringAuditEndpoint" /> describes. It is here beside the third for the same reasons and one more:
/// the two together are what an operator answers "why is this message here" and "why did it answer that" from, and
/// keeping them on one credential means one thing to provision and one thing to revoke.
/// </para>
/// <para>
/// The rest are what an operator does to this deployment's embedding profile, which
/// <see cref="EmbeddingProfileEndpoints" /> describes: reading where semantic search stands, taking up what
/// configuration declares, and stopping a reindex. They are here because starting a provider bill should be bounded by
/// the same credential that bounds everything else administrative, and because none of it is anything a model reasons
/// over.
/// </para>
/// <para>
/// The last are what an operator does about this deployment's mail rules, which <see cref="MailRuleEndpoints" />
/// describes: reading which rules are loaded, asking for them to be run over a whole mailbox, and reading what they
/// did. They are here because a pass over a whole mailbox changes mail on the server, and what bounds who may ask for
/// that should be what bounds everything else administrative — and because the history is an operator's account of an
/// automation over their mailbox rather than anything a model reasons over.
/// </para>
/// <para>
/// Every one of them is mapped into one group so a route cannot be added outside the requirement the endpoint attaches
/// to it.
/// </para>
/// </remarks>
internal static class AdminApiEndpoints
{
    /// <summary>Maps the administrative routes beneath the endpoint's route prefix.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The mapped group, so the caller can attach the requirement the endpoint carries.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints" /> is <see langword="null" />.</exception>
    internal static RouteGroupBuilder MapAdminApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints.MapGroup(AdminEndpointOptions.RoutePrefix);

        api.MapGet("/session", (ClaimsPrincipal caller) => Results.Ok(AdminSessionResponse.For(caller)));
        api.MapMailboxRefreshToken();
        api.MapMailboxMutationAudit();
        api.MapMailAnsweringAudit();
        api.MapEmbeddingProfile();
        api.MapMailRules();

        return api;
    }
}

/// <summary>What the administrative endpoint reports back about an authenticated caller.</summary>
/// <param name="Service">The product this is, so a client can tell it reached MailFathom rather than something else answering the port.</param>
/// <param name="Version">The running version, which is what an operator checks before reporting behavior.</param>
/// <param name="Credential">The name of the credential that authenticated, or <c>anonymous</c> where the endpoint requires none.</param>
/// <remarks>
/// The credential's *name* is MailFathom's own configured identity for it — never the material, and never a claim an
/// authorization server supplied beyond the subject the deployment already authorized. A response that echoed more
/// would be a way to read a token's contents back out of the service.
/// </remarks>
internal sealed record AdminSessionResponse(string Service, string Version, string Credential)
{
    private const string AnonymousCaller = "anonymous";

    /// <summary>Describes the caller a validated credential produced.</summary>
    /// <param name="caller">The principal the authentication scheme produced.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="caller" /> is <see langword="null" />.</exception>
    internal static AdminSessionResponse For(ClaimsPrincipal caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        return new AdminSessionResponse(
            "MailFathom",
            StampedAssemblyVersion.ReadFrom(typeof(AdminSessionResponse).Assembly).Version,
            NameOf(caller));
    }

    /// <summary>Reports the configured name of whatever authenticated, without reaching for a claim nothing issued.</summary>
    private static string NameOf(ClaimsPrincipal caller)
    {
        if (caller.Identity is not { IsAuthenticated: true })
        {
            return AnonymousCaller;
        }

        return caller.FindFirstValue(ApiKeyAuthentication.ApiKeyNameClaimType)
            ?? caller.Identity.Name
            ?? AnonymousCaller;
    }
}
