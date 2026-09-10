// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using MailFathom.Application.Access.Credentials;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>The deployment's record of served assertions, held in this process so a test can reach it.</summary>
/// <remarks>
/// It models the two properties the real table has and nothing else: the pair is unique, so a second insert of one pair
/// writes nothing and says so, and a removal drops exactly what has expired. Whether PostgreSQL settles a concurrent
/// pair the way this dictionary does is not a claim a unit test can make — the orchestrated suite is where the composed
/// statement is proven — so what these doubles buy is the rules the store above the port applies, which is where the
/// sweep interval and the credential scoping live.
/// </remarks>
internal sealed class InMemoryClientAssertionSpendStore : IClientAssertionSpendStore
{
    private readonly ConcurrentDictionary<(string CredentialKey, string Identifier), DateTimeOffset> spent = new();

    /// <summary>Gets how many removals the store above this one has asked for.</summary>
    /// <remarks>The sweep is throttled rather than issued per request, and nothing else observes that: the records it drops are ones the caller was going to be told about anyway.</remarks>
    internal int RemovalCount { get; private set; }

    /// <inheritdoc />
    public Task<bool> TrySpendAsync(
        string credentialKey,
        string identifier,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.spent.TryAdd((credentialKey, identifier), expiresAt));

    /// <inheritdoc />
    public Task RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        this.RemovalCount++;

        foreach (var record in this.spent.Where(record => record.Value <= now))
        {
            this.spent.TryRemove(record);
        }

        return Task.CompletedTask;
    }
}
