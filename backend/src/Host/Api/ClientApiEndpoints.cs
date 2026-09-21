// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Observability.ClientTelemetry;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Signals;
using MailFathom.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
/// The exchange and the revocation, which <see cref="ClientSessionTokenEndpoints" /> describes, are what turn a
/// credential into a session and back again. They are the reason the rest of this group costs what the query costs:
/// the exchange is where a password is derived, once, and every route below it is reached with a token that verifies
/// without deriving anything.
/// </para>
/// <para>
/// The record routes, which <see cref="ClientUserRecordEndpoint" /> describes, are where a person changes what this
/// deployment reads for them, and the mutation routes, which <see cref="ClientMailMutationsEndpoint" /> describes, are
/// where they change the mailbox itself. None of them names a user: the acting user comes off the credential, which
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
/// The contact routes, which <see cref="ClientContactEndpoints" /> describes, are the person's own address book. They
/// stand beside the mail routes rather than among them because what they hold is people rather than messages, and they
/// read it as two — what the user wrote down, and what their own mailboxes picked up — because those are two different
/// things to somebody looking at a screen. The export and the bulk collected erasure the administrative surface
/// publishes are deliberately not here, for the reasons that type holds.
/// </para>
/// <para>
/// The notification routes, which <see cref="ClientNotificationEndpoints" /> describes, are the centre a person reads
/// what happened to them in while nobody was looking. They stand beside the preferences rather than among the mail
/// routes because what they serve is the deployment's own working state about a person rather than their mailbox, and
/// both ways of marking one read are admitted under the reading grant for the reason the preferences write is: a
/// person whose mail accounts an administrator maintains still has to be able to clear their own bell.
/// </para>
/// <para>
/// The task routes, which <see cref="ClientTaskEndpoints" /> describes, are the person's own list of what they owe.
/// They stand beside the notification routes rather than among the mail ones for the same reason those do: what they
/// serve is a record native to this deployment rather than a mailbox, and every act on one is admitted under the
/// reading grant because none of them reaches a mail server. The list is read as two — what the person committed to
/// and what mail proposed — because those are two things to somebody looking at a screen.
/// </para>
/// <para>
/// The calendar routes, which <see cref="ClientCalendarEndpoints" /> describes, are the person's own days, and they
/// stand beside the task routes for the reason those stand where they do: what they hold is a record native to this
/// deployment — what somebody put on their calendar, and the dates their mail proposed to it — and no act among
/// them reaches a mail server or any calendar server at all, so every one of them is admitted under the reading grant.
/// Every read is a window, because every view over a calendar is one, and the two halves are asked for separately
/// because they are drawn in different places.
/// </para>
/// <para>
/// The contact correlation route, which <see cref="ClientContactCorrespondenceEndpoint" /> describes, is what an opened
/// contact is drawn beside: the conversations one person's addresses appear in and the documents they sent, computed
/// from the mail index on the read rather than held anywhere. It sits among the mail routes because that is what it
/// publishes — the book it keys on is read under its own grant before this one is reached.
/// </para>
/// <para>
/// The contact relationship route, which <see cref="ClientContactRelationshipEndpoint" /> describes, reads that same
/// correlation into the note the contact is headed by. It is published under the asking grant rather than the reading
/// one, because a correspondence leaves this deployment for a chat provider to derive it and the call is charged to
/// the allowance a question is — which is also why it is a route of its own rather than a field on the correlation: a
/// client that draws no card never pays for the derivation.
/// </para>
/// <para>
/// The citation route, which <see cref="ClientCitationEndpoint" /> describes, is where an answer stops being something
/// to be believed: it follows the citations a presentation plan declared to the mail behind them. It sits among the
/// mail routes rather than beside a run, because what it does is read the acting user's own mail under the reading
/// grant — the plan it follows was composed somewhere else, and may have been composed for somebody else.
/// </para>
/// <para>
/// The Discover routes, which <see cref="ClientDiscoveryRunEndpoints" /> describes, are where a question is asked of
/// years of mail and where the run answering it is read as it happens. They are two rather than one because a run
/// outlives the request that started it: a client that lost its network reattaches to the run instead of paying for the
/// same question twice, which is the whole reason the answer is a stream rather than a response.
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

        // Read beside it and at the same moment, because the two make one answer: a deployment that forwards nothing
        // asks a client for nothing whatever level it configured, and the level is what a deployment that does forward
        // asks for. It is the default rather than the answer: a person's own record may state a level of its own, and
        // that one is read per request because a record committed a moment ago must reach this person's next session
        // read without the process restarting.
        var deploymentTelemetryLevel = endpoints.ServiceProvider
            .GetRequiredService<IOptions<ClientEndpointOptions>>()
            .Value
            .TelemetryLevel;

        // TypedResults rather than Results, so the response type reaches the endpoint's metadata and the generated
        // OpenAPI document describes what this answers with rather than an untyped 200.
        // The roster is named as a service rather than left to inference: it is a concrete type, so a deployment or a
        // test that maps this route without registering it would have the binder read it as a request body instead of
        // refusing, which is a GET route acquiring one.
        api.MapGet(SessionRoute, (
                IAuthorizedPrincipalSource principals,
                [FromServices] ServedMailUsers servedUsers) =>
            {
                var principal = principals.Current;

                return TypedResults.Ok(ClientSessionResponse.For(
                    principal,
                    forwardsTelemetry,
                    deploymentTelemetryLevel,
                    StatedTelemetryLevelOf(servedUsers, principal)));
            })
            .RequireNoPermission();

        api.MapClientSessionTokens();
        api.MapClientUserRecord();
        api.MapClientDisplayName();
        api.MapClientTimeZone();
        api.MapClientPreferences();
        api.MapClientPortrait();
        api.MapClientMailAccounts();
        api.MapClientMailFolders();
        api.MapClientManagedMailFolders();
        api.MapClientMailTimeline();
        api.MapClientMailSearch();
        api.MapClientMailSearchPhrasing();
        api.MapClientMailThread();
        api.MapClientMailThreadState();
        api.MapClientMailMessage();
        api.MapClientMailBody();
        api.MapClientMailCleanedBody();
        api.MapClientMailAttachment();
        api.MapClientMailMutations();
        api.MapClientContactCorrespondence();
        api.MapClientContactRelationship();
        api.MapClientCalendarEventDrafts();
        api.MapClientCitations();
        api.MapClientDiscoveryRuns();
        api.MapClientReplyDrafting();
        api.MapClientDrafts();
        api.MapClientOutbox();
        api.MapClientContacts();
        api.MapClientNotifications();
        api.MapClientTasks();
        api.MapClientCalendar();
        api.MapClientCalendarImport();
        api.MapClientSignalTicket();
        api.MapClientTelemetry();

        return api;
    }

    /// <summary>Reads the level the acting person's own record asks their client for, where they have one and it states one.</summary>
    /// <param name="servedUsers">The roster this deployment's user records were published into.</param>
    /// <param name="principal">What admitted this request, or nothing where the transport established none.</param>
    /// <returns>The level that person's record states, or <see langword="null" /> where it states none, where the request names no user, or where the roster has not been established.</returns>
    /// <remarks>
    /// Out of the roster rather than out of a document read, for the reason every other reader of a record's own value
    /// takes it from there: it is republished by the commit that changed it, so a level an operator just raised is
    /// answered on the next session read without a restart, and a second source would be a second answer. A request
    /// that names no user is the ordinary case here rather than a refusal — this is the one route a caller granted
    /// nothing reaches — and it is served the deployment's own level, which is what it was served before any record
    /// could state one.
    /// </remarks>
    internal static ClientTelemetryLevel? StatedTelemetryLevelOf(
        ServedMailUsers servedUsers,
        AuthorizedPrincipal? principal) => principal?.User is { } user
        ? servedUsers.TryGetUsers()?.FirstOrDefault(served => served.User == user)?.ClientTelemetryLevel
        : null;
}

