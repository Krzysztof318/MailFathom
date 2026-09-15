// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Runs work while nothing in the deployment is writing to a set of mail accounts, or refuses to run it at all.</summary>
/// <remarks>
/// <para>
/// The seam an erasure reaches the running deployment through. A deletion narrowed on a mail account is outside every
/// lock the row it was asked about could take — a synchronization run and a job handler write rows keyed to the account
/// rather than to the person — so what makes an erasure true afterwards is stopping those writers first, for as long as
/// the deletion takes.
/// </para>
/// <para>
/// The work is handed in rather than the hold handed out, for the reason <see cref="Application.Coordination.IWorkLeaseRunner" />
/// takes the same shape: holding an account is a lease that has to be renewed while the work runs and given back once
/// it ends, and a caller that was handed one could forget either. It also makes the refusal the only other answer,
/// which is what the caller has to act on.
/// </para>
/// </remarks>
internal interface IMailAccountWorkQuiescing
{
    /// <summary>Stops the work bound to a set of accounts, runs the work under that, and starts them again.</summary>
    /// <param name="accounts">The accounts to quiesce, which are the ones the work is about to dispose of.</param>
    /// <param name="work">The work to run while nothing is writing to them.</param>
    /// <param name="cancellationToken">Cancels the wait and the work, which gives back whatever was held.</param>
    /// <returns>The sentence naming what is still running, or <see langword="null" /> when the work ran to its end.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> or <paramref name="work" /> is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled, which is the caller's own withdrawal and nothing this seam decided.</exception>
    /// <remarks>
    /// <para>
    /// A refusal means the work either never started or was rolled back, which is what makes it safe for the caller to
    /// report that nothing was done: half an erasure is worse than none, so the bound running out is an answer rather
    /// than a delay.
    /// </para>
    /// <para>
    /// There are three ways the work does not happen and only one of them is an exception. The bound running out
    /// refuses before the work starts; a hold lost while the work runs cancels it, which rolls the work back and is
    /// refused here with the mailbox named, because to the caller that outcome is a mailbox that would not stay still
    /// rather than a fault of the machinery; and the caller cancelling is the caller's own act and is the one that
    /// leaves as <see cref="OperationCanceledException" />.
    /// </para>
    /// </remarks>
    Task<string?> RunQuiescedAsync(
        IReadOnlyList<MailAccountId> accounts,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken);
}
