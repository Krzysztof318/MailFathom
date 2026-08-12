// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.Mailboxes;

/// <summary>Decides which accounts and folders a mailbox read actually runs against.</summary>
/// <remarks>
/// Every read model asks the same two questions of a requested scope, and both answers are access decisions rather than
/// query details, which is why they are resolved once here instead of per use case. A read model that answered either
/// one differently would publish mail through the query that got it wrong while the others stayed correct.
/// </remarks>
public sealed class MailboxScopeResolver
{
    private readonly IMailAccountCatalog accountCatalog;
    private readonly IMailFolderParticipationReader folderParticipation;
    private readonly IJunkMailFolderCatalog junkFolders;
    private readonly MailFolderReferenceResolver folderReferences;

    /// <summary>Initializes the resolver.</summary>
    /// <param name="accountCatalog">Answers which accounts this deployment serves.</param>
    /// <param name="folderParticipation">Answers which folders no tool may read from.</param>
    /// <param name="junkFolders">Answers which folder each account advertises as its junk folder.</param>
    /// <param name="folderReferences">Turns the alias or the role a request named into the folder of an account it means.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailboxScopeResolver(
        IMailAccountCatalog accountCatalog,
        IMailFolderParticipationReader folderParticipation,
        IJunkMailFolderCatalog junkFolders,
        MailFolderReferenceResolver folderReferences)
    {
        ArgumentNullException.ThrowIfNull(accountCatalog);
        ArgumentNullException.ThrowIfNull(folderParticipation);
        ArgumentNullException.ThrowIfNull(junkFolders);
        ArgumentNullException.ThrowIfNull(folderReferences);

        this.accountCatalog = accountCatalog;
        this.folderParticipation = folderParticipation;
        this.junkFolders = junkFolders;
        this.folderReferences = folderReferences;
    }

    /// <summary>Resolves what a request named into the scope a query runs with.</summary>
    /// <param name="accountSelectors">The text a request named accounts with, or empty for every served account.</param>
    /// <param name="folders">The folders a request named, each by alias or by role, or empty for every folder.</param>
    /// <param name="junkMail">Whether the caller asked for the account's junk folder, which defaults to it being left out.</param>
    /// <returns>The scope a query runs with.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when either list names more values than its limit permits.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account this deployment does not serve.</exception>
    /// <exception cref="MailFolderRoleUnmappedException">Thrown when the request names a role no account in scope maps a folder with.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="junkMail" /> is not a defined member.</exception>
    /// <remarks>
    /// <para>
    /// An account may be named by its configured identifier or by the display name it is published under, and this is
    /// where the two become one identity. Resolution happens against the served accounts rather than at a protocol
    /// boundary, so text naming nothing is refused by the same rule and with the same failure as an identifier the
    /// deployment stopped serving — a caller cannot learn from the refusal which spelling it was holding.
    /// </para>
    /// <para>
    /// An account the deployment does not serve is refused before anything is read, rather than narrowed away by a
    /// predicate: a narrowed predicate would answer with an empty result, and an empty result tells a caller that the
    /// name they used exists. The resolved identities are what the scope is built from, so the same account named two
    /// ways is one query with one continuation cursor.
    /// </para>
    /// <para>
    /// A request that names no account is restricted to the served accounts rather than left unrestricted. Removing an
    /// account from configuration leaves its stored rows in place, so an absent account predicate would keep publishing
    /// mail from an account this deployment no longer serves — which is also why the resolved accounts, not the
    /// requested ones, take part in a continuation cursor's fingerprint.
    /// </para>
    /// <para>
    /// A folder an operator withheld from tools is withheld here, once, rather than by each read model — which is what
    /// makes "no tool lists, searches, reads, or answers from it" a property of the system instead of a list of places
    /// somebody has to keep complete. A tool that read the mailbox some other way would bypass it, which is why the two
    /// reads that reach an email by its identifier ask the same configuration directly rather than building a scope.
    /// </para>
    /// <para>
    /// The junk folder is withheld here too, and it is a different kind of decision from the one above: an operator did
    /// not withhold it, the default did, and a caller may ask for it back. Resolving both in one place is what keeps the
    /// override from being able to reveal a folder the operator withheld — a folder that is hidden *and* junk stays out
    /// under either answer, because the two lists narrow the query independently.
    /// </para>
    /// <para>
    /// A folder named by the role it plays is turned into the folder of each account it means here, after the accounts
    /// are settled and before anything is read, because a role means a different folder on each account in scope. One
    /// role therefore narrows a multi-account read to each account's own folder rather than to whichever alias one
    /// deployment happened to choose. A role no account in scope maps is refused instead of narrowed away, for the
    /// reason an unserved account is: the caller asked for a folder that is not there, and an empty page would read as
    /// a folder holding no mail. Naming <c>role:Junk</c> is narrowing rather than asking — the withholding above still
    /// applies, so a read that names it and does not also ask for junk mail returns nothing.
    /// </para>
    /// </remarks>
    public MailboxScope ReadableScope(
        IReadOnlyList<MailAccountSelector> accountSelectors,
        IReadOnlyList<MailFolderReference> folders,
        JunkMailInclusion junkMail)
    {
        ArgumentNullException.ThrowIfNull(accountSelectors);
        ArgumentNullException.ThrowIfNull(folders);

        if (!Enum.IsDefined(junkMail))
        {
            throw new ArgumentOutOfRangeException(
                nameof(junkMail),
                junkMail,
                "A read either reaches into the junk folder or leaves it out, and no other value names an answer.");
        }

        var servedAccounts = this.accountCatalog.ServedAccounts;

        // Counted before anything is resolved, because the count is the caller's and each resolution walks the served
        // accounts or that account's folders. The scope's own limits are reused rather than second ones invented for
        // the text the identities arrive as.
        MailboxQueryFilterInvalidException.ThrowIfCountExceeded(
            accountSelectors.Count,
            MailboxScope.MaximumAccountIds,
            "accounts");
        MailboxQueryFilterInvalidException.ThrowIfCountExceeded(
            folders.Count,
            MailboxScope.MaximumFolders,
            "folders");

        var requestedAccountIds = accountSelectors
            .Select(selector => ResolvedAccountId(selector, servedAccounts))
            .ToArray();

        var accountsInScope = requestedAccountIds.Length is 0
            ? [.. servedAccounts.Select(static account => account.Id)]
            : requestedAccountIds;

        var resolvedScope = MailboxScope.Create(
            accountsInScope,
            this.ResolvedFolders(accountsInScope, folders));

        return resolvedScope
            .Hiding(this.folderParticipation.FoldersHiddenFromTools)
            .WithJunkMail(junkMail, this.junkFolders.JunkFolders);
    }

    /// <summary>Reports whether a tool may read one email, given the mailbox it was stored from.</summary>
    /// <param name="accountId">The account the email was read from.</param>
    /// <param name="folderAlias">The folder the email was read from.</param>
    /// <returns><see langword="true" /> when the deployment serves that account and no configuration withholds that folder.</returns>
    /// <remarks>
    /// This is <see cref="ReadableScope" /> asked about one email instead of about a query, and it exists because two
    /// reads reach an email by its identifier and build no scope at all. Both questions are answered from the same two
    /// pieces of configuration here, so a folder an operator withheld cannot be readable through one entry point and
    /// withheld through another. A caller that may not read the email is told it was not found rather than refused, for
    /// the reason an account this deployment no longer serves is: a refusal would confirm the identifier exists.
    /// </remarks>
    public bool IsReadableByTools(MailAccountId accountId, MailFolderAlias folderAlias) =>
        this.accountCatalog.ServedAccounts.Any(account => account.Id == accountId)
        && this.folderParticipation.GetParticipation(accountId, folderAlias).IsVisibleToTools;

    /// <summary>Turns what a request named folders with into the account-and-folder pairs a query is expressed in.</summary>
    /// <exception cref="MailFolderRoleUnmappedException">Thrown when a role reaches no folder on any account in scope.</exception>
    /// <remarks>
    /// A role is asked of every account in scope and answers with each of their folders, because one query reads
    /// several mailboxes and the folder playing a role is each mailbox's own. It is refused only when no account in
    /// scope maps it at all: refusing because one of several accounts lacks a junk folder would make a general question
    /// unanswerable for the deployments that have the folder.
    /// </remarks>
    private IReadOnlyList<MailFolderIdentity> ResolvedFolders(
        IReadOnlyList<MailAccountId> accountsInScope,
        IReadOnlyList<MailFolderReference> folders) =>
        [.. folders.SelectMany(folder => this.FoldersNamedBy(accountsInScope, folder))];

    /// <summary>Answers which folder of which account one reference names, as a pair per account it reaches.</summary>
    /// <remarks>
    /// An alias is the caller's own name and means the same folder on every account in scope, so it is paired with each
    /// of them. A role is answered per account and paired with the account that answered, which is what keeps one
    /// account's junk folder from admitting another account's folder of the same name.
    /// </remarks>
    private IReadOnlyList<MailFolderIdentity> FoldersNamedBy(
        IReadOnlyList<MailAccountId> accountsInScope,
        MailFolderReference folder)
    {
        if (folder.Alias is { } alias)
        {
            return [.. accountsInScope.Select(accountId => new MailFolderIdentity(accountId, alias))];
        }

        IReadOnlyList<MailFolderIdentity> named =
        [
            .. accountsInScope
                .Select(accountId => (AccountId: accountId, Mapping: this.folderReferences.TryResolve(accountId, folder)))
                .Where(static resolution => resolution.Mapping is not null)
                .Select(static resolution => new MailFolderIdentity(resolution.AccountId, resolution.Mapping!.Alias)),
        ];

        return named.Count > 0
            ? named
            : throw new MailFolderRoleUnmappedException(folder.Role!.Value);
    }

    /// <summary>Finds the served account text names, refusing the request when it names none.</summary>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when no served account carries that identifier or display name.</exception>
    private static MailAccountId ResolvedAccountId(
        MailAccountSelector selector,
        IReadOnlyList<ServedMailAccount> servedAccounts) =>
        servedAccounts.FirstOrDefault(account => account.IsNamedBy(selector))?.Id
        ?? throw new MailAccountNotAccessibleException(selector);
}
