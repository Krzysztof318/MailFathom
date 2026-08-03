// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Host.Configuration;
using MailFathom.Host.Security;
using MailFathom.Versioning;

namespace MailFathom.Host.Api;

/// <summary>Maps the administrative routes the <c>mailfathom</c> command reaches.</summary>
/// <remarks>
/// <para>
/// One route today, and it is the one <c>mailfathom login</c> exists for: a client that has just been handed a
/// credential needs to know whether this deployment accepts it before it stores it and reports success. Answering that
/// is what turns a stored credential from something an operator hopes is right into something the service confirmed.
/// </para>
/// <para>
/// It reports what the deployment knows about the caller and nothing else. There is no configuration, no account list,
/// and no mailbox here: the response names the credential that authenticated and the product version, which is what a
/// client needs to tell "signed in" from "reached something else that answers HTTP".
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
