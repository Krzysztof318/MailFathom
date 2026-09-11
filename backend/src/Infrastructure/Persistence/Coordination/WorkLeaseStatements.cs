// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;

namespace MailFathom.Infrastructure.Persistence.Coordination;

/// <summary>Composes the statements a lease's whole life is, and the one that reads what they left.</summary>
/// <remarks>
/// <para>
/// Written rather than composed through the query provider, because each of them is the mechanism rather than a query.
/// The claim's exclusion is <c>ON CONFLICT</c> against the primary key: selecting the row and then deciding whether to
/// take it would leave the window in which two replicas both read a lease that had expired. The renewal and the
/// release are each one conditional update whose predicate is the holder, which is what makes a write from a hold that
/// was already reclaimed write nothing rather than merely be unlikely to.
/// </para>
/// <para>
/// <strong>Every instant is PostgreSQL's own.</strong> A lease is stamped and judged against <c>now()</c> rather than
/// against a clock the asking process read, and the duration crosses the boundary as an interval instead. Two replicas
/// hold one scope apart by comparing an expiry, so a comparison made against each replica's own clock would be decided
/// by the drift between them — a replica running minutes fast would find a live lease expired and take it. One clock
/// for the whole deployment is what makes the exclusion hold through a clock that drifted, which is also why the
/// expiry the caller is told about is read back out of the row rather than computed beside it.
/// </para>
/// <para>
/// They are a type of their own so the statements can be read and asserted without a database. Losing the expiry
/// comparison, the conflict target, or either holder predicate would each fail silently at run time — as two replicas
/// holding one scope, as a claim that refused a scope nothing held, or as a late release freeing somebody else's hold
/// — so the statements are verified as text.
/// </para>
/// <para>
/// The read is here beside them because it has to judge an expiry the same way, and for no other reason: it decides
/// nothing, holds nothing, and is composed as a statement only so that <c>now()</c> stays the one clock every reading
/// of this table is made against. A reader comparing the stored expiry with its own clock would report a held scope as
/// free on a replica running fast, which is the same drift the exclusion itself is written to survive.
/// </para>
/// <para>
/// Every value is a parameter. The identifiers are quoted because EF Core names the columns after the properties,
/// which PostgreSQL would otherwise fold to lower case and fail to find.
/// </para>
/// </remarks>
internal static class WorkLeaseStatements
{
    /// <summary>Composes the statement that takes a free or expired scope and stamps it with a hold.</summary>
    /// <param name="scope">The unit of work to hold.</param>
    /// <param name="holder">The hold the lease is stamped with.</param>
    /// <param name="replica">The replica the hold belongs to, stamped beside it so the row says which process holds the scope.</param>
    /// <param name="leaseDuration">How long the scope is held for from the instant PostgreSQL takes it.</param>
    /// <returns>The statement, whose one row is the expiry of the lease it took, and which returns none when the claim was refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" />, <paramref name="holder" />, or <paramref name="replica" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// One statement covers both ways a scope can be free, which is what keeps them one decision: an insert takes a
    /// scope no row names, and the conflict path takes one whose recorded lease has run out. The predicate on the
    /// conflict path reads the *existing* row's expiry, so a live lease leaves the statement affecting nothing and the
    /// row exactly as its holder left it — including where the asking hold is the one already holding it, which is a
    /// renewal written as a claim and is refused as such.
    /// </remarks>
    internal static FormattableString ComposeClaim(
        WorkScope scope,
        WorkLeaseHolder holder,
        ReplicaIdentity replica,
        TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(replica);

        var scopeValue = scope.Value;
        var holderValue = holder.Value;
        var replicaValue = replica.Value;

        return $"""
                INSERT INTO work_leases ("Scope", "Holder", "Replica", "HeldSince", "ExpiresAt")
                VALUES ({scopeValue}, {holderValue}, {replicaValue}, now(), now() + {leaseDuration})
                ON CONFLICT ("Scope") DO UPDATE
                SET "Holder" = EXCLUDED."Holder",
                    "Replica" = EXCLUDED."Replica",
                    "HeldSince" = EXCLUDED."HeldSince",
                    "ExpiresAt" = EXCLUDED."ExpiresAt"
                WHERE work_leases."ExpiresAt" <= now()
                RETURNING work_leases."ExpiresAt" AS "Value"
                """;
    }

    /// <summary>Composes the statement that pushes a held lease further out.</summary>
    /// <param name="scope">The scope whose lease is renewed.</param>
    /// <param name="holder">The hold claiming to hold it.</param>
    /// <param name="leaseDuration">How much longer the scope is held from the instant PostgreSQL renews it.</param>
    /// <returns>The statement, whose one row is the renewed expiry, and which returns none when the hold no longer holds the scope.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> or <paramref name="holder" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The predicate is the holder and not the expiry, so a hold whose lease has run out but which nothing has taken
    /// renews it and goes on. Adding the expiry would abandon work nobody else had claimed, and it would not make the
    /// exclusion any stronger: what makes a second holder impossible is the claim, and this row still names the first.
    /// </remarks>
    internal static FormattableString ComposeRenewal(
        WorkScope scope,
        WorkLeaseHolder holder,
        TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(holder);

        var scopeValue = scope.Value;
        var holderValue = holder.Value;

        return $"""
                UPDATE work_leases
                SET "ExpiresAt" = now() + {leaseDuration}
                WHERE "Scope" = {scopeValue}
                  AND "Holder" = {holderValue}
                RETURNING "ExpiresAt" AS "Value"
                """;
    }

    /// <summary>Composes the statement that gives a held scope back.</summary>
    /// <param name="scope">The scope to release.</param>
    /// <param name="holder">The hold claiming to hold it.</param>
    /// <returns>The statement, which affects one row when the hold still held the scope and none when it did not.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> or <paramref name="holder" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A delete rather than a cleared holder, so the table stays the set of scopes something is holding and a released
    /// scope is taken by the next claim's insert. The holder predicate is what makes a late release harmless: a hold
    /// whose lease was already taken over finds the row naming somebody else and removes nothing.
    /// </remarks>
    internal static FormattableString ComposeRelease(WorkScope scope, WorkLeaseHolder holder)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(holder);

        var scopeValue = scope.Value;
        var holderValue = holder.Value;

        return $"""
                DELETE FROM work_leases
                WHERE "Scope" = {scopeValue}
                  AND "Holder" = {holderValue}
                """;
    }

    /// <summary>Composes the statement that reads which of a set of scopes is held right now.</summary>
    /// <param name="scopes">The scopes to ask about, as the text each is held under.</param>
    /// <returns>The statement, whose rows are the unexpired leases among those scopes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scopes" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The expiry comparison is the claim's own inverted, so what this reports as held is exactly what a claim would be
    /// refused for. One statement over the whole set rather than one per scope, because the surface asking is
    /// describing every account a deployment serves, and the equality against the primary key is answered by it whether
    /// the set holds one scope or thirty.
    /// </para>
    /// <para>
    /// Every mapped column is selected because the rows come back as the mapped type, which is how one statement can
    /// carry four values without a second shape declared for it. <c>HeldSince</c> is therefore read and not reported:
    /// when a hold was taken says nothing an operator can act on that the expiry ahead of it does not already say.
    /// </para>
    /// </remarks>
    internal static FormattableString ComposeHeldRead(IReadOnlyCollection<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var scopeValues = scopes.ToArray();

        return $"""
                SELECT "Scope", "Holder", "Replica", "HeldSince", "ExpiresAt"
                FROM work_leases
                WHERE "Scope" = ANY({scopeValues})
                  AND "ExpiresAt" > now()
                """;
    }
}
