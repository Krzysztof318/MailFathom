// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Observability.ClientTelemetry;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Signals;
using MailFathom.Versioning;

namespace MailFathom.Host.Api;

/// <summary>Maps the routes the MailFathom client reaches.</summary>
/// <remarks>
/// <para>
/// The surface was published carrying proof of life alone, because a transport surface is where a misordered middleware,
/// a scheme that authenticates the wrong caller, or a listener bound where it should not be fails silently and arrives
/// as a working deployment answering the wrong way. Those decisions have since been reviewed and tested for what they
/// are, so the routes a client actually needs arrive on top of them one at a time.
/// </para>
/// <para>
/// The session route answers what a client needs before it has drawn a single message: that this is MailFathom rather
/// than something else answering the port, which version it is, and what the credential the client just presented is
/// allowed to do. That last part is what lets sign-in be built and proven end to end before a screen exists — a client
/// that reached here with a token it had just been issued knows the token works, and knows what the rest of the surface
/// will serve it.
/// </para>
/// <para>
/// The record routes, which <see cref="ClientOwnerRecordEndpoint" /> describes, are where a person changes what this
/// deployment reads for them, and the mutation routes, which <see cref="ClientMailMutationsEndpoint" /> describes, are
/// where they change the mailbox itself. None of them names an owner: the acting owner comes off the credential, which
/// is what makes a request about somebody else something a caller cannot express here rather than something the
/// surface has to refuse.
/// </para>
/// <para>
/// The name routes, which <see cref="ClientDisplayNameEndpoint" /> describes, stand beside the record rather than
/// inside it: what this deployment records a person as is the envelope its record hangs on, so a client reading the
/// record alone would still have nothing to draw the person with. They are written under the record's own grant and
/// refused for the same person, and the read says which, so a screen is drawn from one answer.
/// </para>
/// <para>
/// The preferences routes, which <see cref="ClientPreferencesEndpoint" /> describes, are the surface's other write and
/// are deliberately not part of that record. What they hold is how somebody wants to work rather than what this
/// deployment reads for them, so it is granted, bound, and refused on its own terms — and a person whose mail accounts
/// an administrator maintains writes there while writing nothing to the record.
/// </para>
/// <para>
/// The portrait routes, which <see cref="ClientPortraitEndpoint" /> describes, hold the picture a person is drawn by.
/// They sit beside the preferences rather than in them, because a megabyte of image octets is not a small closed
/// document and reading a switch should not carry a photograph.
/// </para>
/// <para>
/// The notification routes, which <see cref="ClientNotificationEndpoints" /> describes, are the centre a person reads
/// what happened to them in while nobody was looking. They stand beside the preferences rather than among the mail
/// routes because what they serve is the deployment's own working state about a person rather than their mailbox, and
/// both ways of marking one read are admitted under the reading grant for the reason the preferences write is: a
/// person whose mail accounts an administrator maintains still has to be able to clear their own bell.
/// </para>
/// <para>
/// The citation route, which <see cref="ClientCitationEndpoint" /> describes, is where an answer stops being something
/// to be believed: it follows the citations a presentation plan declared to the mail behind them. It sits among the
/// mail routes rather than beside a run, because what it does is read the acting owner's own mail under the reading
/// grant — the plan it follows was composed somewhere else, and may have been composed for somebody else.
/// </para>
/// <para>
/// The signal ticket route, which <see cref="ClientSignalEndpoints" /> describes, is how a client obtains the
/// short-lived value it opens the live channel against. It is the one route here whose answer is a credential, and it
/// is the only part of that channel served in this group: the hub itself is mapped outside it, for the reasons that
/// type holds.
/// </para>
/// <para>
/// The telemetry routes, which <see cref="ClientTelemetryEndpoint" /> describes, are the one family here that is not
/// about mail: they take the client's own OTLP export and forward it to the collector this deployment already exports
/// to, because the collector's address and its credential belong to the deployment and a browser bundle holding either
/// would be publishing them. They exist only where a destination is configured.
/// </para>
/// <para>
/// Every route is mapped into one group so the requirement the endpoint attaches covers everything the surface serves,
/// including a route added later, and so the one filter that reads each route's published grant covers them all too.
/// </para>
/// </remarks>
internal static class ClientApiEndpoints
{
    /// <summary>The route reporting what the deployment grants the caller, relative to the client prefix.</summary>
    /// <remarks>It is what a client reads first, before it has a grant to reach anything else with.</remarks>
    internal const string SessionRoute = "/session";

    /// <summary>Maps the client routes beneath the endpoint's route prefix.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The mapped group, so the caller can attach the requirement the endpoint carries.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints" /> is <see langword="null" />.</exception>
    internal static RouteGroupBuilder MapClientApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints.MapGroup(ClientEndpointOptions.RoutePrefix);

