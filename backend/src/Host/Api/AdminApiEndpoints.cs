// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Claims;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Transport;
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
/// and no mailbox here: the response names the credential that authenticated, the product version, and the permissions
/// that credential's grant carries — which is what a client needs to tell "signed in" from "reached something else that
/// answers HTTP", and what the caller needs to learn what the rest of this surface will serve it. Each of the three is
/// something the caller brought or may ask about itself, which is why this is the one route published under no
/// permission.
/// </para>
/// <para>
/// The second is the surface's only write, and <see cref="MailboxRefreshTokenEndpoint" /> states what that costs.
/// </para>
/// <para>
/// The third reports what synchronization is doing, which <see cref="MailboxSynchronizationStatusEndpoint" />
/// describes. It is here because a deployment that is failing to fetch mail looks from outside exactly like one whose
/// mailbox is quiet, and telling the two apart is an operator's question rather than anything a model reasons over.
/// </para>
/// <para>
/// The fourth pair brings stored mail up to the properties a newer release records, which
/// <see cref="MailboxMaintenanceEndpoints" /> describes: discarding an account's synchronization progress so its
/// folders are read afresh, and re-reading the raw MIME already stored. They are here because the first of them makes a
/// deployment pull a mailbox over IMAP again, which is an operator's decision about somebody's bandwidth and storage
/// rather than anything a model reasons over.
/// </para>
/// <para>
/// The four after them carry the mail already stored into the object backend, which
/// <see cref="ContentMoveEndpoints" /> describes: reading how much of it is still database-backed and what the
/// current move has carried, asking for one, pausing it, and taking it up again. They are here because selecting the
/// backend decides where the next payload is written and says nothing about what is already stored, so carrying that
/// across is a decision about this deployment's storage and about what its endpoint will be billed for rather than
/// anything a model reasons over.
/// </para>
/// <para>
/// The pair after those ends the duplication that move leaves behind, which <see cref="ContentReleaseEndpoints" />
/// describes: reading how much of the database is a copy of the bucket, and freeing a bounded batch of it. They are
/// here for the reason the move is, and one of their own — freeing a copy removes the last one this deployment holds
/// outside its endpoint, so it is asked for under the grant this surface allocates to disposing of what it holds rather
/// than under the one that started the move.
/// </para>
/// <para>
/// The next reads one account's record of the changes MailFathom made to its mailbox, which
/// <see cref="MailboxMutationAuditEndpoint" /> describes. It is here rather than on the MCP surface because its answer
/// is an operator's accountability evidence rather than anything a model reasons over, and because the credential that
/// bounds administrative access is what bounds who may read where a person's mail has been.
/// </para>
/// <para>
/// The one after it reads one account's record of the questions this deployment answered from its mailbox, which
/// <see cref="MailAnsweringAuditEndpoint" /> describes. It is here beside the mutation trail for the same reasons and one more:
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
/// The next are what an operator does about this deployment's mail rules, which <see cref="MailRuleEndpoints" />
/// describes: reading which rules are loaded, asking for them to be run over a whole mailbox, and reading what they
/// did. They are here because a pass over a whole mailbox changes mail on the server, and what bounds who may ask for
/// that should be what bounds everything else administrative — and because the history is an operator's account of an
/// automation over their mailbox rather than anything a model reasons over.
/// </para>
/// <para>
/// The next three are what an operator does about background work that stopped, which
/// <see cref="JobDeadLetterEndpoints" /> describes: reading what has dead-lettered, running one again after fixing what
/// caused it, and recording that one will never be run. They are here because re-running work that changes somebody's
/// mailbox should be bounded by the same credential as asking for it in the first place, and because a queue's terminal
/// state is an operator's problem rather than anything a model reasons over.
/// </para>
/// <para>
/// The next five are what an operator does about the outbox, which <see cref="OutboxEndpoints" /> describes: reading
/// how much stands at each stage, listing what is queued and what failed, reading one send and what each of its
/// recipients was told, withdrawing one that has not begun transmitting, and offering one again. They are here because
/// putting a message back on its way to somebody's mailbox should be bounded by the same credential as asking for the
/// send in the first place — and because the one send nothing will decide for itself, whose server never answered, is a
/// question for a person rather than anything a model reasons over.
/// </para>
/// <para>
/// The next takes a folder's local mail away, which <see cref="MailFolderErasureEndpoint" /> describes. It is the only
/// route that disposes of stored mail, which is why it is bounded by the same credential as everything else here and
/// reachable from nowhere a model can write to.
/// </para>
/// <para>
/// The last are the contact book, which <see cref="ContactEndpoints" /> describes: recording a person, correcting one,
/// promoting a collected record, reading the book, and the two data-subject paths over it. They are here because the
/// book is the most concentrated personal data this deployment holds, so what bounds administrative access is what
/// should bound who may add to it, read it out, or erase somebody from it.
/// </para>
/// <para>
/// Every one of them is mapped into one group so a route cannot be added outside the requirement the endpoint attaches
/// to it, and so the one filter that reads each route's published permission covers every route the group holds.
/// </para>
/// </remarks>
internal static class AdminApiEndpoints
{
    /// <summary>The route reporting what the deployment knows about the caller, relative to the administrative prefix.</summary>
    /// <remarks>
    /// The one administrative route published under no permission. It discloses nothing a caller did not bring — the
    /// credential it presented, the version this deployment already publishes, and what its own grant carries — and it
    /// is what every command reads first, <c>mfctl login</c> included. Putting it behind a permission would make that
    /// permission a component of every administrative grant, so a credential granted only the spend permission could not
    /// sign in to use it. A credential granted nothing therefore still answers here and nowhere else; an operator who
    /// wants nothing answered at all removes the entry.
    /// </remarks>
    internal const string SessionRoute = "/session";

