// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>Holds the owners this deployment serves, once the gate that reconciles them has established who they are.</summary>
/// <remarks>
/// <para>
/// A singleton because the roster is a property of the deployment rather than of a request, and because every admitted
/// caller and every synchronization run is composed against it: resolving it per request would put a database read in
/// front of each of them to establish a value that cannot change while the process runs.
/// </para>
/// <para>
/// Reading it before the gate has settled it fails rather than answering, because the alternative is an owner nobody
/// named and callers composed against one would read whichever mail a query matched. The window that reading belongs
/// to is a real one rather than a wiring defect alone: the gate is an ordinary hosted service and the web host's own is
/// registered while the builder runs, so the listener is already accepting connections while the gate runs. What holds
/// traffic off that window is the startup probe, which reports the deployment unstarted until every gate has completed.
/// </para>
/// <para>
/// The roster is written once from the startup path and read from every request thread afterwards, and both take the
/// same lock. The write is one assignment, but nothing about a bare field would establish that a thread which observes
/// it observes the whole of what it points at, or observes it at all. That is what the lock is for rather than
/// contention: it is uncontended for the life of the process, since the one write happens before any request the read
/// serves.
/// </para>
/// </remarks>
internal sealed class ServedMailOwners : IDeploymentMailOwnerSource
{
    private readonly Lock mutex = new();

    /// <summary>The roster the startup gate established, or nothing while it has not run.</summary>
    /// <remarks>
    /// Absence is what is being stored rather than an empty roster, which is why the field is nullable: a deployment
    /// before its gate has run serves nobody <em>yet</em>, and an empty list would read as a deployment that serves
    /// nobody at all. Every read and write of it is taken under <see cref="mutex" />.
    /// </remarks>
    private IReadOnlyList<ServedMailOwner>? resolvedOwners;

    /// <summary>Gets every owner this deployment serves, in the order the roster was established in.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the startup gate that establishes the roster has not yet run.</exception>
    public IReadOnlyList<ServedMailOwner> Owners
    {
        get
        {
            lock (this.mutex)
            {
                return this.resolvedOwners
                    ?? throw new InvalidOperationException(
                        "The owners this deployment serves are read before the startup gate that establishes them has "
                        + "run. Either the process is still starting, which the startup probe reports until every gate "
                        + "has completed, or the caller is composed outside the host's own startup ordering.");
            }
        }
    }

    /// <summary>Gets the mail accounts every served owner declares, across the whole roster.</summary>
    /// <returns>The accounts, empty while the startup gate that establishes the roster has not run.</returns>
    /// <remarks>
    /// The one read that answers rather than refusing before the gate, because its callers judge a candidate or a
    /// reload rather than serve mail: a rule set is judged against the accounts that exist, and none exist yet. What
    /// keeps that from being a hole is that the same judgement runs again over the composed configuration, where the
    /// owners are read from the file directly and the roster is not consulted at all.
    /// </remarks>
    public IReadOnlyList<MailSynchronizationAccountOptions> MailAccountsOfEveryOwner()
    {
        lock (this.mutex)
        {
            return [.. (this.resolvedOwners ?? []).SelectMany(owner => owner.MailAccounts)];
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the startup gate has not yet run, or when this deployment serves more than one owner and there is therefore no sole owner to name.</exception>
    /// <remarks>
    /// The sole owner is what a surface with no credential to read an owner off acts for, so a deployment serving
    /// several has no answer here rather than a first one. Nothing picks between them: attributing a caller to whichever
    /// owner came first is how one person is handed another person's mail.
    /// </remarks>
    public MailOwnerId Owner =>
        this.Owners is [var soleOwner]
            ? soleOwner.Owner
            : throw new InvalidOperationException(
                "This deployment serves more than one owner, so there is no sole owner for a caller to be composed "
                + "against. A credential names the owner it acts for; until every owner-facing surface reads one, a "
                + "deployment serving several owners serves no owner-facing surface.");

    /// <summary>Finds the owner a mail account belongs to and the declaration this roster holds for it.</summary>
    /// <param name="accountId">The identifier the account is named by.</param>
    /// <returns>The owner and their declaration, or <see langword="null" /> when no owner of this roster holds one under that identifier.</returns>
    /// <remarks>
    /// It answers only about the owners whose declarations this record holds — an owner declared in their own section
    /// of the file, and one who has taken their record over. An account of the deployment's own section is not here,
    /// because that section is the reloadable mail snapshot's and is where the lookup that calls this looks first.
    /// </remarks>
    public (MailOwnerId Owner, MailSynchronizationAccountOptions Account)? FindAccount(MailAccountId accountId) =>
        this.Owners
            .SelectMany(owner => owner.MailAccounts.Select(account => (owner.Owner, Account: account)))
            .Where(entry => StringComparer.Ordinal.Equals(
                MailSynchronizationOptions.TryReadAccountId(entry.Account.AccountId),
                accountId.Value))
            .Cast<(MailOwnerId Owner, MailSynchronizationAccountOptions Account)?>()
            .FirstOrDefault();

    /// <summary>States the roster the startup gate established.</summary>
    /// <param name="owners">Every owner this deployment serves, each with the source their accounts are read from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="owners" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the roster is empty, which is a deployment serving nobody rather than a roster.</exception>
    internal void Resolved(IReadOnlyList<ServedMailOwner> owners)
    {
        ArgumentNullException.ThrowIfNull(owners);

        if (owners.Count == 0)
        {
            throw new ArgumentException("A deployment serves at least one owner.", nameof(owners));
        }

        lock (this.mutex)
        {
            this.resolvedOwners = owners;
        }
    }
}
