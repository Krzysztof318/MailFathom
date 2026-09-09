// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.ThreadStates;

namespace MailFathom.AI.ThreadStates;

/// <summary>Derives nothing, on a deployment that has not turned a conversation's state on.</summary>
/// <remarks>
/// <para>
/// The default state of an instance, and what every deployment gets until an operator says otherwise. It exists as a
/// registration rather than as an absent one because the answer the pass needs is a reason it can act on, and a missing
/// service would have made the pass carry a null check and decide for itself what an absence meant.
/// </para>
/// <para>
/// Nothing is read and nothing is sent. The conversation is not touched at all, so an instance in this state cannot
/// disclose an exchange however often it is asked, which is the property the switch exists to give and would not have
/// if the refusal came after the turn had been composed.
/// </para>
/// </remarks>
internal sealed class InactiveThreadStateDeriver : IThreadStateDeriver
{
    /// <summary>The one instance, since it holds nothing and answers everything the same way.</summary>
    internal static readonly InactiveThreadStateDeriver Instance = new();

    private InactiveThreadStateDeriver()
    {
    }

    /// <inheritdoc />
    public bool IsActive => false;

    /// <inheritdoc />
    public Task<ThreadStateDerivation> DeriveAsync(DerivableThread thread, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(thread);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ThreadStateDerivation.Withholding(ThreadStateWithholding.NotActivated));
    }
}
