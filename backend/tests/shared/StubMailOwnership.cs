// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.TestSupport;

/// <summary>Answers whose mail an account or a message is, from what a test arranged rather than from a database.</summary>
/// <remarks>
/// Hand-written rather than substituted, because a test that bounds two owners against each other has to be able to say
/// which message belongs to which and read that back: a substitute would need one arrangement per identifier, and the
/// arrangement would then be the thing under test.
/// </remarks>
internal sealed class StubMailOwnership(MailOwnerId defaultOwner) : IMailOwnership
{
    private readonly Dictionary<Guid, MailOwnerId> ownersByStoredEmail = [];
    private readonly Dictionary<string, MailOwnerId> ownersByAccount = [];

    /// <summary>Initializes ownership answering for the deployment's own owner unless a test says otherwise.</summary>
    public StubMailOwnership()
        : this(SyntheticMailOwner.Deployment)
    {
    }

    /// <summary>Says that one message belongs to somebody other than the default owner.</summary>
    /// <param name="storedEmailId">The message.</param>
    /// <param name="owner">Whose it is.</param>
    /// <returns>This stub, so arrangements read as one expression.</returns>
    public StubMailOwnership Owns(StoredEmailId storedEmailId, MailOwnerId owner)
    {
        this.ownersByStoredEmail[storedEmailId.Value] = owner;

        return this;
    }

    /// <summary>Says that one mail account belongs to somebody other than the default owner.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="owner">Whose it is.</param>
    /// <returns>This stub, so arrangements read as one expression.</returns>
    public StubMailOwnership Owns(MailAccountId accountId, MailOwnerId owner)
    {
        this.ownersByAccount[accountId.Value] = owner;

        return this;
    }

    /// <inheritdoc />
    public Task<MailOwnerId> ReadAccountOwnerAsync(MailAccountId accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(this.ownersByAccount.GetValueOrDefault(accountId.Value, defaultOwner));
    }

    /// <inheritdoc />
    public Task<MailOwnerId> ReadStoredEmailOwnerAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(this.ownersByStoredEmail.GetValueOrDefault(storedEmailId.Value, defaultOwner));
    }
}
