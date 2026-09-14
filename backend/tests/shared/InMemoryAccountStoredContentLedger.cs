// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Domain.Accounts;

namespace MailFathom.TestSupport;

/// <summary>Keeps what each account's stored content holds in memory, so a ceiling has a figure to read.</summary>
/// <remarks>
/// Hand-written rather than substituted, because a ceiling test arranges a figure and then asserts against what the
/// ceiling did with it: a substitute would answer from a script and the assertion would be about the script. It refuses
/// an account naming nothing exactly as the persisted ledger does, so a caller that would be refused in a deployment is
/// refused here rather than quietly answered from an entry keyed by an empty identifier.
/// </remarks>
internal sealed class InMemoryAccountStoredContentLedger : IAccountStoredContentLedger
{
    private readonly Dictionary<MailAccountId, long> storedBytesByAccount = [];

    /// <summary>Gets how many times a figure was read, which is what says a ceiling asked once per run.</summary>
    public int ReadCount { get; private set; }

    /// <summary>Gets how many times a figure was re-derived rather than read.</summary>
    public int RederiveCount { get; private set; }

    /// <summary>States what one account's stored content holds before the test begins.</summary>
    /// <param name="account">The account.</param>
    /// <param name="storedBytes">What its payloads hold.</param>
    /// <returns>This ledger, so arrangements read as one expression.</returns>
    public InMemoryAccountStoredContentLedger Holding(MailAccountId account, long storedBytes)
    {
        this.storedBytesByAccount[account] = storedBytes;

        return this;
    }

    /// <inheritdoc />
    public Task<long> ReadStoredContentBytesAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        RequireNamedAccount(account);
        cancellationToken.ThrowIfCancellationRequested();
        this.ReadCount++;

        return Task.FromResult(this.storedBytesByAccount.GetValueOrDefault(account));
    }

    /// <inheritdoc />
    public Task<long> RederiveStoredContentBytesAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        RequireNamedAccount(account);
        cancellationToken.ThrowIfCancellationRequested();
        this.RederiveCount++;

        return Task.FromResult(this.storedBytesByAccount.GetValueOrDefault(account));
    }

    private static void RequireNamedAccount(MailAccountId account)
    {
        if (string.IsNullOrWhiteSpace(account.Value))
        {
            throw new ArgumentException(
                "A stored-content counter is kept for a named account, so an account naming nothing has none to read or re-derive.",
                nameof(account));
        }
    }
}
