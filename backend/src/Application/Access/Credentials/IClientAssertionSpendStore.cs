// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access.Credentials;

/// <summary>Where the deployment records the client assertions it has served, so no replica serves one of them again.</summary>
/// <remarks>
/// <para>
/// The port exists because the property it carries is the deployment's rather than a process's. An identifier spendable
/// once per replica is not spendable once at all: the replica that refuses a replay is not the replica the next
/// presentation reaches, and nothing about routing puts that back, because the caller replaying a captured assertion is
/// the one who would have had to honour the affinity.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// records both halves of that.
/// </para>
/// <para>
/// What is remembered is a verifying credential, an identifier the client minted, and when the assertion carrying it
/// stops being accepted. Nothing from a request, a message, or a key reaches it, and only an assertion whose signature
/// already verified is offered — so what an unauthenticated caller can put here is nothing.
/// </para>
/// </remarks>
public interface IClientAssertionSpendStore
{
    /// <summary>Spends one identifier against the credential that verified the assertion carrying it.</summary>
    /// <param name="credentialKey">What identifies the verifying credential, which scopes the identifier to it.</param>
    /// <param name="identifier">The assertion's own replay identifier.</param>
    /// <param name="expiresAt">When the assertion stops being accepted, which is when the record stops being needed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when this call is the one that spent the identifier; <see langword="false" /> when something already had.</returns>
    /// <remarks>
    /// One statement whose own outcome is the answer, so two replicas presenting one identifier at the same instant
    /// leave one served and one refused with PostgreSQL settling it rather than a check between two statements. An
    /// implementation that read and then wrote would admit both.
    /// </remarks>
    Task<bool> TrySpendAsync(
        string credentialKey,
        string identifier,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    /// <summary>Forgets the identifiers whose assertions can no longer be presented.</summary>
    /// <param name="now">The instant an assertion's expiry is judged against.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the expired records are gone.</returns>
    /// <remarks>
    /// Bounded by what it can match rather than by a limit the caller passes: an implementation reaches the expired
    /// records through an ordering on the expiry, so the work is proportional to what has expired since the last
    /// removal rather than to everything ever spent.
    /// </remarks>
    Task RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
