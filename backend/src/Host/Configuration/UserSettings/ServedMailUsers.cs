// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using Microsoft.Extensions.Primitives;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Holds the users this deployment serves, once the gate that reconciles them has established who they are.</summary>
/// <remarks>
/// <para>
/// A singleton because the roster is a property of the deployment rather than of a request, and because every admitted
/// caller and every synchronization run is composed against it: resolving it per request would put a database read in
/// front of each of them instead of following the publication raised by the write that changed it.
/// </para>
/// <para>
/// Reading it before the gate has settled it fails rather than answering, because the alternative is a user nobody
/// named and callers composed against one would read whichever mail a query matched. The window that reading belongs
/// to is a real one rather than a wiring defect alone: the gate is an ordinary hosted service and the web host's own is
/// registered while the builder runs, so the listener is already accepting connections while the gate runs. What holds
/// traffic off that window is the startup probe, which reports the deployment unstarted until every gate has completed.
/// </para>
/// <para>
/// The startup gate publishes the first roster and a committed user record publishes a replacement. Reads and writes
/// take the same lock, so a caller observes one complete immutable list rather than a collection changing beneath it.
/// The reload token rises only after the replacement is visible, which lets a mail-settings snapshot pair itself with
/// exactly that roster.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "This process-lifetime singleton never requests SemaphoreSlim.AvailableWaitHandle, so the semaphore owns no operating-system handle to release.")]
internal sealed class ServedMailUsers : IDeploymentMailUserSource
{
    /// <summary>The greatest number of users one deployment may serve.</summary>
    /// <remarks>
    /// It bounds the roster a deployment records rather than a list anybody writes, so it is generous against any
    /// deployment serving people and far below the point at which a start would spend meaningful time reading them.
    /// Meeting it means rows were recorded by something other than an administrator, which is worth stopping for.
    /// </remarks>
    public const int MaximumUsers = 256;

    private readonly Lock mutex = new();
    private readonly Dictionary<MailUserId, long> publishedDocumentVersions = [];
    private readonly SemaphoreSlim rosterPublication = new(1, 1);
    private ConfigurationReloadToken reloadToken = new();

    /// <summary>The roster the startup gate established, or nothing while it has not run.</summary>
    /// <remarks>
    /// Absence is what is being stored rather than an empty roster, which is why the field is nullable: a deployment
    /// before its gate has run serves nobody <em>yet</em>, and an empty list would read as a deployment that serves
    /// nobody at all. Every read and write of it is taken under <see cref="mutex" />.
    /// </remarks>
    private IReadOnlyList<ServedMailUser>? resolvedUsers;

    /// <summary>Gets every user this deployment serves, in the order the roster was established in.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the startup gate that establishes the roster has not yet run.</exception>
    public IReadOnlyList<ServedMailUser> Users
    {
        get
        {
            lock (this.mutex)
            {
                return this.resolvedUsers
                    ?? throw new InvalidOperationException(
                        "The users this deployment serves are read before the startup gate that establishes them has "
                        + "run. Either the process is still starting, which the startup probe reports until every gate "
                        + "has completed, or the caller is composed outside the host's own startup ordering.");
            }
        }
    }

    /// <summary>Gets the established roster, or nothing before the startup gate has run.</summary>
    internal IReadOnlyList<ServedMailUser>? TryGetUsers()
    {
        lock (this.mutex)
        {
            return this.resolvedUsers;
        }
    }

    /// <summary>Gets a token that changes after a newer runtime roster has been published.</summary>
    internal IChangeToken GetReloadToken() => Volatile.Read(ref this.reloadToken);

    /// <summary>Waits until this process can validate and publish one user-document write without another overtaking it.</summary>
    internal Task WaitForRosterPublicationAsync(CancellationToken cancellationToken) =>
        this.rosterPublication.WaitAsync(cancellationToken);

    /// <summary>Lets the next user-document write validate against the roster this one published.</summary>
    internal void ReleaseRosterPublication() => this.rosterPublication.Release();

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the startup gate has not yet run.</exception>
    /// <exception cref="DeploymentMailUserUnresolvedException">Thrown when this deployment serves nobody, or serves more than one user, and there is therefore no sole user to name.</exception>
    /// <remarks>
    /// <para>
    /// The sole user is what a surface with no credential to read a user off acts for, so a deployment serving
    /// several has no answer here rather than a first one. Nothing picks between them: attributing a caller to whichever
    /// user came first is how one person is handed another person's mail.
    /// </para>
    /// <para>
    /// The absences are different failures and are raised as different types. A roster that has not been settled is
    /// this process asking a question before the gate that answers it, which is a defect in the host's own ordering and
    /// nothing an operator or a caller did. A roster of none and a roster of several are deployments a start admitted,
    /// reached by a request that names no user — so each carries a code and a sentence naming its own remedy, recording
    /// somebody where there is nobody and a credential naming the user where there are several, rather than arriving at
    /// a caller as an unclassified fault.
    /// </para>
    /// </remarks>
    public MailUserId User =>
        this.Users switch
        {
            [var soleUser] => soleUser.User,
            [] => throw DeploymentMailUserUnresolvedException.NoUserToActFor(),
            _ => throw DeploymentMailUserUnresolvedException.NoSoleUserToActFor(),
        };

