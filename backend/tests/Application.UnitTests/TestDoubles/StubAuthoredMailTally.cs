// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Collection;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Answers how much of an account's mail each address wrote, from what a test wrote down.</summary>
/// <remarks>
/// The counts are keyed by the address's comparison form, so a test may state one casing and assert against another
/// exactly as the real tally does. An address a test never named has written nothing, which is the state of every
/// mailbox before somebody writes to it.
/// </remarks>
internal sealed class StubAuthoredMailTally(IReadOnlyDictionary<string, int> messagesByAddress) : IAuthoredMailTally
{
    /// <summary>Answers that nobody has written anything.</summary>
    internal static StubAuthoredMailTally NobodyHasWritten { get; } = new(new Dictionary<string, int>());

    /// <summary>Gets how many times the tally was asked.</summary>
    internal int QueryCount { get; private set; }

    /// <summary>Answers that one address has written the given number of messages, and nobody else has written any.</summary>
    /// <param name="address">The address that wrote.</param>
    /// <param name="messageCount">How many messages it wrote.</param>
    /// <returns>The tally.</returns>
    internal static StubAuthoredMailTally Of(string address, int messageCount) =>
        new(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [address.ToUpperInvariant()] = messageCount,
        });

    /// <inheritdoc />
    public Task<int> CountMessagesAuthoredByAsync(
        MailAccountIdentity account,
        EmailAddress author,
        int ceiling,
        CancellationToken cancellationToken)
    {
        this.QueryCount++;

        return Task.FromResult(Math.Min(
            messagesByAddress.GetValueOrDefault(author.NormalizedAddress),
            ceiling));
    }
}
