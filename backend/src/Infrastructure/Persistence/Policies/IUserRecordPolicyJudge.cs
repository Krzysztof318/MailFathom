// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>Judges a user's record against the policies a write would leave it governed by.</summary>
/// <remarks>
/// <para>
/// Two writes change which policies govern a record without writing the record: a write of a policy, and a move of
/// the user into or out of an organization. Each has to answer two things about every record it reaches before it
/// commits — whether the record is still one this deployment can serve, and which endpoints its user is served on —
/// and both answers come from resolving the record and binding it, which is the composition root's to do because the
/// record's own type and its rules live there. So the stores that own those two transactions ask through this port,
/// inside the transaction and under the lock that keeps the policies still.
/// </para>
/// <para>
/// An implementation reads nothing and performs no I/O: everything it judges is handed to it, which is what lets it
/// be called for every governed record of a write without lengthening the transaction by a round trip each.
/// </para>
/// </remarks>
public interface IUserRecordPolicyJudge
{
    /// <summary>Judges the records one write reaches, each under the policies the write would leave it governed by.</summary>
    /// <param name="records">The records, each with the policies governing it now and the ones that would govern it.</param>
    /// <returns>One judgement per record, in the order given: the endpoint switches to project onto the user's row, and what refuses the write.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="records" /> is <see langword="null" />.</exception>
    /// <remarks>Asked once for a whole write rather than once per record, so a policy a thousand records share is read as a policy once.</remarks>
    IReadOnlyList<UserRecordPolicyJudgement> Judge(IReadOnlyList<GovernedUserRecord> records);
}
