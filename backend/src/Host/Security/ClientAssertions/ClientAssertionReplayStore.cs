// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using MailFathom.Common.ClientAssertions;
using MailFathom.Infrastructure.Secrets;

namespace MailFathom.Host.Security.ClientAssertions;

/// <summary>Remembers the assertions this process has already served, so none of them is served twice.</summary>
/// <remarks>
/// <para>
/// A valid signature is not by itself a reason to serve a request: an assertion travels over the wire like any other
/// bearer credential, so anything that captures one could present it again inside its remaining seconds. Refusing an
/// identifier that has already been spent is what closes that window, and it is the one thing a short lifetime alone
/// cannot do.
/// </para>
/// <para>
/// In memory and per process, deliberately. What it protects against is a captured assertion being replayed within its
/// own lifetime, which is a window of minutes, and a restart ends every such window it was holding. Making it durable or
/// shared would put a write on the path of every authenticated request to defend a replay that has to arrive at a second
/// instance inside the same minutes — a trade a deployment behind a load balancer can take by binding a client to one
/// instance, and one this is not going to make on its behalf.
/// </para>
/// <para>
/// The store is bounded by what it accepts rather than by a cap. Only an assertion whose signature already verified is
/// remembered, so nothing an unauthenticated caller sends reaches it; an entry lives no longer than the permitted
/// assertion lifetime; and how fast a verified client can add entries is exactly what the surface's rate limit already
/// bounds. A cap with an eviction policy would be worse than none: evicting an entry that has not expired is precisely
/// the replay this exists to refuse.
/// </para>
/// </remarks>
internal sealed class ClientAssertionReplayStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> spentIdentifiers = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;

    private long nextSweepTicks;

    /// <summary>Initializes a new replay store.</summary>
    /// <param name="timeProvider">The clock an entry's expiry and the sweep interval are judged against.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public ClientAssertionReplayStore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
        this.nextSweepTicks = (timeProvider.GetUtcNow() + ClientAssertion.MaximumLifetime).UtcTicks;
    }

    /// <summary>Records one assertion as served, refusing an identifier that has already been.</summary>
    /// <param name="keyName">The public key that verified the assertion, which scopes the identifier to that client.</param>
    /// <param name="identifier">The assertion's own replay identifier.</param>
    /// <param name="expiresAt">When the assertion stops being accepted, which is when this entry stops being needed.</param>
    /// <returns><see langword="true" /> when the assertion may be served; <see langword="false" /> when it has been served before.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="identifier" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Scoped to the verifying key rather than kept as one flat set of identifiers, so no client can spend an identifier
    /// another client was going to use. It costs nothing and removes the only way one authorized client could interfere
    /// with another through this store.
    /// </para>
    /// <para>
    /// An identifier is refused for as long as its entry exists, which may briefly outlive the assertion that carried
    /// it. That is the safe direction and never refuses anything legitimate: an assertion repeating an identifier past
    /// its own expiry is already refused for the expiry, and one repeating it inside its lifetime is the replay.
    /// </para>
    /// </remarks>
    public bool TrySpend(SecretName keyName, string identifier, DateTimeOffset expiresAt) =>
        this.TrySpend(keyName.Value ?? string.Empty, identifier, expiresAt);

    /// <summary>Spends one assertion identifier against the credential that verified it.</summary>
    /// <param name="credentialKey">What identifies the verifying credential, which scopes the identifier to it.</param>
    /// <param name="identifier">The assertion's own identifier.</param>
    /// <param name="expiresAt">When the assertion stops being accepted, which is when this entry stops being needed.</param>
    /// <returns><see langword="true" /> when the assertion may be served; <see langword="false" /> when it has been served before.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either string is <see langword="null" />.</exception>
    /// <remarks>
    /// The overload taking a configured name delegates here, because the scoping is the same question whichever kind of
    /// credential verified the assertion: an owner's registered public key is identified by its fingerprint and a
    /// configured one by the name an operator gave it, and neither may spend the other's identifiers. The two
    /// vocabularies cannot collide — a fingerprint is 43 base64url characters and a configured name is not — and if one
    /// ever did, what it would cost is one client refusing another's identifier rather than admitting it.
    /// </remarks>
    public bool TrySpend(string credentialKey, string identifier, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(credentialKey);
        ArgumentNullException.ThrowIfNull(identifier);

        this.SweepExpiredEntries();

        return this.spentIdentifiers.TryAdd($"{credentialKey}\0{identifier}", expiresAt);
    }

    /// <summary>Removes the entries whose assertions have expired, at most once per permitted lifetime.</summary>
    /// <remarks>
    /// On the authentication path rather than on a timer, because a store nothing is writing to needs no sweeping and a
    /// background timer would keep a process awake to prove it. The interval is claimed with one atomic exchange, so
    /// concurrent requests produce one sweep rather than one each.
    /// </remarks>
    private void SweepExpiredEntries()
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

        foreach (var entry in this.spentIdentifiers)
        {
            if (entry.Value <= now)
            {
                this.spentIdentifiers.TryRemove(entry);
            }
        }
    }
}
