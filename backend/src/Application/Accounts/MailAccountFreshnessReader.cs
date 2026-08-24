// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Accounts;

/// <summary>Reads the caller's own accounts as one line each: which account it is, and whether its local copy is current.</summary>
/// <remarks>
/// <para>
/// It answers the same question <see cref="MailAccountDirectoryReader" /> does and answers it at a different size. That
/// use case publishes a folder per account because a caller about to narrow a query needs the aliases; a reader that is
/// only deciding whether to trust the mailbox needs one instant and one word, and an answer that grew a line per folder
/// would grow with how a mailbox is organized rather than with how many mailboxes there are.
/// </para>
/// <para>
/// Composed over that use case rather than beside it, so the two cannot come to disagree about which accounts an owner
/// has. Whose accounts these are, which folders count, the grant that is required, and the read that is recorded are all
/// decided there, once; what is added here is the account-level reading none of those sources holds on its own.
/// </para>
/// <para>
/// The state is what this process has observed. <see cref="MailSynchronizationRunLedger" /> is deliberately not durable,
/// so a process that has just started reports an account it has not run yet by what its stored progress says rather than
/// by how its runs were going before the restart — which is the honest answer, since the backoff that was failing is not
/// one this process is applying.
/// </para>
/// <para>
/// It reaches no mail server and returns no mail: an account's configured identifier, its display name, one instant, and
/// one state are the whole of what a caller receives.
/// </para>
/// </remarks>
public sealed class MailAccountFreshnessReader
{
    private readonly MailAccountDirectoryReader directoryReader;
    private readonly MailSynchronizationRunLedger runLedger;

    /// <summary>Initializes the use case.</summary>
    /// <param name="directoryReader">Reads which accounts the caller's owner owns and how current each of their folders is.</param>
    /// <param name="runLedger">Reports how this process's most recent run of each account ended.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailAccountFreshnessReader(
        MailAccountDirectoryReader directoryReader,
        MailSynchronizationRunLedger runLedger)
    {
        ArgumentNullException.ThrowIfNull(directoryReader);
        ArgumentNullException.ThrowIfNull(runLedger);

        this.directoryReader = directoryReader;
        this.runLedger = runLedger;
    }

    /// <summary>Reads the caller's accounts and how current each one is.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One entry per account the caller's owner owns, and whether the deployment refreshes them.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" /> that is acting for an owner.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// An owner who owns no account is answered with an empty list rather than a refusal, because owning nothing is a
    /// state a client renders and not a failure. A caller whose grant does not carry the permission is refused instead,
    /// which is what keeps the two answers from arriving as the same empty collection.
    /// </remarks>
    public async Task<MailAccountFreshnessDirectory> ReadAsync(CancellationToken cancellationToken)
    {
        var directory = await this.directoryReader.ReadAsync(cancellationToken);

        return new MailAccountFreshnessDirectory(
            directory.SynchronizationEnabled,
            [.. directory.Accounts.Select(this.Summarize)]);
    }

    /// <summary>Reduces one account's folders and its last run to the two facts a reader deciding whether to trust the copy needs.</summary>
    private MailAccountFreshness Summarize(DescribedMailAccount account)
    {
        var lastSynchronizedAt = account.Folders.Max(static folder => folder.SynchronizedAt);
        var lastRunFailed = this.runLedger.ReadAccount(account.Account.Id).LastRun?.Failed is true;

        return new MailAccountFreshness(account.Account, StateOf(lastRunFailed, lastSynchronizedAt), lastSynchronizedAt);
    }

    /// <summary>Names where the account stands, with a failing run outranking the absence of any progress.</summary>
    private static MailAccountSynchronizationState StateOf(bool lastRunFailed, DateTimeOffset? lastSynchronizedAt) =>
        (lastRunFailed, lastSynchronizedAt) switch
        {
            (true, _) => MailAccountSynchronizationState.Failing,
            (false, null) => MailAccountSynchronizationState.NeverSynchronized,
            _ => MailAccountSynchronizationState.Synchronized,
        };
}
