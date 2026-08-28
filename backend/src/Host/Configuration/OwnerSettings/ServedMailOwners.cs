// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using Microsoft.Extensions.Primitives;

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>Holds the owners this deployment serves, once the gate that reconciles them has established who they are.</summary>
/// <remarks>
/// <para>
/// A singleton because the roster is a property of the deployment rather than of a request, and because every admitted
/// caller and every synchronization run is composed against it: resolving it per request would put a database read in
/// front of each of them instead of following the publication raised by the write that changed it.
/// </para>
/// <para>
/// Reading it before the gate has settled it fails rather than answering, because the alternative is an owner nobody
/// named and callers composed against one would read whichever mail a query matched. The window that reading belongs
/// to is a real one rather than a wiring defect alone: the gate is an ordinary hosted service and the web host's own is
/// registered while the builder runs, so the listener is already accepting connections while the gate runs. What holds
/// traffic off that window is the startup probe, which reports the deployment unstarted until every gate has completed.
/// </para>
/// <para>
/// The startup gate publishes the first roster and a committed owner record publishes a replacement. Reads and writes
/// take the same lock, so a caller observes one complete immutable list rather than a collection changing beneath it.
/// The reload token rises only after the replacement is visible, which lets a mail-settings snapshot pair itself with
/// exactly that roster.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "This process-lifetime singleton never requests SemaphoreSlim.AvailableWaitHandle, so the semaphore owns no operating-system handle to release.")]
internal sealed class ServedMailOwners : IDeploymentMailOwnerSource
{
    private readonly Lock mutex = new();
    private readonly Dictionary<MailOwnerId, long> publishedDocumentVersions = [];
    private readonly SemaphoreSlim documentPublication = new(1, 1);
    private ConfigurationReloadToken reloadToken = new();

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

    /// <summary>Gets the established roster, or nothing before the startup gate has run.</summary>
    internal IReadOnlyList<ServedMailOwner>? TryGetOwners()
    {
        lock (this.mutex)
        {
            return this.resolvedOwners;
        }
    }

    /// <summary>Gets a token that changes after a newer runtime roster has been published.</summary>
    internal IChangeToken GetReloadToken() => Volatile.Read(ref this.reloadToken);

    /// <summary>Waits until this process can validate and publish one owner-document write without another overtaking it.</summary>
    internal Task WaitForDocumentPublicationAsync(CancellationToken cancellationToken) =>
        this.documentPublication.WaitAsync(cancellationToken);

    /// <summary>Lets the next owner-document write validate against the roster this one published.</summary>
    internal void ReleaseDocumentPublication() => this.documentPublication.Release();

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

