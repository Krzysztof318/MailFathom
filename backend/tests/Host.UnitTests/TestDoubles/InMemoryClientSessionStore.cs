// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using MailFathom.Application.Access.Sessions;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>The deployment's client sessions, held in this process so a test can reach them.</summary>
/// <remarks>
/// <para>
/// It models what the rules above the port are judged against and nothing else: a row keyed by its identifier, a
/// removal guarded by the digest, a renewal that replaces one row with another carrying the same grant, a bound
/// counting what stands, and a removal that drops exactly what has expired. Whether PostgreSQL settles a concurrent
/// pair, honours a lock order, or cascades a foreign key is not a claim any dictionary can make — the orchestrated
/// suite proves the composed statements, which is why the store is marked as requiring that coverage.
/// </para>
/// <para>
/// What it does model deliberately is the one refusal a unit test must be able to state: a user or a credential that
/// no longer admits a session, through <see cref="NoLongerAdmits" />, so the route's three answers can be told apart
/// without a database.
/// </para>
/// </remarks>
internal sealed class InMemoryClientSessionStore : IClientSessionStore
{
    private readonly ConcurrentDictionary<string, HeldClientSession> held = new(StringComparer.Ordinal);

    /// <summary>Gets or sets whether the next write finds the user or the credential behind the session gone or disabled.</summary>
    internal bool NoLongerAdmits { get; set; }

    /// <summary>Gets or sets what every operation raises instead of answering, so a test can state an unreachable store.</summary>
    internal Exception? Unreachable { get; set; }

    /// <summary>Gets how many removals of expired sessions the type above this one has asked for.</summary>
    internal int RemovalCount { get; private set; }

    /// <summary>Gets how many sessions the deployment is holding.</summary>
    internal int Count => this.held.Count;

    /// <summary>Gets what each held session admits, so a test asserts on the grant a row carries rather than on the token answering it.</summary>
    internal IReadOnlyList<ClientSessionGrant> Grants => [.. this.held.Values.Select(static session => session.Grant)];

    /// <summary>Holds one session written by something other than a mint, so a test can arrange a store it did not fill through the port.</summary>
    internal void Hold(string identifier, HeldClientSession session) => this.held[identifier] = session;

    /// <inheritdoc />
    public Task<ClientSessionMintOutcome> TryMintAsync(
        MintedClientSessionRow row,
        ClientSessionGrant grant,
        int mostLiveSessions,
        CancellationToken cancellationToken)
    {
        this.RefuseWhenUnusable(cancellationToken);

        if (this.NoLongerAdmits)
        {
            return Task.FromResult(ClientSessionMintOutcome.NoLongerAdmitted);
        }

        if (this.held.Count >= mostLiveSessions)
        {
            return Task.FromResult(ClientSessionMintOutcome.BoundReached);
        }

        this.held[row.Identifier] = new HeldClientSession(grant, row.SecretDigest, row.ExpiresAt);

        return Task.FromResult(ClientSessionMintOutcome.Minted);
    }

    /// <inheritdoc />
    public Task<HeldClientSession?> FindAsync(string identifier, CancellationToken cancellationToken)
    {
        this.RefuseWhenUnusable(cancellationToken);

        return Task.FromResult(this.held.GetValueOrDefault(identifier));
    }

    /// <inheritdoc />
    public Task<ClientSessionGrant?> RenewAsync(
        string identifier,
        ReadOnlyMemory<byte> secretDigest,
        MintedClientSessionRow replacement,
        DateTimeOffset renewableFrom,
        CancellationToken cancellationToken)
    {
        this.RefuseWhenUnusable(cancellationToken);

        if (this.held.GetValueOrDefault(identifier) is not { } presented
            || !presented.SecretDigest.Span.SequenceEqual(secretDigest.Span)
            || presented.ExpiresAt < renewableFrom
            || this.NoLongerAdmits)
        {
            return Task.FromResult<ClientSessionGrant?>(null);
        }

        this.held.TryRemove(identifier, out _);
        this.held[replacement.Identifier] = new HeldClientSession(
            presented.Grant,
            replacement.SecretDigest,
            replacement.ExpiresAt);

        return Task.FromResult<ClientSessionGrant?>(presented.Grant);
    }

    /// <inheritdoc />
    public Task<bool> RevokeAsync(
        string identifier,
        ReadOnlyMemory<byte> secretDigest,
        CancellationToken cancellationToken)
    {
        this.RefuseWhenUnusable(cancellationToken);

        if (this.held.GetValueOrDefault(identifier) is not { } presented
            || !presented.SecretDigest.Span.SequenceEqual(secretDigest.Span))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(this.held.TryRemove(identifier, out _));
    }

    /// <inheritdoc />
    public Task RemoveExpiredAsync(DateTimeOffset removableFrom, CancellationToken cancellationToken)
    {
        this.RefuseWhenUnusable(cancellationToken);

        this.RemovalCount++;

        foreach (var session in this.held.Where(session => session.Value.ExpiresAt < removableFrom))
        {
            this.held.TryRemove(session);
        }

        return Task.CompletedTask;
    }

    /// <summary>Refuses the way the real store refuses: an outage raises, and a cancelled caller stops the write.</summary>
    /// <remarks>
    /// The cancellation half matters as much as the outage half. Every statement the PostgreSQL store issues goes
    /// through Npgsql, which observes the token before it sends anything, so a caller that hands this double a
    /// cancelled token and is served anyway is being told something a deployment would not do.
    /// </remarks>
    private void RefuseWhenUnusable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (this.Unreachable is { } failure)
        {
            throw failure;
        }
    }
}
