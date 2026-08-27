// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Accounts;
using MailFathom.Client.Backend.Folders;

namespace MailFathom.Client.Backend;

/// <summary>The client's one way of asking the deployment it is pointed at something.</summary>
/// <remarks>
/// <para>
/// A transport is asked for per exchange rather than held, which is what makes the deployment this reaches follow
/// <see cref="DeploymentAddress" /> instead of whatever it was when the host was composed. No route here is ever an
/// absolute address and nothing in this assembly decides where the deployment is. The bearer token, when there is one,
/// is attached by the handler in the pipeline rather than by any method below — which is what keeps a route from being
/// written without one by accident.
/// </para>
/// <para>
/// The three routes it carries are the ones a client reads before it draws anything: what the deployment made of the
/// caller, which mailboxes that caller has beside a statement of how current each copy is, and the folders those
/// mailboxes hold. None of them reaches a mail server, so no screen here can wait on IMAP or set the remote
/// <c>\Seen</c> flag.
/// </para>
/// </remarks>
public sealed class DeploymentClient
{
    private readonly IHttpClientFactory transports;

    /// <summary>Initializes the client over the transports this assembly registered.</summary>
    /// <param name="transports">Supplies the transport aimed at the deployment, configured as each one is created.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transports" /> is <see langword="null" />.</exception>
    public DeploymentClient(IHttpClientFactory transports)
    {
        ArgumentNullException.ThrowIfNull(transports);

        this.transports = transports;
    }

    /// <summary>Asks the deployment what it makes of the credential this client is presenting.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the deployment reports about the caller.</returns>
    /// <exception cref="DeploymentFailure">Thrown when the deployment refused, was unreachable, did not answer in time, or answered with something else.</exception>
    /// <exception cref="InvalidOperationException">Thrown when nothing has pointed this client at a deployment yet.</exception>
    /// <remarks>
    /// A caller holding no token still gets an answer, because the route requires no permission at the other end: it
    /// reports an anonymous caller with an empty grant. That is what makes it usable as a reachability check once
    /// somebody has chosen a deployment; asking an address nobody has chosen yet is <see cref="DeploymentProbe" />.
    /// </remarks>
    public Task<DeploymentSession> ReadSessionAsync(CancellationToken cancellationToken = default) =>
        DeploymentExchange.ReadAsync(
            this.Transport(),
            new HttpRequestMessage(HttpMethod.Get, DeploymentRoutes.SessionPath),
            DeploymentJsonContext.Default.DeploymentSession,
            cancellationToken);

    /// <summary>Asks the deployment which mail accounts the signed-in owner has, and how current each copy is.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The owner's accounts, empty where they own none.</returns>
    /// <exception cref="DeploymentFailure">Thrown when the deployment refused, was unreachable, did not answer in time, or answered with something else.</exception>
    /// <exception cref="InvalidOperationException">Thrown when nothing has pointed this client at a deployment yet.</exception>
    /// <remarks>
    /// A caller whose grant does not carry reading mail is refused rather than answered with nothing, and reaches the
    /// screen as <see cref="DeploymentFailureReason.CredentialRefused" /> — which is what keeps an owner who owns no
    /// account from being shown the same thing as a credential that may not look.
    /// </remarks>
    public Task<DeploymentMailAccounts> ReadMailAccountsAsync(CancellationToken cancellationToken = default) =>
        DeploymentExchange.ReadAsync(
            this.Transport(),
            new HttpRequestMessage(HttpMethod.Get, DeploymentRoutes.MailAccountsPath),
            DeploymentJsonContext.Default.DeploymentMailAccounts,
            cancellationToken);

    /// <summary>Asks the deployment for the owner's mailboxes and every folder in them, as the one tree a screen is drawn from.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The owner's accounts and their folders, empty where they own no account.</returns>
    /// <exception cref="DeploymentFailure">Thrown when the deployment refused, was unreachable, did not answer in time, or answered with something else.</exception>
    /// <exception cref="InvalidOperationException">Thrown when nothing has pointed this client at a deployment yet.</exception>
    /// <remarks>
    /// A refused credential and an owner who owns no mailbox are kept apart here exactly as they are on the accounts
    /// route: the first arrives as <see cref="DeploymentFailureReason.CredentialRefused" /> and the second as an empty
    /// list, because they are two different things to put in front of somebody.
    /// </remarks>
    public Task<DeploymentMailFolders> ReadMailFoldersAsync(CancellationToken cancellationToken = default) =>
        DeploymentExchange.ReadAsync(
            this.Transport(),
            new HttpRequestMessage(HttpMethod.Get, DeploymentRoutes.MailFoldersPath),
            DeploymentJsonContext.Default.DeploymentMailFolders,
            cancellationToken);

    /// <summary>Takes a transport aimed at wherever the client is pointed right now.</summary>
    /// <remarks>
    /// Not disposed, which is the documented shape for a client an <see cref="IHttpClientFactory" /> produced: the
    /// handler behind it is pooled and outlives the client, so disposing one buys nothing and returning the same
    /// instance twice is something a factory is allowed to do.
    /// </remarks>
    private HttpClient Transport()
    {
        var transport = this.transports.CreateClient(DeploymentHttpClients.Deployment);

        return transport.BaseAddress is not null
            ? transport
            : throw new InvalidOperationException(
                "This client has not been pointed at a deployment, so there is nothing to resolve a route against. "
                + $"Point {nameof(DeploymentAddress)} at one before asking the deployment anything.");
    }
}