        // On the group rather than on each route, because what a route supplies is its decision and what this supplies
        // is the enforcement: a route mapped without stating a permission is refused by this rather than served, which
        // is what makes forgetting to decide fail closed. The surface is the mailbox half, which is where this surface's
        // grants are drawn from. Group filters reach every route the group holds, whenever it was added, so nothing here
        // depends on this line staying first.
        api.AddEndpointFilter(RouteAuthorization.RefusingUnpermitted(ProtectedSurface.Mail));

        // Whether the telemetry routes are served at all, read once where they are mapped rather than per request: the
        // destination is resolved while the host is composed, so a request could only ever get the same answer more
        // expensively. It is reported because a client cannot find out any other way without exporting a batch to see
        // whether it is refused, and a switch over a deployment that forwards nothing is a control deciding nothing.
        var forwardsTelemetry = endpoints.ServiceProvider.GetService<ClientTelemetryDestination>() is not null;

        // TypedResults rather than Results, so the response type reaches the endpoint's metadata and the generated
        // OpenAPI document describes what this answers with rather than an untyped 200.
        api.MapGet(SessionRoute, (IAuthorizedPrincipalSource principals) =>
                TypedResults.Ok(ClientSessionResponse.For(principals.Current, forwardsTelemetry)))
            .RequireNoPermission();

        api.MapClientOwnerRecord();
        api.MapClientDisplayName();
        api.MapClientPreferences();
        api.MapClientPortrait();
        api.MapClientMailAccounts();
        api.MapClientMailFolders();
        api.MapClientMailTimeline();
        api.MapClientMailSearch();
        api.MapClientMailThread();
        api.MapClientMailMessage();
        api.MapClientMailBody();
        api.MapClientMailAttachment();
        api.MapClientMailMutations();
        api.MapClientCitations();
        api.MapClientDrafts();
        api.MapClientOutbox();
        api.MapClientNotifications();
        api.MapClientSignalTicket();
        api.MapClientTelemetry();

        return api;
    }
}

/// <summary>What the client endpoint reports back about an authenticated caller.</summary>
/// <param name="Service">The product this is, so a client can tell it reached MailFathom rather than something else answering the port.</param>
/// <param name="Version">The running version, which is what tells a client which contract it is talking to.</param>
/// <param name="Permissions">The published names of what this caller's grant carries, in the order this repository publishes them, and empty for a credential granted nothing.</param>
/// <param name="Telemetry">Whether this deployment forwards a client's own telemetry, which is the same answer for every caller because it is a deployment's configuration rather than a grant.</param>
/// <remarks>
/// <para>
/// It names no credential, which is the one way it differs from what the administrative surface answers. That surface's
/// reader is <c>mfctl</c> in an operator's own hands, and the deployment's configured name for the credential that
/// authenticated is what tells them which of their own entries let them in. This surface's reader is a page holding a
/// token, which brought no name and has nothing to do with one — and a response echoing a deployment's own configured
/// identity for a credential would be a way to read configuration back out of the service from a browser.
/// </para>
/// <para>
/// The grant is reported because it is the caller asking what it may do, which is the one question a caller may always
/// ask about itself. A request that established no principal reports an empty grant rather than failing, which is the
/// accurate answer to what such a caller may do.
/// </para>
/// <para>
/// Whether telemetry is forwarded stands beside the grant rather than inside it, because it is not one: every caller
/// gets the same answer, and what decides it is whether the deployment named a collector. It is reported so that a
/// client can say there is nothing behind its own telemetry switch instead of offering a control that decides nothing
/// — the alternative being to export a batch and read the <c>404</c>, which is finding out by doing the thing.
/// </para>
/// </remarks>
internal sealed record ClientSessionResponse(
    string Service,
    string Version,
    IReadOnlyList<string> Permissions,
    bool Telemetry)
{
    /// <summary>Describes what the credential that reached this route was granted.</summary>
    /// <param name="principal">What the application layer was told admitted this request, or nothing where the transport established none.</param>
    /// <param name="forwardsTelemetry">Whether this deployment serves the telemetry routes, which it does where it named a collector of its own.</param>
    /// <returns>The response body.</returns>
    internal static ClientSessionResponse For(AuthorizedPrincipal? principal, bool forwardsTelemetry) => new(
        "MailFathom",
        StampedAssemblyVersion.ReadFrom(typeof(ClientSessionResponse).Assembly).Version,
        GrantOf(principal),
        forwardsTelemetry);

    /// <summary>Names what the caller holds, in the order this repository publishes the set.</summary>
    /// <remarks>The published order rather than the grant's own, so two credentials granted the same permissions are reported identically whichever order an operator wrote them in.</remarks>
    private static IReadOnlyList<string> GrantOf(AuthorizedPrincipal? principal) => principal is null
        ? []
        : [.. MailFathomPermission.All.Where(principal.Holds).Select(permission => permission.Name)];
}
