// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Accounts;
using MailFathom.Client.Backend.Folders;
using MailFathom.Client.Backend.Mail;
using MailFathom.Client.Backend.Search;
using MailFathom.Client.Backend.Threads;
using MailFathom.Client.Backend.Timeline;

namespace MailFathom.Client.Backend;

/// <summary>The client's one way of asking the deployment it is pointed at something.</summary>
/// <remarks>
/// <para>
/// A transport is asked for per exchange rather than held, which is what makes the deployment this reaches follow
/// <see cref="DeploymentAddress" /> instead of whatever it was when the host was composed. No route here is ever an
/// absolute address and nothing in this assembly decides where the deployment is. The signed-in owner's credential,
/// when there is one, is presented by the handler in the pipeline rather than by any method below — which is what
/// keeps a route from being written without one by accident.
/// </para>
/// <para>
/// Three of the routes it carries are the ones a client reads before it draws anything: what the deployment made of the
/// caller, which mailboxes that caller has beside a statement of how current each copy is, and the folders those
/// mailboxes hold. The rest are the mail itself — one page of the message list asked for by cursor, which is where a
/// mail screen spends its time; one conversation across every folder and account it spans; and one message's body in
/// the two renderings a reading pane draws it from. None of them reaches a mail server, so no screen here can wait on
/// IMAP or set the remote <c>\Seen</c> flag.
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
    /// A caller holding no credential still gets an answer, because the route requires no permission at the other end: it
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

    /// <summary>Asks the deployment for one page of the owner's message list.</summary>
    /// <param name="query">The place, the filters, the order, and where the page continues from.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page, and the cursor continuing it at each end where one exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="DeploymentFailure">Thrown when the deployment refused, was unreachable, did not answer in time, or answered with something else.</exception>
    /// <exception cref="InvalidOperationException">Thrown when nothing has pointed this client at a deployment yet.</exception>
    /// <remarks>
    /// A cursor the deployment will not honour — one it never issued, or one issued for a list the request no longer
    /// describes — arrives as <see cref="DeploymentFailureReason.RequestRefused" /> rather than as a defect, because it
    /// is a value this client sent and can therefore stop sending.
    /// </remarks>
    public Task<DeploymentMailTimelinePage> ReadMailTimelineAsync(
        MailTimelineQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return DeploymentExchange.ReadAsync(
            this.Transport(),
            new HttpRequestMessage(
                HttpMethod.Get,
                $"{DeploymentRoutes.MailTimelinePath}{query.QueryString()}"),
            DeploymentJsonContext.Default.DeploymentMailTimelinePage,
            cancellationToken);
    }

    /// <summary>Asks the deployment for one forward page of mail ranked against a query.</summary>
    /// <param name="query">The text, scope, filters, and cursor that define the ranked list.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page and what explains both its rows and the ranking available.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    public Task<DeploymentMailSearchPage> SearchMailAsync(
        MailSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return DeploymentExchange.ReadAsync(
            this.Transport(),
            new HttpRequestMessage(HttpMethod.Get, $"{DeploymentRoutes.MailSearchPath}{query.QueryString()}"),
            DeploymentJsonContext.Default.DeploymentMailSearchPage,
            cancellationToken);
    }

    /// <summary>Asks the deployment for one page of one of the owner's conversations.</summary>
    /// <param name="threadId">The conversation to read, as a message row published it.</param>
    /// <param name="pageSize">How many messages the page may hold, or <see langword="null" /> for the deployment's own default.</param>
    /// <param name="cursor">The cursor a previous page returned, or <see langword="null" /> for the beginning of the conversation.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page, the whole conversation's participants and counts beside it, and the cursor continuing it where one exists.</returns>
    /// <exception cref="DeploymentFailure">Thrown when the deployment refused, was unreachable, did not answer in time, or answered with something else.</exception>
    /// <exception cref="InvalidOperationException">Thrown when nothing has pointed this client at a deployment yet.</exception>
    /// <remarks>
    /// A cursor the deployment will not honour — one it never issued, one issued for another conversation, and one
    /// naming a message the conversation no longer shows — arrives as
    /// <see cref="DeploymentFailureReason.RequestRefused" />, because it is a value this client sent and can therefore
    /// stop sending. A conversation this owner does not hold and one no deployment ever held are answered identically
    /// by the deployment, so nothing here separates the two either.
    /// </remarks>
    public Task<DeploymentMailThreadPage> ReadMailThreadAsync(
        Guid threadId,
        int? pageSize = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        DeploymentExchange.ReadAsync(
            this.Transport(),
            new HttpRequestMessage(HttpMethod.Get, DeploymentRoutes.MailThreadPath(threadId, pageSize, cursor)),
            DeploymentJsonContext.Default.DeploymentMailThreadPage,
            cancellationToken);

    /// <summary>Asks the deployment for everything a reading pane draws around one message.</summary>
    public Task<DeploymentMailMessageDetail> ReadMailMessageAsync(
        Guid storedEmailId,
        CancellationToken cancellationToken = default) =>
        DeploymentExchange.ReadAsync(
            this.Transport(),
            new HttpRequestMessage(HttpMethod.Get, DeploymentRoutes.MailMessagePath(storedEmailId)),
            DeploymentJsonContext.Default.DeploymentMailMessageDetail,
            cancellationToken);

    /// <summary>Asks the deployment for one message's body, in both the renderings a reading pane draws it from.</summary>
    /// <param name="storedEmailId">The message to read, as a list row or a conversation published it.</param>
    /// <param name="remoteImages">Whether the reader has asked for this message's remote pictures, having been told what that reveals.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The message's body.</returns>
    /// <exception cref="DeploymentFailure">Thrown when the deployment refused, was unreachable, did not answer in time, or answered with something else.</exception>
    /// <exception cref="InvalidOperationException">Thrown when nothing has pointed this client at a deployment yet.</exception>
    /// <remarks>
    /// Asking for remote pictures is a second call rather than a setting, and neither end keeps the answer: the
    /// deployment reads it off the query and this client writes it from what the reader has just done. Opening the
    /// message again therefore asks again, which is the point — a remembered allowance is a standing consent that
    /// outlives the reason it was given.
    /// </remarks>
    public Task<DeploymentMailBody> ReadMailBodyAsync(
        Guid storedEmailId,
        bool remoteImages = false,
        CancellationToken cancellationToken = default) =>
        DeploymentExchange.ReadAsync(
            this.Transport(),
            new HttpRequestMessage(HttpMethod.Get, DeploymentRoutes.MailBodyPath(storedEmailId, remoteImages)),
            DeploymentJsonContext.Default.DeploymentMailBody,
            cancellationToken,
            DeploymentExchange.MaxMailBodyBytes);

    /// <summary>Streams one attachment into the destination a reader chose.</summary>
    public Task DownloadMailAttachmentAsync(
        Guid storedEmailId,
        int position,
        long expectedSizeOctets,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedSizeOctets);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The attachment destination must be writable.", nameof(destination));
        }

        return DeploymentExchange.CopyAsync(
            this.Transport(),
            new HttpRequestMessage(HttpMethod.Get, DeploymentRoutes.MailAttachmentPath(storedEmailId, position)),
            expectedSizeOctets,
            destination,
            cancellationToken);
    }

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
