// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Coordination;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Synchronization;

/// <summary>Names the lease one account's supervision is held under.</summary>
/// <remarks>
/// <para>
/// Here rather than beside the worker that takes the lease, because two callers now compose it and they must not
/// disagree: the coordinator asks for the scope before it starts a supervisor, and the administrative surface asks the
/// lease table which replica is holding that same scope. A second spelling would report every account as unsupervised
/// while every account was in fact being supervised.
/// </para>
/// <para>
/// The unit is the account's generated identifier and nothing beside it. The identifier names one mailbox across the
/// deployment, and one mailbox is supervised once however many users are assigned it — a scope naming a user would
/// have a shared mailbox supervised once per assignment, which is the same mailbox fetched several times over.
/// </para>
/// </remarks>
public static class MailAccountSupervisionScope
{
    /// <summary>Composes the scope every replica configured with an account asks for.</summary>
    /// <param name="account">The account supervised.</param>
    /// <returns>The scope one account's supervision is held under.</returns>
    /// <remarks>
    /// An account identifier the scope cannot carry — longer than it leaves room for, or holding a control character —
    /// is named by its SHA-256 digest instead. Configuration accepts identifiers far longer than a scope, and a scope
    /// that could not be composed would end supervision for every account on the replica rather than for that one.
    /// </remarks>
    public static WorkScope For(MailAccountId account)
    {
        var accountId = account.Value;
        var readableScope = string.Create(CultureInfo.InvariantCulture, $"mail-synchronization/{accountId}");

        if (readableScope.Length <= WorkScope.MaximumLength && !accountId.Any(char.IsControl))
        {
            return WorkScope.Create(readableScope);
        }

        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(accountId)));

        return WorkScope.Create(string.Create(CultureInfo.InvariantCulture, $"mail-synchronization/sha256-{digest}"));
    }
}
