// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in owner's mail accounts and how current the local copy of each one is.</summary>
/// <remarks>
/// <para>
/// It is the first route on this surface that answers with something about mail at all, and it is the one a client reads
/// before it can draw anything: a copy of a mailbox is only worth showing beside a statement of when it was last
/// reconciled and whether the deployment can still reach it, and those are separable facts a screen must not blur.
/// </para>
/// <para>
/// What it returns is the accounts the request's acting owner owns. It composes no deployment-wide catalog: an account
/// another owner holds is absent exactly as an account this deployment does not serve is absent, with nothing in the
/// response, its timing, or its failure modes separating the two. An owner who owns none is answered with an empty
/// collection, which is a state a client renders rather than an error, and is a different answer from the refusal a
/// credential without the grant receives.
/// </para>
/// <para>
/// Nothing of the mailbox reaches it beyond an account's own identity: no message, no subject, no correspondent, no
/// folder listing, and no mail server, port, user name, or credential. The answer is bounded by how many accounts the
/// owner has and by nothing else — not by their folders, their messages, or how many times synchronization has run.
/// </para>
/// </remarks>
internal static class ClientMailAccountsEndpoint
{
    /// <summary>The route reporting the owner's accounts, relative to the client prefix.</summary>
    internal const string MailAccountsRoute = "/accounts";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailAccounts(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailAccountsRoute, ReadAccountsAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Reports the acting owner's accounts and their synchronization freshness.</summary>
    /// <param name="reader">Reads the owner's accounts and reduces each one's folders to a single reading.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the owner's accounts, empty where they own none, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>It speaks to no mail server, so a client request cannot wait on IMAP and cannot set the remote <c>\Seen</c> flag.</remarks>
    internal static async Task<Ok<ClientMailAccountsResponse>> ReadAccountsAsync(
        [FromServices] MailAccountFreshnessReader reader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var accounts = await reader.ReadAsync(cancellationToken);

        return TypedResults.Ok(ClientMailAccountsResponse.For(accounts));
    }
}

/// <summary>What the client endpoint reports about the owner's mail accounts.</summary>
/// <param name="SynchronizationEnabled">Whether this deployment refreshes the local copy of these accounts at all.</param>
/// <param name="Accounts">One entry per account the acting owner owns, ordered by identifier, empty where they own none.</param>
/// <remarks>
/// The switch is reported beside the accounts because no per-account value carries it: a copy that last moved a week ago
/// means one thing where the deployment is still trying and another where it has stopped, and a client that could not
/// tell the two apart would show every account as failing or none of them.
/// </remarks>
internal sealed record ClientMailAccountsResponse(
    bool SynchronizationEnabled,
    IReadOnlyList<ClientMailAccountResponse> Accounts)
{
    /// <summary>Describes the owner's accounts on the wire.</summary>
    /// <param name="accounts">What the use case answered.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    internal static ClientMailAccountsResponse For(MailAccountFreshnessDirectory accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        return new ClientMailAccountsResponse(
            accounts.SynchronizationEnabled,
            [.. accounts.Accounts.Select(ClientMailAccountResponse.For)]);
    }
}

/// <summary>One of the owner's accounts, and how current its local copy is.</summary>
/// <param name="Id">The identifier the account was declared under, which a client may hold and name it by; it is unique within the owner rather than across the deployment.</param>
/// <param name="DisplayName">The name the account is published under, which is what a person recognizes; it is unique within the owner in the same way.</param>
/// <param name="SynchronizationState">Whether the deployment's last attempt at the account succeeded, failed, or has never happened, as the state's own name.</param>
/// <param name="LastSynchronizedAt">When the account last durably took anything in, or <see langword="null" /> where it never has.</param>
/// <remarks>
/// The state and the timestamp answer different halves of one question and are published apart for that reason. The
/// timestamp says how old what is being read is, and the state says whether it is still being refreshed — an account
/// that has been failing since yesterday and one nobody has written to since yesterday carry the same instant.
/// </remarks>
internal sealed record ClientMailAccountResponse(
    string Id,
    string DisplayName,
    string SynchronizationState,
    DateTimeOffset? LastSynchronizedAt)
{
    /// <summary>Describes one account on the wire.</summary>
    /// <param name="account">The account's freshness.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailAccountResponse For(MailAccountFreshness account) => new(
        account.Account.Id.Value,
        account.Account.DisplayName.Value,
        account.State.ToString(),
        account.LastSynchronizedAt);
}