/// <summary>What the client endpoint reports back about an authenticated caller.</summary>
/// <param name="Service">The product this is, so a client can tell it reached MailFathom rather than something else answering the port.</param>
/// <param name="Version">The running version, which is what tells a client which contract it is talking to.</param>
/// <param name="Permissions">The published names of what this caller's grant carries, in the order this repository publishes them, and empty for a credential granted nothing.</param>
/// <param name="Telemetry">The least severe log record this deployment asks this caller's client to write, or <c>off</c> where it forwards none at all — configuration rather than a grant, and the deployment's own answer unless this person's record raised or lowered it for them alone.</param>
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
/// Whether telemetry is forwarded stands beside the grant rather than inside it, because it is not one: no permission
/// decides it, and what does is whether the deployment named a collector. It is reported so that a client can say
/// there is nothing behind its own telemetry switch instead of offering a control that decides nothing — the
/// alternative being to export a batch and read the <c>404</c>, which is finding out by doing the thing.
/// </para>
/// <para>
/// It answers the level rather than a yes, and <c>off</c> is where the yes used to be a no. One field carries both
/// because they are one question — how much this deployment wants from a client, of which none is a value — and two
/// fields would be a way for a configured level to contradict a deployment that forwards nothing. What a client does
/// with the level is refuse to write a record below it, so the floor costs nothing on the wire rather than being
/// filtered off it.
/// </para>
/// <para>
/// The level is the one thing here that is this person's rather than every caller's, which is why the field is read
/// per request rather than settled where the route is mapped. A deployment-wide floor is the wrong size for the case
/// it is mainly turned down for — one person reporting a defect — so their own record may state a level of their own,
/// and this route is where the record's answer and the deployment's are resolved into the single one a client applies.
/// </para>
/// </remarks>
internal sealed record ClientSessionResponse(
    string Service,
    string Version,
    IReadOnlyList<string> Permissions,
    string Telemetry)
{
    /// <summary>What the route answers where the deployment named no collector, which is the whole of what stops a client exporting.</summary>
    private const string NoTelemetry = "off";

    /// <summary>Describes what the credential that reached this route was granted.</summary>
    /// <param name="principal">What the application layer was told admitted this request, or nothing where the transport established none.</param>
    /// <param name="forwardsTelemetry">Whether this deployment serves the telemetry routes, which it does where it named a collector of its own.</param>
    /// <param name="deploymentTelemetryLevel">The least severe record this deployment asks every client to write, read where the routes are mapped.</param>
    /// <param name="statedTelemetryLevel">The level this person's own record asks their client for, or <see langword="null" /> where it states none and where the request is served for nobody in particular.</param>
    /// <returns>The response body.</returns>
    /// <remarks>
    /// The record's level wins over the deployment's and neither wins over the collector: a deployment forwarding
    /// nothing answers <c>off</c> however anybody's record was raised, because what is being answered there is that
    /// there is nowhere for a record to go rather than how much to write. The person's own switch is the third
    /// participant and is not resolved here at all — it is kept on their device and applied by the client, which is
    /// what lets somebody decline on one machine without deciding for the next one.
    /// </remarks>
    internal static ClientSessionResponse For(
        AuthorizedPrincipal? principal,
        bool forwardsTelemetry,
        ClientTelemetryLevel deploymentTelemetryLevel,
        ClientTelemetryLevel? statedTelemetryLevel = null) => new(
        "MailFathom",
        StampedAssemblyVersion.ReadFrom(typeof(ClientSessionResponse).Assembly).Version,
        GrantOf(principal),
        forwardsTelemetry ? (statedTelemetryLevel ?? deploymentTelemetryLevel).Published() : NoTelemetry);

    /// <summary>Names what the caller holds, in the order this repository publishes the set.</summary>
    /// <remarks>The published order rather than the grant's own, so two credentials granted the same permissions are reported identically whichever order an operator wrote them in.</remarks>
    private static IReadOnlyList<string> GrantOf(AuthorizedPrincipal? principal) => principal is null
        ? []
        : [.. MailFathomPermission.All.Where(principal.Holds).Select(permission => permission.Name)];
}
