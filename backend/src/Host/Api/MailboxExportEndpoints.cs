// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Export;
using MailFathom.Domain.Access;
using MailFathom.Domain.Exports;
using MailFathom.Domain.Failures;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the export that hands a mailbox back as a zip archive of Maildirs.</summary>
/// <remarks>
/// <para>
/// A drained account has no server behind it, so MailFathom holds the only copy of its mail and these routes are the
/// only way out of the product. They are the whole of that way: measuring what an export would carry, asking for one,
/// following it, cancelling it, downloading the archive, and deleting the archive once it has been fetched.
/// </para>
/// <para>
/// Seven routes rather than one taking an action in its body, for the reason the content move gives: measuring,
/// starting, cancelling, and deleting are different decisions about somebody's whole mailbox, and a mistyped value must
/// not be the difference between measuring one and copying it.
/// </para>
/// <para>
/// Every one of them is <c>mailfathom.admin.export</c> rather than split between reading and operating, which is what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>
/// decided and what ADR 0012's rule produces here: measuring a mailbox reports how large a copy of it would be, which
/// is a step of taking the copy rather than a reading of what the deployment holds. One grant provisions the whole act
/// and one revocation ends it.
/// </para>
/// <para>
/// They are here rather than on the MCP surface, and absent from the client, because ADR 0028 refuses the export there:
/// a copy of a whole mailbox is not something a model reasons over or a person clicks by accident.
/// </para>
/// </remarks>
internal static class MailboxExportEndpoints
{
    /// <summary>The route exports are listed and asked for on, relative to the administrative prefix.</summary>
    internal const string ExportsRoute = "/exports";

    /// <summary>The route a measurement is read from, which starts nothing.</summary>
    /// <remarks>A path of its own beneath the collection rather than a flag on the request that starts one, so a caller cannot measure and export by getting one field wrong. The identity constraint below keeps it from colliding with an export's own path.</remarks>
    internal const string MeasurementRoute = $"{ExportsRoute}/measurement";

    /// <summary>The route one export is read from and deleted on.</summary>
    internal const string ExportRoute = $"{ExportsRoute}/{{exportId:guid}}";

    /// <summary>The route an export in flight is stopped on.</summary>
    internal const string CancellationRoute = $"{ExportRoute}/cancellation";

    /// <summary>The route a finished archive is downloaded from.</summary>
    internal const string ArchiveRoute = $"{ExportRoute}/archive";

    /// <summary>The media type a zip archive is served as.</summary>
    private const string ArchiveMediaType = "application/zip";

    /// <summary>The greatest request body the one write reads before refusing it.</summary>
    /// <remarks>The body names one account and one folder, so a few hundred bytes is the whole of anything it could mean. Stated for the reason the maintenance routes state it: the server's own default would let an authenticated client make the process buffer a body four orders of magnitude larger than the request it is sending.</remarks>
    private const int MaxExportRequestBytes = 4 * 1024;

    /// <summary>Maps the export routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapMailboxExports(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MeasurementRoute, MeasureAsync)
            .RequirePermission(MailFathomPermission.AdminExport);

