// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.TestSupport;

/// <summary>Answers which mailbox a message is in, from what a test arranged rather than from a database.</summary>
/// <remarks>
/// Hand-written rather than substituted, because a test that bounds two mailboxes against each other has to be able to
/// say which message belongs to which and read that back: a substitute would need one arrangement per identifier, and
/// the arrangement would then be the thing under test.
/// </remarks>
internal sealed class StubMailOwnership(MailAccountId defaultAccount) : IMailOwnership
{
    private readonly Dictionary<Guid, MailAccountId> accountsByStoredEmail = [];

    /// <summary>Initializes ownership answering for the deployment's own account unless a test says otherwise.</summary>
    public StubMailOwnership()
        : this(SyntheticMailAccount.Deployment)
    {
    }

    /// <summary>Says that one message belongs to a mailbox other than the default one.</summary>
    /// <param name="storedEmailId">The message.</param>
    /// <param name="account">Whose mailbox it is.</param>
    /// <returns>This stub, so arrangements read as one expression.</returns>
    public StubMailOwnership Owns(StoredEmailId storedEmailId, MailAccountId account)
    {
        this.accountsByStoredEmail[storedEmailId.Value] = account;

        return this;
    }

    /// <inheritdoc />
    public Task<MailAccountId> ReadStoredEmailAccountAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(this.accountsByStoredEmail.GetValueOrDefault(storedEmailId.Value, defaultAccount));
    }
}
