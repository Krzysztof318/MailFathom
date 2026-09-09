// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>Derives where one conversation stands: what was agreed, what is open, and who owes what.</summary>
/// <remarks>
/// <para>
/// The one way a conversation becomes a state. An implementation is registered for every deployment, including one that
/// has turned the derivation off — the answer a caller needs is a reason it can act on, and a missing registration
/// would have made every caller carry a null check and decide for itself what an absence meant.
/// </para>
/// <para>
/// It never throws for a provider that failed. Every way a derivation can come to nothing is a member of
/// <see cref="ThreadStateWithholding" />, because the caller's response to each of them is the same shape — leave the
/// conversation outstanding and stop — and an exception would have made an ordinary outage look like a defect in the
/// pass. Caller cancellation is the exception to that and propagates, being the caller withdrawing the work rather than
/// anything about the conversation.
/// </para>
/// </remarks>
public interface IThreadStateDeriver
{
    /// <summary>Gets whether this deployment derives a conversation's state at all.</summary>
    /// <remarks>
    /// Asked so that a pass on a deployment that has not turned the derivation on answers in one comparison and issues
    /// no query — the selection it would otherwise run is a grouped scan per account per run for work nothing will do.
    /// </remarks>
    bool IsActive { get; }

    /// <summary>Derives one conversation's state.</summary>
    /// <param name="thread">The conversation and the messages the derivation reads.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The statements that were settled, or the reason nothing was derived this time.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="thread" /> is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    Task<ThreadStateDerivation> DeriveAsync(DerivableThread thread, CancellationToken cancellationToken);
}