        api.MapGet(ExportsRoute, ListAsync)
            .RequirePermission(MailFathomPermission.AdminExport);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as the maintenance routes
        // reach it: it implements IRequestSizeLimitMetadata, which routing applies to the request body feature, so a
        // body over the bound is answered 413 before the handler is reached.
        api.MapPost(ExportsRoute, StartAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxExportRequestBytes))
            .RequirePermission(MailFathomPermission.AdminExport);

        api.MapGet(ExportRoute, ReadAsync)
            .RequirePermission(MailFathomPermission.AdminExport);

        api.MapPost(CancellationRoute, CancelAsync)
            .RequirePermission(MailFathomPermission.AdminExport);

        api.MapDelete(ExportRoute, DeleteAsync)
            .RequirePermission(MailFathomPermission.AdminExport);

        api.MapGet(ArchiveRoute, DownloadAsync)
            .RequirePermission(MailFathomPermission.AdminExport);
    }

    /// <summary>Reports what an export would carry, without starting one.</summary>
    /// <param name="account">The account to measure, as the deployment's own identifier for it.</param>
    /// <param name="folder">The one folder to measure alone, or nothing for the whole mailbox.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="exports">Performs the measurement.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the measurement, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>It reads the recorded length of each stored message and never a payload, so it answers in seconds whatever the mailbox holds.</remarks>
    internal static async Task<Results<Ok<MailboxExportMeasurementResponse>, ProblemHttpResult>> MeasureAsync(
        [FromQuery] string? account,
        [FromQuery] string? folder,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailboxExports exports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(exports);

        if (AdminAccountRequest.Resolve(account, accounts) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(account);
        }

        try
        {
            var measurement = await exports.MeasureAsync(servedAccount, folder, cancellationToken);

            return TypedResults.Ok(MailboxExportMeasurementResponse.For(measurement));
        }
        catch (MailboxExportRefusedException refusal)
        {
            return Refuse(refusal);
        }
    }

    /// <summary>Reads the exports one account has, newest first.</summary>
    /// <param name="account">The account asked about.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="exports">Reads the listing.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the exports, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<MailboxExportListResponse>, ProblemHttpResult>> ListAsync(
        [FromQuery] string? account,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailboxExports exports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(exports);

        if (AdminAccountRequest.Resolve(account, accounts) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(account);
        }

        var listing = await exports.ListAsync(servedAccount, cancellationToken);

        return TypedResults.Ok(MailboxExportListResponse.For(listing));
    }

    /// <summary>Records an export and queues the work that writes its archive.</summary>
    /// <param name="request">The account to export, and the one folder of it to cover.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="exports">Records the export.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the measurement and the export, or <c>400</c> or <c>409</c> naming why no export was started.</returns>
    /// <remarks>
    /// It records that the export is wanted and writes no archive. The job is the deployment's own background work, so
    /// the request neither carries it nor keeps it alive — which is what makes this answer immediately however large
    /// the mailbox is, and what stops an operator's terminal closing from stopping an export of their mail.
    /// <para>
    /// An export of the same scope already being written is answered with itself rather than started over, so asking
    /// twice is asking once.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<MailboxExportStartResponse>, ProblemHttpResult>> StartAsync(
        [FromBody] MailboxExportRequest request,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailboxExports exports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(exports);

        if (AdminAccountRequest.Resolve(request.Account, accounts) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(request.Account);
        }

        try
        {
            var started = await exports.StartAsync(servedAccount, request.Folder, cancellationToken);

            return TypedResults.Ok(MailboxExportStartResponse.For(started));
        }
        catch (MailboxExportRefusedException refusal)
        {
            return Refuse(refusal);
        }
    }

    /// <summary>Reads where one export stands.</summary>
    /// <param name="exportId">The export's identity.</param>
    /// <param name="account">The account it belongs to.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="exports">Reads the export.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the export, <c>404</c> when the account holds none under that identity, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<MailboxExportResponse>, ProblemHttpResult>> ReadAsync(
        Guid exportId,
        [FromQuery] string? account,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailboxExports exports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(exports);

        if (AdminAccountRequest.Resolve(account, accounts) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(account);
        }

        if (!TryReadExportId(exportId, out var identity))
        {
            return EmptyIdentity();
        }

        try
        {
            var export = await exports.ReadAsync(
                servedAccount,
                identity,
                cancellationToken);

            return TypedResults.Ok(MailboxExportResponse.For(export));
        }
        catch (MailboxExportRefusedException refusal)
        {
            return Refuse(refusal);
        }
    }

    /// <summary>Stops an export in flight, which deletes whatever it had written.</summary>
    /// <param name="exportId">The export's identity.</param>
    /// <param name="account">The account it belongs to.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="exports">Records the cancellation.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the export as it now stands, <c>404</c> when the account holds none under that identity, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>An export that has already finished is answered as it stands rather than refused, because there is nothing left to stop and saying so is the answer.</remarks>
    internal static async Task<Results<Ok<MailboxExportResponse>, ProblemHttpResult>> CancelAsync(
        Guid exportId,
        [FromQuery] string? account,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailboxExports exports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(exports);

        if (AdminAccountRequest.Resolve(account, accounts) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(account);
        }

        if (!TryReadExportId(exportId, out var identity))
        {
            return EmptyIdentity();
        }

        try
        {
            var cancelled = await exports.CancelAsync(
                servedAccount,
                identity,
                cancellationToken);

            return TypedResults.Ok(MailboxExportResponse.For(cancelled));
        }
        catch (MailboxExportRefusedException refusal)
        {
            return Refuse(refusal);
        }
    }

    /// <summary>Deletes a finished archive before its retention period ends.</summary>
    /// <param name="exportId">The export's identity.</param>
    /// <param name="account">The account it belongs to.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="exports">Removes the archive.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the export as it now stands, <c>404</c> when the account holds none under that identity, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>Deleting an archive that has already expired or been deleted succeeds, because the caller asked for a state the deployment is already in — which is what an operator deleting after a download wants to be able to repeat.</remarks>
    internal static async Task<Results<Ok<MailboxExportResponse>, ProblemHttpResult>> DeleteAsync(
        Guid exportId,
        [FromQuery] string? account,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailboxExports exports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(exports);

        if (AdminAccountRequest.Resolve(account, accounts) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(account);
        }

        if (!TryReadExportId(exportId, out var identity))
        {
            return EmptyIdentity();
        }

        try
        {
            var deleted = await exports.DeleteAsync(
                servedAccount,
                identity,
                cancellationToken);

            return TypedResults.Ok(MailboxExportResponse.For(deleted));
        }
        catch (MailboxExportRefusedException refusal)
        {
            return Refuse(refusal);
        }
    }

    /// <summary>Serves a finished archive, straight from the content store.</summary>
    /// <param name="exportId">The export's identity.</param>
    /// <param name="account">The account it belongs to.</param>
    /// <param name="accounts">Reports whether this deployment serves the named account.</param>
    /// <param name="exports">Opens the archive.</param>
    /// <param name="cancellationToken">Cancels opening the archive when the client disconnects.</param>
    /// <returns><c>200</c> with the archive, <c>404</c> when the account holds no such export, <c>409</c> when it has no archive to serve, or <c>400</c> naming what was wrong with the request.</returns>
    /// <remarks>
    /// The body is the store's own stream rather than anything this process holds, so an archive of any size is served
    /// without being read into memory first, and any replica serves it because the object is the deployment's rather
    /// than one replica's. The framework disposes the stream once the response is written, which releases the client the
    /// object was read over with it.
    /// </remarks>
    internal static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> DownloadAsync(
        Guid exportId,
        [FromQuery] string? account,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] MailboxExports exports,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(exports);

        if (AdminAccountRequest.Resolve(account, accounts) is not { } servedAccount)
        {
            return AdminAccountRequest.Refuse(account);
        }

        if (!TryReadExportId(exportId, out var identity))
        {
            return EmptyIdentity();
        }

        try
        {
            var archive = await exports.OpenArchiveAsync(
                servedAccount,
                identity,
                cancellationToken);

            return TypedResults.Stream(
                archive.Content,
                ArchiveMediaType,
                archive.FileName);
        }
        catch (MailboxExportRefusedException refusal)
        {
            return Refuse(refusal);
        }
    }

    /// <summary>Reads the export a route named, refusing the one value its identity constraint still admits.</summary>
    /// <param name="exportId">The identifier the route bound.</param>
    /// <param name="identity">The export's identity, where the value is one.</param>
    /// <returns><see langword="true" /> where the route named an identity an export can carry.</returns>
    /// <remarks>
    /// The <c>:guid</c> constraint admits the all-zero UUID like any other, and an unset script variable binds exactly
    /// that — so it reaches here as a value no export can carry. <see cref="MailboxExportId.Create" /> refuses it by
    /// raising, which no clause on these routes catches, so the guard is what turns it into the refusal the route
    /// documents rather than into an unhandled failure.
    /// </remarks>
    private static bool TryReadExportId(Guid exportId, out MailboxExportId identity)
    {
        identity = default;

        if (exportId == Guid.Empty)
        {
            return false;
        }

        identity = MailboxExportId.Create(exportId);

        return true;
    }

    /// <summary>States that the route named the one identifier no export can carry.</summary>
    private static ProblemHttpResult EmptyIdentity() => TypedResults.Problem(
        "An export identifier cannot be empty.",
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>States a refusal the use case raised, under the status its code means.</summary>
    /// <remarks>
    /// The code decides the status rather than the call site, so the same refusal reads the same way whichever route met
    /// it. An export nothing holds is <c>404</c>; an archive that is gone and an account already exporting are
    /// <c>409</c>, because both say the deployment is in a state this request cannot have; everything else is a request
    /// this deployment will not serve as written.
    /// </remarks>
    private static ProblemHttpResult Refuse(MailboxExportRefusedException refusal) => TypedResults.Problem(
        refusal.Message,
        statusCode: StatusOf(refusal.ErrorCode));

    private static int StatusOf(MailFathomErrorCode errorCode) => errorCode switch
    {
        _ when errorCode == MailFathomErrorCode.MailboxExportNotFound => StatusCodes.Status404NotFound,
        _ when errorCode == MailFathomErrorCode.MailboxExportNoLongerDownloadable => StatusCodes.Status409Conflict,
        _ when errorCode == MailFathomErrorCode.MailboxExportAlreadyRunning => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };
}