    /// <summary>Gets whether any served owner's mail accounts are their own rather than the deployment's section.</summary>
    /// <returns><see langword="true" /> when at least one owner is served from their own declaration or their own document.</returns>
    /// <remarks>
    /// It answers rather than refusing before the gate, for the reason <see cref="MailAccountsOfEveryOwner" /> does: its
    /// caller judges a reloaded candidate, and a deployment whose roster is not settled yet has nothing for a candidate
    /// to conflict with. The question is about the source rather than about the count, because the deployment's own
    /// section belongs to whichever sole owner a deployment holds and is legitimately populated for that one.
    /// </remarks>
    public bool ServesAnyOwnerFromTheirOwnAccounts()
    {
        lock (this.mutex)
        {
            return (this.resolvedOwners ?? [])
                .Any(owner => owner.Source != MailOwnerAccountSource.DeploymentSection);
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the startup gate has not yet run.</exception>
    /// <exception cref="DeploymentMailOwnerUnresolvedException">Thrown when this deployment serves more than one owner and there is therefore no sole owner to name.</exception>
    /// <remarks>
    /// <para>
    /// The sole owner is what a surface with no credential to read an owner off acts for, so a deployment serving
    /// several has no answer here rather than a first one. Nothing picks between them: attributing a caller to whichever
    /// owner came first is how one person is handed another person's mail.
    /// </para>
    /// <para>
    /// The two absences are different failures and are raised as different types. A roster that has not been settled is
    /// this process asking a question before the gate that answers it, which is a defect in the host's own ordering and
    /// nothing an operator or a caller did. A roster of several is a deployment an operator composed and a start
    /// admitted, reached by a request that names no owner — so it carries a code and a sentence naming what would have
    /// been answered, rather than arriving at a caller as an unclassified fault.
    /// </para>
    /// </remarks>
    public MailOwnerId Owner =>
        this.Owners is [var soleOwner]
            ? soleOwner.Owner
            : throw DeploymentMailOwnerUnresolvedException.NoSoleOwnerToActFor();

    /// <summary>Finds the owner a mail account belongs to and the declaration this roster holds for it.</summary>
    /// <param name="accountId">The identifier the account is named by.</param>
    /// <returns>The owner and their declaration, or <see langword="null" /> when no owner of this roster holds one under that identifier.</returns>
    /// <remarks>
    /// It answers only about the owners whose declarations this record holds — an owner declared in their own section
    /// of the file, and one who has taken their record over. An account of the deployment's own section is not here,
    /// because that section is the reloadable mail snapshot's. The lookup that calls this checks a published owner
    /// document first so an adoption takes effect before the stale deployment section is removed for the next start.
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
            this.resolvedOwners = [.. owners];
            this.publishedDocumentVersions.Clear();
        }

        this.SignalReload();
    }

    /// <summary>Publishes one owner's committed document as the source new operations read their mail accounts from.</summary>
    /// <param name="owner">The owner whose document committed.</param>
    /// <param name="displayName">The label the owner record carries.</param>
    /// <param name="mailAccounts">The validated account declarations the committed document contains.</param>
    /// <param name="version">The committed document version.</param>
    internal void OwnerDocumentPublished(
        MailOwnerId owner,
        string displayName,
        IReadOnlyList<MailSynchronizationAccountOptions> mailAccounts,
        long version)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("A published owner document belongs to a named owner.", nameof(owner));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(mailAccounts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        var changed = false;

        lock (this.mutex)
        {
            var owners = this.resolvedOwners
                ?? throw new InvalidOperationException(
                    "An owner document cannot be published before the startup gate has established the roster.");
            if (!this.publishedDocumentVersions.TryGetValue(owner, out var publishedVersion)
                || version > publishedVersion)
            {
                var published = new ServedMailOwner(
                    owner,
                    displayName,
                    MailOwnerAccountSource.OwnerDocument,
                    [.. mailAccounts]);

                this.resolvedOwners = owners.Any(candidate => candidate.Owner == owner)
                    ? [.. owners.Select(candidate => candidate.Owner == owner ? published : candidate)]
                    : [.. owners, published];
                this.publishedDocumentVersions[owner] = version;
                changed = true;
            }
        }

        if (changed)
        {
            this.SignalReload();
        }
    }

    /// <summary>Removes an erased owner from the runtime roster and publishes the resulting account set.</summary>
    internal void OwnerErased(MailOwnerId owner)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("An erased owner is named.", nameof(owner));
        }

        var changed = false;

        lock (this.mutex)
        {
            var owners = this.resolvedOwners
                ?? throw new InvalidOperationException(
                    "An owner cannot be erased from the runtime roster before the startup gate has established it.");
            var remaining = owners.Where(candidate => candidate.Owner != owner).ToArray();

            if (remaining.Length != owners.Count)
            {
                this.resolvedOwners = remaining;
                this.publishedDocumentVersions.Remove(owner);
                changed = true;
            }
        }

        if (changed)
        {
            this.SignalReload();
        }
    }

    private void SignalReload()
    {
        var changed = Interlocked.Exchange(ref this.reloadToken, new ConfigurationReloadToken());
        changed.OnReload();
    }
}
