// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Signals;
using MailFathom.Infrastructure.Persistence.Policies;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>Reads and writes the settings policy the deployment holds, and the one each organization holds.</summary>
/// <remarks>
/// <para>
/// A policy is read and written whole, against the version it was read at. Nothing here patches one: the caller saves
/// a document, the document is judged on its own by <see cref="SettingsPolicyCandidate" />, and what passes is
/// committed as one statement or not at all.
/// </para>
/// <para>
/// A stored policy governs nothing yet. No record is read through one, so a write here changes what the scope states
/// and nothing about what any user or mail account is served; what it is judged by is therefore the document alone.
/// </para>
/// <para>
/// The deployment's policy is the deployment's to write, so it takes the grant over the whole deployment. An
/// organization's accepts the same grant held at that organization, which is what lets an organization's own
/// administrator write its policy and no other; an organization the caller's scope does not cover is answered as one
/// this deployment does not hold, as every other route naming one answers.
/// </para>
/// <para>
/// A policy is never logged and never quoted in a refusal. By the rules it is judged under it carries no name, no
/// address, and no credential of anybody it governs, but a forced list of trusted senders is still a list of
/// somebody's correspondents, so it is handled as the records it governs are.
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class SettingsPolicyAdministration(
    AccessAuthorization authorization,
    ISettingsPolicyStore store,
    ConfigurationChangeAnnouncements announcements)
{
    /// <summary>Reads the policy one scope holds.</summary>
    /// <param name="organizationId">The organization, or <see langword="null" /> for the deployment.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The policy, stating nothing where the scope stores none; or <see langword="null" /> when this deployment holds no such organization or the caller's scope does not cover it.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminRead" />, or holds it below the deployment where the deployment's own policy is asked for.</exception>
    internal async Task<SettingsPolicyDocument?> ReadAsync(Guid? organizationId, CancellationToken cancellationToken) =>
        await this.CoversAsync(MailFathomPermission.AdminRead, organizationId, cancellationToken)
            ? await store.ReadAsync(organizationId, cancellationToken)
            : null;

    /// <summary>Replaces the policy one scope holds with the one the caller saved.</summary>
    /// <param name="organizationId">The organization, or <see langword="null" /> for the deployment.</param>
    /// <param name="documentJson">The whole policy as the caller saved it.</param>
    /// <param name="expectedVersion">The version the saved policy was composed over.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>What the write did, or <see langword="null" /> when this deployment holds no such organization or the caller's scope does not cover it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="documentJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />, or holds it below the deployment where the deployment's own policy is written.</exception>
    /// <remarks>
    /// The version is checked before the candidate is judged as well as in the statement, so a policy composed over
    /// one somebody else has replaced is told so rather than told about faults in a document it is about to compose
    /// again. Two administrators writing at once are settled by the statement: the second to commit matches no row,
    /// whichever replica each reached, and is answered with the version the first produced.
    /// </remarks>
    internal async Task<SettingsPolicyWriteOutcome?> ApplyAsync(
        Guid? organizationId,
        string documentJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documentJson);

        if (!await this.CoversAsync(MailFathomPermission.AdminConfigurationWrite, organizationId, cancellationToken)
            || await store.ReadAsync(organizationId, cancellationToken) is not { } inForce)
        {
            return null;
        }

        if (inForce.Version != expectedVersion)
        {
            return Superseded(expectedVersion, inForce.Version);
        }

        var candidate = SettingsPolicyCandidate.Judge(documentJson);

        if (candidate.Json is not { } judged)
        {
            return SettingsPolicyWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                inForce.Version,
                candidate.Refusals);
        }

        if (JsonNode.DeepEquals(JsonNode.Parse(inForce.Json), JsonNode.Parse(judged)))
        {
            return SettingsPolicyWriteOutcome.NothingToChange(
                inForce.Version,
                $"The saved policy states what the policy in force already states, so nothing was written and version {inForce.Version} stays in force.");
        }

        if (await store.CommitAsync(organizationId, judged, expectedVersion, cancellationToken) is not { } committed)
        {
            // The policy moved while this candidate was being judged, or the organization was removed under it. Which
            // of the two is settled by reading rather than assumed, because the statement distinguishes neither.
            return await store.ReadAsync(organizationId, cancellationToken) is { } current
                ? Superseded(expectedVersion, current.Version)
                : null;
        }

        // The fast path only, as it is for a committed record: every replica converges on a version on its own
        // interval whether or not this reaches it.
        await announcements.AnnounceAsync();

        return SettingsPolicyWriteOutcome.Committed(committed);
    }

    /// <summary>Requires the capability an act on a policy is published under, and reports whether the caller holds it over the scope named.</summary>
    /// <remarks>
    /// The deployment's policy names no target, so a grant held below the deployment is refused rather than answered
    /// as nothing found: there is exactly one such policy, and whether it exists is not a fact to withhold.
    /// </remarks>
    private async Task<bool> CoversAsync(
        MailFathomPermission permission,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        if (organizationId is not { } organization)
        {
            authorization.RequirePermission(permission);

            return true;
        }

        authorization.RequirePermissionAtAnyScope(permission);

        return await authorization.PermitsOverScopeAsync(
            permission,
            AssignmentScope.Organization(organization),
            cancellationToken);
    }

    private static SettingsPolicyWriteOutcome Superseded(long composedOver, long inForce) =>
        SettingsPolicyWriteOutcome.Refused(
            MailFathomErrorCode.ConfigurationVersionSuperseded,
            inForce,
            [
                $"The change was composed over settings policy version {composedOver}, and version {inForce} is in force. Read the policy as it now stands and decide again against it.",
            ]);
}
