// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.TestSupport;

/// <summary>Answers which mailbox a message is in, from what a test arranged rather than from a database.</summary>
/// <remarks>
/// Hand-written rather than substituted, because a test that bounds two users against each other has to be able to say
/// which message belongs to which and read that back: a substitute would need one arrangement per identifier, and the
/// arrangement would then be the thing under test.
/// </remarks>
internal sealed class StubMailOwnership(MailUserId defaultUser) : IMailOwnership
{
    /// <summary>The mailbox a message nothing names an account for is answered as being in.</summary>
    /// <remarks>
    /// One account per user rather than one for the whole stub, so two messages arranged to two users are two mailboxes
    /// — which is what a posture held per account is read against.
    /// </remarks>
    private static readonly MailAccountId DefaultAccount = MailAccountId.Create("stub-account");

    private readonly Dictionary<Guid, MailAccountIdentity> accountsByStoredEmail = [];

    /// <summary>Initializes ownership answering for the deployment's own user unless a test says otherwise.</summary>
    public StubMailOwnership()
        : this(SyntheticMailUser.Deployment)
    {
    }

    /// <summary>Says that one message belongs to somebody other than the default user.</summary>
    /// <param name="storedEmailId">The message.</param>
    /// <param name="user">Whose it is, in that user's own default mailbox.</param>
    /// <returns>This stub, so arrangements read as one expression.</returns>
    public StubMailOwnership Owns(StoredEmailId storedEmailId, MailUserId user) =>
        this.Owns(storedEmailId, MailAccountIdentity.Create(user, DefaultAccount));

    /// <summary>Says which mailbox one message is in, and who that mailbox is assigned to.</summary>
    /// <param name="storedEmailId">The message.</param>
    /// <param name="account">The mailbox it is in.</param>
    /// <returns>This stub, so arrangements read as one expression.</returns>
    public StubMailOwnership Owns(StoredEmailId storedEmailId, MailAccountIdentity account)
    {
        this.accountsByStoredEmail[storedEmailId.Value] = account;

        return this;
    }

    /// <inheritdoc />
    public Task<MailAccountIdentity> ReadStoredEmailAccountAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(this.accountsByStoredEmail.GetValueOrDefault(
            storedEmailId.Value,
            MailAccountIdentity.Create(defaultUser, DefaultAccount)));
    }
}
