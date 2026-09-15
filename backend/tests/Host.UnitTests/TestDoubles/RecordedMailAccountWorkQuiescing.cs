// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Host.Hosting.Workers;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Records which accounts an erasure asked to have stopped, and refuses on demand.</summary>
/// <remarks>
/// Written rather than substituted because what a test asserts about quiescing is a relation between two calls: the
/// accounts named here are the ones the erasure was about to dispose of, and the work only runs when nothing refused.
/// A substitute would have to be taught to invoke the callback before either could be observed at all.
/// </remarks>
internal sealed class RecordedMailAccountWorkQuiescing : IMailAccountWorkQuiescing
{
    /// <summary>Gets the accounts the last erasure asked to have stopped.</summary>
    internal IReadOnlyList<MailAccountId> Quiesced { get; private set; } = [];

    /// <summary>Gets whether the work ran, which is what a refusal is asserted not to have let happen.</summary>
    internal bool WorkRan { get; private set; }

    /// <summary>Gets or sets the sentence every call refuses with, or nothing to let the work run.</summary>
    internal string? Refusal { get; set; }

    /// <inheritdoc />
    public async Task<string?> RunQuiescedAsync(
        IReadOnlyList<MailAccountId> accounts,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        this.Quiesced = accounts;

        if (this.Refusal is { } refusal)
        {
            return refusal;
        }

        await work(cancellationToken);
        this.WorkRan = true;

        return null;
    }
}
