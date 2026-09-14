// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Records;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Reports the stored records this deployment will not read, so a broken row is met rather than discovered.</summary>
/// <remarks>
/// <para>
/// Every one of these rows was written through a route that judged it, so reaching this listing means something else
/// wrote it: an older build, a tightened rule, a manual database edit, or a restored backup. What each costs is bounded
/// — a user is unserved, a mail account is not synchronized, an organization's members cannot type the prefix their
/// login begins with — and everything else keeps running, which is exactly why nothing else would tell an operator that
/// it happened.
/// </para>
/// <para>
/// The users and their mail accounts are this replica's own reading, taken as the roster was bound and replaced on every
/// convergence; the organizations are read from the rows when the route is asked, because no roster binds one. So the
/// first two answer for the process being asked and the third for the deployment — and since every replica binds the
/// same rows, an operator reading one replica learns what all of them refused.
/// </para>
/// </remarks>
internal static class HeldBackRecordEndpoints
{
    /// <summary>The route the held-back records are read at, relative to the administrative prefix.</summary>
    internal const string HeldBackRecordsRoute = "/records/held-back";

    /// <summary>Maps the held-back record route into the administrative group, so it inherits its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapHeldBackRecords(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(HeldBackRecordsRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.AdminRead);
    }

    /// <summary>Reports every record this deployment is holding back, and how many of each kind there are.</summary>
    /// <param name="heldBack">What this replica refused while binding the roster.</param>
    /// <param name="organizations">The organization administration, which reads the rows for the third kind.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the records, each naming what must change, and empty lists where nothing is held back.</returns>
    internal static async Task<Ok<HeldBackRecordListResponse>> ReadAsync(
        [FromServices] HeldBackRecords heldBack,
        [FromServices] OrganizationAdministration organizations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(heldBack);
        ArgumentNullException.ThrowIfNull(organizations);

        var refused = heldBack.Current;
        var listing = await organizations.ReadAsync(cancellationToken);

        return TypedResults.Ok(new HeldBackRecordListResponse(
            [.. OfKind(refused, HeldBackRecordKind.User)],
            [.. OfKind(refused, HeldBackRecordKind.MailAccount)],
            [
                .. listing.Unreadable.Select(organization => new HeldBackRecordResponse(
                    organization.Id,
                    organization.DisplayName,
                    RejectedVersion: null,
                    [organization.Correction])),
            ]));
    }

    private static IEnumerable<HeldBackRecordResponse> OfKind(
        IReadOnlyList<HeldBackRecord> refused,
        HeldBackRecordKind kind) =>
        refused
            .Where(record => record.Kind == kind)
            .Select(record => new HeldBackRecordResponse(
                record.Identity,
                record.Label,
                record.RejectedVersion,
                record.Corrections));
}

/// <summary>One record this deployment will not read, as the administrative surface publishes it.</summary>
/// <param name="Id">The identifier every act on the record names it by.</param>
/// <param name="Label">The operator's own text for the record.</param>
/// <param name="RejectedVersion">The version that was refused, or <see langword="null" /> for a kind of record that carries no version.</param>
/// <param name="Corrections">One sentence per setting to correct.</param>
internal sealed record HeldBackRecordResponse(
    Guid Id,
    string Label,
    long? RejectedVersion,
    IReadOnlyList<string> Corrections);

/// <summary>What this deployment is holding back, by the kind of record it is.</summary>
/// <param name="Users">The users whose own record is not one, each of whom this deployment serves nothing for.</param>
/// <param name="MailAccounts">The mail accounts left out of an otherwise readable user's record, each of which is not synchronized.</param>
/// <param name="Organizations">The organization rows this build will not read, whose members cannot type the prefix their login begins with.</param>
/// <remarks>
/// Three lists rather than one carrying a kind, because the counts an operator reads first are per kind and the remedy
/// differs for each: a user is repaired through their record, a mail account through the account, and an organization
/// through its short name. A deployment holding nothing back answers with three empty lists.
/// </remarks>
internal sealed record HeldBackRecordListResponse(
    IReadOnlyList<HeldBackRecordResponse> Users,
    IReadOnlyList<HeldBackRecordResponse> MailAccounts,
    IReadOnlyList<HeldBackRecordResponse> Organizations);