    /// <summary>Maps the administrative routes beneath the endpoint's route prefix.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <returns>The mapped group, so the caller can attach the requirement the endpoint carries.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints" /> is <see langword="null" />.</exception>
    internal static RouteGroupBuilder MapAdminApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints.MapGroup(AdminEndpointOptions.RoutePrefix);

        // On the group rather than on each route, because what a route supplies is its decision and what this supplies
        // is the enforcement: a route mapped without stating a permission is refused by this rather than served, which
        // is what makes forgetting to decide fail closed. The surface is stated here rather than on each route, because
        // it is the group that decides which half of the published set these grants come from. Group filters reach every
        // route the group holds, whenever it was added, so nothing here depends on this line staying first.
        api.AddEndpointFilter(RouteAuthorization.RefusingUnpermitted(ProtectedSurface.Administration));

        // TypedResults rather than Results, so the response type reaches the endpoint's metadata and the generated
        // OpenAPI document describes what this answers with rather than an untyped 200.
        api.MapGet(SessionRoute, (ClaimsPrincipal caller, IAuthorizedPrincipalSource principals) =>
                TypedResults.Ok(AdminSessionResponse.For(caller, principals.Current)))
            .RequireNoPermission();

        api.MapMailboxRefreshToken();
        api.MapMailboxSynchronizationStatus();
        api.MapMailboxMaintenance();
        api.MapContentMove();
        api.MapContentRelease();
        api.MapMailboxMutationAudit();
        api.MapMailAnsweringAudit();
        api.MapEmbeddingProfile();
        api.MapMailRules();
        api.MapSpamClassification();
        api.MapJobDeadLetters();
        api.MapOutbox();
        api.MapMailFolderErasure();
        api.MapContacts();

        return api;
    }
}

/// <summary>What the administrative endpoint reports back about an authenticated caller.</summary>
/// <param name="Service">The product this is, so a client can tell it reached MailFathom rather than something else answering the port.</param>
/// <param name="Version">The running version, which is what an operator checks before reporting behavior.</param>
/// <param name="Credential">The name of the credential that authenticated, or <c>anonymous</c> where the endpoint requires none.</param>
/// <param name="Permissions">The published names of what this caller's grant carries, in the order this repository publishes them, and empty for a credential granted nothing.</param>
/// <remarks>
/// <para>
/// The credential's *name* is MailFathom's own configured identity for it — never the material, and never a claim an
/// authorization server supplied beyond the subject the deployment already authorized. A response that echoed more
/// would be a way to read a token's contents back out of the service.
/// </para>
/// <para>
/// The grant is reported for the same reason the route requires none: it is the caller asking what it may do, which is
/// the one question a caller may always ask about itself. It is also what an operator reads instead of their own
/// configuration file — a grant nobody narrowed reaches the whole surface, and reading that back is how they meet the
/// posture rather than infer it from what a credential turned out to be able to do.
/// </para>
/// </remarks>
internal sealed record AdminSessionResponse(
    string Service,
    string Version,
    string Credential,
    IReadOnlyList<string> Permissions)
{
    /// <summary>Describes the caller a validated credential produced, and what it was granted.</summary>
    /// <param name="caller">The principal the authentication scheme produced.</param>
    /// <param name="principal">What the application layer was told admitted this request, or nothing where the transport established none.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="caller" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A request that established no principal reports an empty grant rather than failing. This route requires no
    /// permission, so it answers a caller the rest of the surface refuses, and saying "nothing" is the accurate answer
    /// to what such a caller may do.
    /// </remarks>
    internal static AdminSessionResponse For(ClaimsPrincipal caller, AuthorizedPrincipal? principal)
    {
        ArgumentNullException.ThrowIfNull(caller);

        return new AdminSessionResponse(
            "MailFathom",
            StampedAssemblyVersion.ReadFrom(typeof(AdminSessionResponse).Assembly).Version,
            NameOf(caller),
            GrantOf(principal));
    }

    /// <summary>Names what the caller holds, in the order this repository publishes the set.</summary>
    /// <remarks>The published order rather than the grant's own, so two credentials granted the same permissions are reported identically whichever order an operator wrote them in.</remarks>
    private static IReadOnlyList<string> GrantOf(AuthorizedPrincipal? principal) => principal is null
        ? []
        : [.. MailFathomPermission.All.Where(principal.Holds).Select(permission => permission.Name)];

    /// <summary>Reports the configured name of whatever authenticated, or that nothing did.</summary>
    /// <remarks>The naming rule is the transport's own, shared with what the application layer is told the work is running for, so this response and a record of a refusal cannot call one caller two things.</remarks>
    private static string NameOf(ClaimsPrincipal caller) =>
        TransportCallerIdentity.NameOf(caller) ?? TransportCallerIdentity.AnonymousCaller;
}
