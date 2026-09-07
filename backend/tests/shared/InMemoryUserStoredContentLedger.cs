// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>Keeps what each user's stored content holds in memory, so a per-user ceiling has a figure to read.</summary>
/// <remarks>
/// Hand-written rather than substituted, because a ceiling test arranges a figure and then asserts against what the
/// ceiling did with it: a substitute would answer from a script and the assertion would be about the script. It refuses
/// a user naming nobody exactly as the persisted ledger does, so a caller that would be refused in a deployment is
/// refused here rather than quietly answered from an entry keyed by an empty identifier.
/// </remarks>
internal sealed class InMemoryUserStoredContentLedger : IUserStoredContentLedger
{
    private readonly Dictionary<MailUserId, long> storedBytesByUser = [];

    /// <summary>Gets how many times a figure was read, which is what says a ceiling asked once per run.</summary>
    public int ReadCount { get; private set; }

    /// <summary>Gets how many times a figure was re-derived rather than read.</summary>
    public int RederiveCount { get; private set; }

    /// <summary>States what one user's stored content holds before the test begins.</summary>
    /// <param name="user">The user.</param>
    /// <param name="storedBytes">What their payloads hold.</param>
    /// <returns>This ledger, so arrangements read as one expression.</returns>
    public InMemoryUserStoredContentLedger Holding(MailUserId user, long storedBytes)
    {
        this.storedBytesByUser[user] = storedBytes;

        return this;
    }

    /// <inheritdoc />
    public Task<long> ReadStoredContentBytesAsync(MailUserId user, CancellationToken cancellationToken)
    {
        RequireNamedUser(user);
        cancellationToken.ThrowIfCancellationRequested();
        this.ReadCount++;

        return Task.FromResult(this.storedBytesByUser.GetValueOrDefault(user));
    }

    /// <inheritdoc />
    public Task<long> RederiveStoredContentBytesAsync(MailUserId user, CancellationToken cancellationToken)
    {
        RequireNamedUser(user);
        cancellationToken.ThrowIfCancellationRequested();
        this.RederiveCount++;

        return Task.FromResult(this.storedBytesByUser.GetValueOrDefault(user));
    }

    private static void RequireNamedUser(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException(
                "A stored-content counter is kept for a named user, so a user naming nobody has none to read or re-derive.",
                nameof(user));
        }
    }
}
