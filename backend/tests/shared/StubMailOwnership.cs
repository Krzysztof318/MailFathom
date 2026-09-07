// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.TestSupport;

/// <summary>Answers whose mail a message is, from what a test arranged rather than from a database.</summary>
/// <remarks>
/// Hand-written rather than substituted, because a test that bounds two users against each other has to be able to say
/// which message belongs to which and read that back: a substitute would need one arrangement per identifier, and the
/// arrangement would then be the thing under test.
/// </remarks>
internal sealed class StubMailOwnership(MailUserId defaultUser) : IMailOwnership
{
    private readonly Dictionary<Guid, MailUserId> usersByStoredEmail = [];

    /// <summary>Initializes ownership answering for the deployment's own user unless a test says otherwise.</summary>
    public StubMailOwnership()
        : this(SyntheticMailUser.Deployment)
    {
    }

    /// <summary>Says that one message belongs to somebody other than the default user.</summary>
    /// <param name="storedEmailId">The message.</param>
    /// <param name="user">Whose it is.</param>
    /// <returns>This stub, so arrangements read as one expression.</returns>
    public StubMailOwnership Owns(StoredEmailId storedEmailId, MailUserId user)
    {
        this.usersByStoredEmail[storedEmailId.Value] = user;

        return this;
    }

    /// <inheritdoc />
    public Task<MailUserId> ReadStoredEmailUserAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(this.usersByStoredEmail.GetValueOrDefault(storedEmailId.Value, defaultUser));
    }
}