    /// <summary>Finds the user a mail account belongs to and the declaration this roster holds for it.</summary>
    /// <param name="accountId">The identifier the account is named by.</param>
    /// <returns>The user and their declaration, or <see langword="null" /> when no user of this roster holds one under that identifier.</returns>
    /// <remarks>
    /// It answers about every user this roster serves, each from their own record, which is the one place a mail
    /// account is declared. An identifier two users record answers with the one recorded first, which is the collision
    /// the startup gate reports until <see href="https://github.com/Krzysztof318/MailFathom/issues/1325">issue 1325</see>
    /// keys this lookup by the user as well.
    /// </remarks>
    public (MailUserId User, MailSynchronizationAccountOptions Account)? FindAccount(MailAccountId accountId) =>
        this.Users
            .SelectMany(user => user.MailAccounts.Select(account => (user.User, Account: account)))
            .Where(entry => StringComparer.Ordinal.Equals(
                MailSynchronizationOptions.TryReadAccountId(entry.Account.AccountId),
                accountId.Value))
            .Cast<(MailUserId User, MailSynchronizationAccountOptions Account)?>()
            .FirstOrDefault();

    /// <summary>States the roster the startup gate established.</summary>
    /// <param name="users">Every user this deployment serves, each composed from their own record.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="users" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An empty roster is a state a start admits rather than refuses: a fresh database is seeded with one user, but an
    /// administrator may erase every user a deployment holds, and a deployment left that way starts and serves nobody
    /// until one is recorded. What absence still means is *the gate has not run*, which is why that is a null field
    /// rather than an empty list.
    /// </remarks>
    internal void Resolved(IReadOnlyList<ServedMailUser> users)
    {
        ArgumentNullException.ThrowIfNull(users);

        lock (this.mutex)
        {
            this.resolvedUsers = [.. users];
            this.publishedDocumentVersions.Clear();
        }

        this.SignalReload();
    }

    /// <summary>Publishes one user's committed document as the source new operations read their mail accounts from.</summary>
    /// <param name="user">The user whose document committed.</param>
    /// <param name="displayName">The label the user record carries.</param>
    /// <param name="record">The validated record the committed document bound to.</param>
    /// <param name="version">The committed document version.</param>
    /// <exception cref="ArgumentException">Thrown when the user is unspecified, the display name is blank, or the version is not positive.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the record is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the startup gate has not established the roster.</exception>
    /// <remarks>
    /// The whole bound record rather than the accounts alone, because everything of a user's the roster publishes
    /// comes from one document: a second parameter per settings block would leave a caller free to publish a user's
    /// mailboxes from the committed record and their spam or scanning posture from somewhere else.
    /// </remarks>
    internal void UserDocumentPublished(
        MailUserId user,
        string displayName,
        UserAccountOptions record,
        long version)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A published user document belongs to a named user.", nameof(user));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        var changed = false;

        lock (this.mutex)
        {
            var users = this.resolvedUsers
                ?? throw new InvalidOperationException(
                    "A user document cannot be published before the startup gate has established the roster.");
            if (!this.publishedDocumentVersions.TryGetValue(user, out var publishedVersion)
                || version > publishedVersion)
            {
                var published = new ServedMailUser(
                    user,
                    displayName,
                    [.. record.MailAccounts],
                    record.SpamClassification,
                    record.SensitiveContent);

                this.resolvedUsers = users.Any(candidate => candidate.User == user)
                    ? [.. users.Select(candidate => candidate.User == user ? published : candidate)]
                    : [.. users, published];
                this.publishedDocumentVersions[user] = version;
                changed = true;
            }
        }

        if (changed)
        {
            this.SignalReload();
        }
    }

    /// <summary>Removes an erased user from the runtime roster and publishes the resulting account set.</summary>
    /// <param name="user">The user whose record was erased.</param>
    /// <exception cref="ArgumentException">Thrown when the user is unspecified.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the startup gate has not established the roster.</exception>
    internal void UserErased(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("An erased user is named.", nameof(user));
        }

        var changed = false;

        lock (this.mutex)
        {
            var users = this.resolvedUsers
                ?? throw new InvalidOperationException(
                    "A user cannot be erased from the runtime roster before the startup gate has established it.");
            var remaining = users.Where(candidate => candidate.User != user).ToArray();

            if (remaining.Length != users.Count)
            {
                this.resolvedUsers = remaining;
                this.publishedDocumentVersions.Remove(user);
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
