// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Common.ClientAssertions;
using MailFathom.Infrastructure.Secrets;

namespace MailFathom.Host.Security.ClientAssertions;

/// <summary>Remembers the assertions this deployment has already served, so none of them is served twice.</summary>
/// <remarks>
/// <para>
/// A valid signature is not by itself a reason to serve a request: an assertion travels over the wire like any other
/// bearer credential, so anything that captures one could present it again inside its remaining seconds. Refusing an
/// identifier that has already been spent is what closes that window, and it is the one thing a short lifetime alone
/// cannot do.
/// </para>
/// <para>
/// <b>The record is the deployment's rather than this process's.</b> It was in memory and per process once, on the
/// reading that a replay has to arrive inside a window of minutes and that a restart ends every such window — which was
/// right at one replica and stopped being right at two. An identifier spendable once per replica is not spendable once:
/// the replica that refuses a replay is not the replica the next presentation reaches, so raising <c>replicaCount</c>
/// would have turned a closed window into an open one without anything saying so. The trade that reading named — binding
/// a client to one instance — is refused by
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>,
/// because affinity is honoured by the client and the client here is whoever captured the assertion.
/// </para>
/// <para>
/// What it costs is one insert on a path that has already verified a signature and is about to serve a request that
/// reaches the database anyway, and the write is refused or accepted by PostgreSQL itself rather than by a check this
/// process makes between two statements — so two replicas presenting one identifier at the same instant leave one
/// served and one refused.
/// </para>
/// <para>
/// The cost was measured rather than assumed, because the reasoning the in-memory form rested on was that a durable one
/// is not worth a write per authenticated request. Against PostgreSQL 17 over a loopback connection, one client: a bare
/// round trip averages under half a millisecond and the spend averages a little over one, so the statement adds
/// something under a millisecond to a request that presents an assertion, and a hundred thousand rows already in the
/// table move that by nothing measurable — the insert reaches one index entry whatever the table holds. The removal
/// costs single-digit milliseconds through the expiry index where a few thousand of a hundred thousand rows have
/// expired, and tens of milliseconds where nearly all of them have and PostgreSQL scans instead; both are bounded by
/// the same fact, which is that the table only ever holds the last few minutes of a deployment's authenticated traffic.
/// </para>
/// <para>
/// The table is bounded by what it accepts and by the removal rather than by a cap. Only an assertion whose signature
/// already verified is remembered, so nothing an unauthenticated caller sends reaches it, and a record is dropped by
/// the first sweep past the point its assertion stops being accepted — which is what keeps the table proportional to
/// recent authenticated traffic rather than to a deployment's history of it. The surface's rate limit is not part of
/// that bound on the MCP surface and must not be read as one: authentication runs ahead of the limiter there, so a
/// client presenting freshly signed assertions above its permitted rate writes a record per request and is refused
/// afterwards. A cap with an eviction policy would be worse than none: evicting a record whose assertion is still
/// being accepted is precisely the replay this exists to refuse.
/// </para>
/// </remarks>
internal sealed class ClientAssertionReplayStore
{
    private readonly IClientAssertionSpendStore spentAssertions;
    private readonly TimeProvider timeProvider;

    private long nextSweepTicks;

