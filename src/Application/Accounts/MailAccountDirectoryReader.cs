// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Observability;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts;

/// <summary>Reads which accounts this deployment serves and how current the local copy of each one is.</summary>
/// <remarks>
/// <para>
/// It is the one use case that publishes the account set rather than using it as a bound. Every other reader asks the
/// catalog what it may read and answers only about the account the caller already named; this exists because a caller
/// that cannot see the accounts cannot name one, and a filter it has no way to fill in is a filter nobody uses.
/// </para>
/// <para>
/// What it publishes is MailFathom's own configured names and the progress of synchronization against them. The mail
/// server, the port, the user name, and every credential stay out of it: the display name is what makes a mailbox
/// recognizable, and how MailFathom reaches it is the operator's business rather than the caller's.
/// </para>
/// <para>
/// It reaches no mail server. The freshness it reports is what synchronization durably committed, so the answer is the
/// same whether or not a mail server is reachable at the moment it is asked.
/// </para>
/// </remarks>
public sealed class MailAccountDirectoryReader
{
    private readonly IMailAccountCatalog accountCatalog;
    private readonly ISynchronizationFreshnessReader freshnessReader;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IMailboxReadTelemetry readTelemetry;

    /// <summary>Initializes the use case.</summary>
    /// <param name="accountCatalog">Describes the accounts this deployment serves.</param>
    /// <param name="freshnessReader">Reads how current the local copy of each folder is.</param>
    /// <param name="scopeResolver">Answers which folders of those accounts a tool may see.</param>
    /// <param name="readTelemetry">Publishes the read as the operation it is, beside the call it happened inside.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailAccountDirectoryReader(
        IMailAccountCatalog accountCatalog,
        ISynchronizationFreshnessReader freshnessReader,
        MailboxScopeResolver scopeResolver,
        IMailboxReadTelemetry readTelemetry)
    {
        ArgumentNullException.ThrowIfNull(accountCatalog);
        ArgumentNullException.ThrowIfNull(freshnessReader);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(readTelemetry);

        this.accountCatalog = accountCatalog;
        this.freshnessReader = freshnessReader;
        this.scopeResolver = scopeResolver;
        this.readTelemetry = readTelemetry;
    }

    /// <summary>Reads the served accounts and their synchronization freshness.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The served accounts with their folders, and whether the deployment refreshes them.</returns>
    /// <remarks>
    /// Nothing here writes, so the operation is safe to repeat, and it sets no remote state because it speaks to no mail
    /// server at all.
    /// </remarks>
    public async Task<MailAccountDirectory> ReadAsync(CancellationToken cancellationToken)
    {
        using var read = this.readTelemetry.BeginRead(
            MailboxReadOperation.ReadAccountDirectory,
            cancellationToken);

        var servedAccounts = this.accountCatalog.ServedAccounts;

        if (servedAccounts.Count is 0)
        {
            read.Completed(0);

            return new MailAccountDirectory(this.accountCatalog.SynchronizationEnabled, []);
        }

        // Resolved rather than built here, for the reason every other read is: local state holds folders of accounts an
        // operator has since removed and folders no mapping names any more, and neither may reappear in the one answer
        // that lists them. Naming a folder is publishing that it exists, which is the whole of what this answer does.
        // Junk is included because it is a mapped folder whose freshness the operator asked to see; withholding it is
        // about not returning its mail unasked, and no mail is returned here.
        var scope = this.scopeResolver.ReadableScope([], [], JunkMailInclusion.Included);

        var folderFreshness = await this.freshnessReader.ReadAsync(scope, cancellationToken);
        var foldersByAccount = folderFreshness
            .GroupBy(static freshness => freshness.AccountId)
            .ToDictionary(static group => group.Key, FoldersOrderedByAlias);

        read.Completed(servedAccounts.Count);

        return new MailAccountDirectory(
            this.accountCatalog.SynchronizationEnabled,
            [.. servedAccounts.Select(account => new DescribedMailAccount(account, FoldersOf(foldersByAccount, account.Id)))]);
    }

    private static IReadOnlyList<MailboxFolderFreshness> FoldersOrderedByAlias(IGrouping<MailAccountId, MailboxFolderFreshness> group) =>
        [.. group.OrderBy(static freshness => freshness.FolderAlias.Value, StringComparer.Ordinal)];

    /// <summary>Reads one account's folders, which are absent for an account synchronization has never reached.</summary>
    private static IReadOnlyList<MailboxFolderFreshness> FoldersOf(
        Dictionary<MailAccountId, IReadOnlyList<MailboxFolderFreshness>> foldersByAccount,
        MailAccountId accountId) =>
        foldersByAccount.TryGetValue(accountId, out var folders) ? folders : [];
}
