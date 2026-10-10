// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Infrastructure.Persistence.Policies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Reads and writes the settings policy the deployment holds, and the one each organization holds.</summary>
/// <remarks>
/// <para>
/// A settings policy is what one scope says about the records it governs: the defaults a user's record and a mail
/// account start from, the values they are held to, and which of the rest the person may change. It is read and
/// saved the way a user's record is — the whole document, against the version it was read at — so there is one habit
/// for both. A stored policy governs nothing yet: no record is read through one, so these routes change what a scope
/// states and nothing about what anybody is served.
/// </para>
/// <para>
/// Reading is <see cref="MailFathomPermission.AdminRead" /> and writing
/// <see cref="MailFathomPermission.AdminConfigurationWrite" />, which already covers who this deployment serves and
/// what it reads for them. The deployment's policy names no target, so it is the deployment's alone; an
/// organization's is admitted at a scope covering that organization, and one outside the caller's scope is answered
/// as one this deployment does not hold.
/// </para>
/// </remarks>
internal static class SettingsPolicyEndpoints
{
    /// <summary>The route the deployment's policy is read at and saved back to, relative to the administrative prefix.</summary>
    internal const string DeploymentPolicyRoute = "/policy";

    /// <summary>The route one organization's policy is read at and saved back to.</summary>
    /// <remarks>Beneath the organization, because the policy is that organization's and goes with it when it is removed.</remarks>
    internal const string OrganizationPolicyRoute = $"{OrganizationEndpoints.OrganizationRoute}/policy";

    /// <summary>The greatest request body the write routes read before refusing it.</summary>
    /// <remarks>Twice what a policy may be, for the reason a user's record is read at twice its own bound: the doubling is the room JSON string escaping and the request envelope take on the way.</remarks>
    internal const int MaxWriteRequestBytes = 2 * SettingsPolicyDocument.MaximumOctets;

    /// <summary>Maps the policy routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapSettingsPolicies(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(DeploymentPolicyRoute, ReadDeploymentPolicyAsync)
            .RequirePermission(MailFathomPermission.AdminRead);

        api.MapPost(DeploymentPolicyRoute, SaveDeploymentPolicyAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        api.MapGet(OrganizationPolicyRoute, ReadOrganizationPolicyAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminRead);

        api.MapPost(OrganizationPolicyRoute, SaveOrganizationPolicyAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermissionOverTarget(MailFathomPermission.AdminConfigurationWrite);
    }

    /// <summary>Hands over the deployment's policy, as the JSON an editing session opens.</summary>
    /// <param name="policies">The policy administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the policy and the version it was read at, which is a policy stating nothing at version <c>0</c> where the deployment stores none.</returns>
    internal static async Task<Ok<SettingsPolicyResponse>> ReadDeploymentPolicyAsync(
        [FromServices] SettingsPolicyAdministration policies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policies);

        var policy = await policies.ReadAsync(organizationId: null, cancellationToken) ?? throw NoDeploymentPolicy();

        return TypedResults.Ok(SettingsPolicyResponse.For(policy));
    }

    /// <summary>Takes back the deployment's policy as an editing session saved it.</summary>
    /// <param name="policies">The policy administration.</param>
    /// <param name="request">The policy and the version the buffer was opened over.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns><c>200</c> with what the write did, or <c>400</c> when the request carries no policy.</returns>
    internal static async Task<Results<Ok<SettingsPolicyWriteResponse>, ProblemHttpResult>> SaveDeploymentPolicyAsync(
        [FromServices] SettingsPolicyAdministration policies,
        [FromBody] SettingsPolicySaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(request);

        if (FindRequestRefusal(request) is { } refused)
        {
            return refused;
        }

        var outcome = await policies.ApplyAsync(organizationId: null, request.Document!, request.Version, cancellationToken)
            ?? throw NoDeploymentPolicy();

        return TypedResults.Ok(SettingsPolicyWriteResponse.For(outcome));
    }

    /// <summary>Hands over one organization's policy, as the JSON an editing session opens.</summary>
    /// <param name="organizationId">The organization asked about.</param>
    /// <param name="policies">The policy administration.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the policy and the version it was read at, which is a policy stating nothing at version <c>0</c> where the organization stores none; <c>404</c> when this deployment holds no such organization; or <c>400</c> when the request names none.</returns>
    internal static async Task<Results<Ok<SettingsPolicyResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> ReadOrganizationPolicyAsync(
        Guid organizationId,
        [FromServices] SettingsPolicyAdministration policies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policies);

        if (organizationId == Guid.Empty)
        {
            return EmptyOrganization();
        }

        return await policies.ReadAsync(organizationId, cancellationToken) is { } policy
            ? TypedResults.Ok(SettingsPolicyResponse.For(policy))
            : NoSuchOrganization();
    }

    /// <summary>Takes back one organization's policy as an editing session saved it.</summary>
    /// <param name="organizationId">The organization whose policy is written.</param>
    /// <param name="policies">The policy administration.</param>
    /// <param name="request">The policy and the version the buffer was opened over.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns><c>200</c> with what the write did, <c>404</c> when this deployment holds no such organization, or <c>400</c> when the request names no organization or carries no policy.</returns>
    /// <remarks>An organization outside the caller's scope is answered with the same <c>404</c>, so the status code reports whether the caller's scope covers the organization rather than whether this deployment holds it.</remarks>
    internal static async Task<Results<Ok<SettingsPolicyWriteResponse>, NotFound<ProblemDetails>, ProblemHttpResult>> SaveOrganizationPolicyAsync(
        Guid organizationId,
        [FromServices] SettingsPolicyAdministration policies,
        [FromBody] SettingsPolicySaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(request);

        if (organizationId == Guid.Empty)
        {
            return EmptyOrganization();
        }

        if (FindRequestRefusal(request) is { } refused)
        {
            return refused;
        }

        return await policies.ApplyAsync(organizationId, request.Document!, request.Version, cancellationToken) is { } outcome
            ? TypedResults.Ok(SettingsPolicyWriteResponse.For(outcome))
            : NoSuchOrganization();
    }

    /// <summary>Says why a save is not one this boundary accepts, or nothing where it is.</summary>
    /// <remarks>A policy that states nothing is saved as an empty object rather than as no document, so the two cannot be confused: one is a decision to empty the policy and the other is a request that lost its body.</remarks>
    private static ProblemHttpResult? FindRequestRefusal(SettingsPolicySaveRequest request)
    {
        if (request.Version < 0)
        {
            return Refusal("A write to a settings policy states the version it was composed over, which is never negative.");
        }

        return request.Document is { Length: > 0 }
            ? null
            : Refusal("A saved settings policy carries the policy. One that states nothing is saved as an empty object.");
    }

    /// <summary>Describes the one answer the deployment's own routes cannot receive.</summary>
    /// <remarks>Absence is how an organization nobody holds is answered, and the deployment's policy names no organization to be absent.</remarks>
    private static InvalidOperationException NoDeploymentPolicy() =>
        new("The deployment's own settings policy was answered as absent, which only an organization's can be.");

    private static ProblemHttpResult EmptyOrganization() =>
        Refusal("An organization is named by the identifier this deployment recorded it under.");

    private static NotFound<ProblemDetails> NoSuchOrganization() => TypedResults.NotFound(new ProblemDetails
    {
        Status = StatusCodes.Status404NotFound,
        Detail = "This deployment holds no such organization.",
    });

    private static ProblemHttpResult Refusal(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status400BadRequest);
}