    /// <summary>Initializes a new replay store.</summary>
    /// <param name="spentAssertions">Where the deployment records the assertions it has served.</param>
    /// <param name="timeProvider">The clock the sweep interval is judged against.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public ClientAssertionReplayStore(IClientAssertionSpendStore spentAssertions, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(spentAssertions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.spentAssertions = spentAssertions;
        this.timeProvider = timeProvider;
        this.nextSweepTicks = (timeProvider.GetUtcNow() + ClientAssertion.MaximumLifetime).UtcTicks;
    }

    /// <summary>Records one assertion as served, refusing an identifier that has already been.</summary>
    /// <param name="keyName">The public key that verified the assertion, which scopes the identifier to that client.</param>
    /// <param name="identifier">The assertion's own replay identifier.</param>
    /// <param name="expiresAt">When the assertion stops being accepted, which is when the record stops being needed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the assertion may be served; <see langword="false" /> when it has been served before.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identifier" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Scoped to the verifying key rather than kept as one flat set of identifiers, so no client can spend an identifier
    /// another client was going to use. It costs nothing and removes the only way one authorized client could interfere
    /// with another through this store.
    /// </para>
    /// <para>
    /// An identifier is refused for as long as its record exists, which outlives the assertion that carried it: the
    /// record is dropped by the first sweep after the assertion stops being verifiable, so it may survive that instant
    /// by up to one permitted lifetime. That is the safe direction and never refuses anything legitimate: an assertion
    /// repeating an identifier past the point validation still accepts it is already refused for the expiry, and one
    /// repeating it before that point is the replay.
    /// </para>
    /// </remarks>
    public Task<bool> TrySpendAsync(
        SecretName keyName,
        string identifier,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken) =>
        this.TrySpendAsync(keyName.Value ?? string.Empty, identifier, expiresAt, cancellationToken);

    /// <summary>Spends one assertion identifier against the credential that verified it.</summary>
    /// <param name="credentialKey">What identifies the verifying credential, which scopes the identifier to it.</param>
    /// <param name="identifier">The assertion's own identifier.</param>
    /// <param name="expiresAt">When the assertion stops being accepted, which is when the record stops being needed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the assertion may be served; <see langword="false" /> when it has been served before.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either string is <see langword="null" />.</exception>
    /// <remarks>
    /// The overload taking a configured name delegates here, because the scoping is the same question whichever kind of
    /// credential verified the assertion: a user's registered public key is identified by its fingerprint and a
    /// configured one by the name an operator gave it, and neither may spend the other's identifiers. The two
    /// vocabularies cannot collide — a fingerprint is 43 base64url characters and a configured name is not — and if one
    /// ever did, what it would cost is one client refusing another's identifier rather than admitting it.
    /// </remarks>
    public async Task<bool> TrySpendAsync(
        string credentialKey,
        string identifier,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentialKey);
        ArgumentNullException.ThrowIfNull(identifier);

        await this.SweepExpiredRecordsAsync(cancellationToken);

        return await this.spentAssertions.TrySpendAsync(credentialKey, identifier, expiresAt, cancellationToken);
    }

    /// <summary>Removes the records whose assertions have expired, at most once per permitted lifetime.</summary>
    /// <remarks>
    /// On the authentication path rather than on a timer, because a table nothing is writing to needs no sweeping and a
    /// background timer would keep a process awake to prove it. The interval is claimed with one atomic exchange, so
    /// concurrent requests produce one sweep rather than one each — and it stays a process's own interval above one
    /// replica, where the cost of two replicas each issuing a bounded delete once every few minutes is smaller than
    /// anything that would have to coordinate them.
    /// <para>
    /// A removal that fails takes the request that triggered it with it, which is deliberate rather than an oversight:
    /// the spend about to follow reaches the same database over the same pool, so a removal that could not run is a
    /// database this request was not going to be served by either, and swallowing the failure would hide it from the
    /// operator while changing nothing about the outcome. The interval is claimed before the statement runs, so a
    /// failure costs one deferred removal rather than a retry on the next request.
    /// </para>
    /// <para>
    /// What counts as expired here is what validation counts as expired, which is later than the assertion's own
    /// <c>exp</c>: <see cref="ClientAssertionValidation.PermittedClockSkew" /> is tolerated on either side of it, so an
    /// assertion is still accepted for that long afterwards. Removing a record at its <c>exp</c> would therefore drop
    /// the record of an assertion still being accepted, and the next presentation of that captured assertion would find
    /// no row and be served — the one failure this store exists to refuse. So the instant handed down is the one past
    /// which nothing can be presented any more rather than the one the assertion nominally expires at.
    /// </para>
    /// </remarks>
    private async Task SweepExpiredRecordsAsync(CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();
        var due = Interlocked.Read(ref this.nextSweepTicks);

        if (now.UtcTicks < due)
        {
            return;
        }

        if (Interlocked.CompareExchange(
                ref this.nextSweepTicks,
                (now + ClientAssertion.MaximumLifetime).UtcTicks,
                due) != due)
        {
            return;
        }

        await this.spentAssertions.RemoveExpiredAsync(
            now - ClientAssertionValidation.PermittedClockSkew,
            cancellationToken);
    }
}
