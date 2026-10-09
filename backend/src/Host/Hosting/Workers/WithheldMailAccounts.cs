// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Primitives;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Holds the mail accounts this replica stops supervising while an erasure decides whether to delete them.</summary>
/// <remarks>
/// <para>
/// An erasure has to stop this replica writing to the accounts it is about to delete <em>before</em> it deletes anything,
/// and the accounts it supervises are read from the database, where nothing has changed yet. So the stop is stated here
/// instead: the coordinator leaves a withheld account out of every pass and gives back the supervision it holds, and a
/// run reading its settings finds the account gone. The erasure then takes the supervision lease itself, which is what
/// excludes every other replica.
/// </para>
/// <para>
/// A withholding is released whether the erasure committed or was refused. A refused erasure is a person nothing erased,
/// whose accounts go back to being supervised on the next pass; a committed one deleted the accounts, which no pass reads
/// again. Withholdings count rather than flag, so two erasures naming one account release it only once both have ended.
/// </para>
/// <para>
/// It is this replica's alone, and a bound rather than a guarantee: the exclusion an erasure relies on is the lease it
/// takes, which every replica honours, and this only makes the replica that received the request let go of the account
/// at once rather than at the end of its next run.
/// </para>
/// </remarks>
internal sealed class WithheldMailAccounts
{
    private readonly Lock mutex = new();
    private readonly Dictionary<MailAccountId, int> withholdings = [];
    private ConfigurationReloadToken changeToken = new();

    /// <summary>Gets whether an erasure is deciding about this account.</summary>
    /// <param name="account">The account asked about.</param>
    /// <returns><see langword="true" /> while a withholding naming it has not been released.</returns>
    internal bool IsWithheld(MailAccountId account)
    {
        lock (this.mutex)
        {
            return this.withholdings.ContainsKey(account);
        }
    }

    /// <summary>Gets a token that changes once an account is withheld or released.</summary>
    internal IChangeToken GetChangeToken() => Volatile.Read(ref this.changeToken);

    /// <summary>Stops this replica supervising the accounts until the returned withholding is disposed.</summary>
    /// <param name="accounts">The accounts an erasure is about to delete.</param>
    /// <returns>The withholding, which releases the accounts when it is disposed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    internal IDisposable Withhold(IReadOnlyCollection<MailAccountId> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var distinct = accounts.Distinct().ToArray();

        lock (this.mutex)
        {
            foreach (var account in distinct)
            {
                this.withholdings[account] = this.withholdings.GetValueOrDefault(account) + 1;
            }
        }

        this.SignalChange();

        return new Withholding(this, distinct);
    }

    private void Release(MailAccountId[] accounts)
    {
        lock (this.mutex)
        {
            foreach (var account in accounts)
            {
                if (this.withholdings[account] == 1)
                {
                    this.withholdings.Remove(account);
                }
                else
                {
                    this.withholdings[account]--;
                }
            }
        }

        this.SignalChange();
    }

    private void SignalChange() => Interlocked.Exchange(ref this.changeToken, new ConfigurationReloadToken()).OnReload();

    /// <summary>One erasure's hold on the accounts it is deciding about.</summary>
    private sealed class Withholding(WithheldMailAccounts registry, MailAccountId[] accounts) : IDisposable
    {
        private int released;

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.released, 1) == 0)
            {
                registry.Release(accounts);
            }
        }
    }
}
