// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.EmailContent.Move;

/// <summary>Reports where the move of stored content has got to, and how much the database still holds.</summary>
/// <remarks>
/// A reader of its own rather than a method on <see cref="StoredContentMoveControl" />, because reading what a
/// deployment holds and asking it to rewrite where it holds it are different grants. An operator watching a move they
/// started needs the first and not the second, and the deployment that answers a monitoring credential should be able to
/// say so.
/// </remarks>
public sealed class StoredContentMoveReader
{
    private readonly IStoredContentMoveRunStore runStore;
    private readonly IStoredContentMoveStore contentStore;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the reader.</summary>
    /// <param name="runStore">Reads the move this deployment last had.</param>
    /// <param name="contentStore">Counts what the database still holds.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public StoredContentMoveReader(
        IStoredContentMoveRunStore runStore,
        IStoredContentMoveStore contentStore,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(authorization);

        this.runStore = runStore;
        this.contentStore = contentStore;
        this.authorization = authorization;
    }

    /// <summary>Reads the move and the backlog behind it.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The move, where there is one, together with what the database still holds.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <remarks>
    /// The backlog is counted whether or not a move exists, because that is the figure an operator weighs before asking
    /// for one. It is an aggregate over the four content tables and is therefore read on request rather than published
    /// as a series: what it costs is proportional to the mail stored, and nothing needs it on a scrape interval.
    /// </remarks>
    public async Task<StoredContentMoveProgress> ReadAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        var run = await this.runStore.FindAsync(cancellationToken);
        var backlog = await this.contentStore.CountPayloadsAwaitingMoveAsync(cancellationToken);

        return new StoredContentMoveProgress(run, backlog);
    }
}
