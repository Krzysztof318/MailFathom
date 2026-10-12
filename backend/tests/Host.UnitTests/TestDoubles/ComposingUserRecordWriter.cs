// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>A record writer that composes each commit under the policies a test states, and reports what was composed.</summary>
/// <param name="commits">What every composed commit is reported to, and what answers whether it landed.</param>
/// <remarks>
/// The policies a commit is composed under are the ones in force inside its transaction, which need not be the ones
/// the record was read with. They are stated here apart from the reader for that reason: a test that moves a policy
/// between a read and a commit states the later one here and the earlier one on the record it arranges.
/// </remarks>
internal sealed class ComposingUserRecordWriter(IRecordedUserRecordCommits commits) : IUserSettingsDocumentWriter
{
    private readonly Dictionary<UserId, GoverningSettingsPolicies> inForce = [];

    /// <summary>Gets or sets the policies a commit is composed under for a user no test named, which is how a user minted inside the act is governed.</summary>
    internal GoverningSettingsPolicies Otherwise { get; set; } = GoverningSettingsPolicies.Unwritten;

    /// <summary>States the policies a commit for one user is composed under.</summary>
    /// <param name="user">The user.</param>
    /// <param name="policies">The policies in force when their record commits.</param>
    internal void GovernedBy(UserId user, GoverningSettingsPolicies policies) => this.inForce[user] = policies;

    /// <inheritdoc />
    public Task<long?> CommitAsync(
        UserId user,
        long expectedVersion,
        Func<GoverningSettingsPolicies, UserRecordCandidate?> compose,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compose);

        return compose(this.inForce.GetValueOrDefault(user, this.Otherwise)) is { } candidate
            ? commits.CommitAsync(user, candidate.Json, candidate.EndpointAccess, expectedVersion, cancellationToken)
            : Task.FromResult<long?>(null);
    }
}
