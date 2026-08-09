// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;

namespace MailFathom.TestSupport;

/// <summary>Builds the served account a test arranges when the account's own settings are not what it is about.</summary>
/// <remarks>
/// Most tests care which accounts a deployment serves and not what they are called, so a derived display name keeps them
/// from restating one each. It is deliberately not the identifier: a helper that made the two spellings equal would let
/// a test pass while resolution matched only the identifier, which is the behaviour several of these tests exist to
/// prove.
/// </remarks>
internal static class SyntheticServedAccount
{
    /// <summary>Builds one served account from its identifier.</summary>
    /// <param name="accountId">The account to serve.</param>
    /// <returns>The account, polling, under a display name derived from the identifier.</returns>
    public static ServedMailAccount Of(MailAccountId accountId) =>
        new(accountId, DisplayNameOf(accountId), MailSynchronizationMode.Polling);

    /// <summary>Builds one served account from the text of its identifier.</summary>
    /// <param name="accountId">The account to serve.</param>
    /// <returns>The account, polling, under a display name derived from the identifier.</returns>
    public static ServedMailAccount Of(string accountId) => Of(MailAccountId.Create(accountId));

    /// <summary>Reads the display name this helper gives one account, which a test asserting on the name names.</summary>
    /// <param name="accountId">The account to name.</param>
    /// <returns>The display name the account is served under.</returns>
    public static MailAccountDisplayName DisplayNameOf(MailAccountId accountId) =>
        MailAccountDisplayName.Create($"The {accountId.Value} mailbox");
}
