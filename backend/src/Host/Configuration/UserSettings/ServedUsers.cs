// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Records;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Resolves the users this deployment serves one at a time, from a cache keyed by the version of each user's record.</summary>
/// <remarks>
/// <para>
/// A deployment serves every user its database holds, and no replica holds all of them: a user is composed from their
/// own record the first time something on this replica asks for them, and held beside the version it was composed at.
/// What a replica holds therefore follows the people it is serving now rather than the people the deployment has
/// recorded, and a start reads nobody's record.
/// </para>
/// <para>
/// A held user is current until their record moves. A write on this replica puts the version it committed here
/// directly, and every other replica compares what it holds against the stored versions when the backplane announces a
/// committed change, or on the convergence interval where no backplane is declared — one statement naming the held
/// users rather than a read of everybody. A user nobody has asked for over <see cref="IdleLifetime" /> is let go at that
/// comparison, so a replica's memory follows its traffic.
/// </para>
/// <para>
/// A version that does not bind leaves the user served from the last version that did, where this replica holds one,
/// exactly as a refused write leaves the record it would have replaced: one broken commit is reported rather than
/// costing that person their mail.
/// </para>
/// <para>
/// Whether the deployment holds nobody, one user, or several is a value rather than a read, because a request carrying
/// no user is attributed to the sole one and that question is asked per request. It is read when the startup gate runs,
/// after this replica records or erases somebody, and at every comparison.
/// </para>
/// </remarks>
internal sealed class ServedUsers(
    IUserSettingsDocumentReader documents,
    ServedUserResolution resolution,
    HeldBackRecords heldBackRecords,
    TimeProvider timeProvider) : IDeploymentUserSource
{
    /// <summary>How many users one comparison statement names.</summary>
    /// <remarks>A bound on one statement's parameter rather than on how many users a replica holds: a replica holding more is compared in several statements.</remarks>
    internal const int ComparedPerStatement = 1000;

    /// <summary>How long a held user nobody has asked for is kept before the next comparison lets them go.</summary>
    internal static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(15);

    private readonly Lock mutex = new();
    private readonly Dictionary<UserId, HeldUser> held = [];

    /// <summary>How many users the deployment holds, read no further than two, or nothing before the startup gate has read it.</summary>
    private DeploymentUserCount? count;

    /// <summary>How many erasures are deciding about each user; a user is served nothing while their count is above zero.</summary>
    private readonly Dictionary<UserId, int> withheld = [];

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the startup gate has not yet read how many users the deployment holds.</exception>
    /// <exception cref="DeploymentUserUnresolvedException">Thrown when this deployment holds nobody, or holds more than one user, and there is therefore no sole user to name.</exception>
    /// <remarks>
    /// <para>
    /// The sole user is what a surface with no credential to read a user off acts for, so a deployment holding several
    /// has no answer here rather than a first one. Nothing picks between them: attributing a caller to whichever user
    /// came first is how one person is handed another person's mail.
    /// </para>
    /// <para>
    /// The answer counts the rows the deployment holds rather than the users this replica has composed, so a user whose
    /// erasure is being decided, or whose record does not bind, still counts: nothing has been deleted, and reading them
    /// as absent would answer a deployment of two as a deployment of one.
    /// </para>
    /// </remarks>
    public UserId User
    {
        get
        {
            lock (this.mutex)
            {
                return this.count switch
                {
                    null => throw new InvalidOperationException(
                        "The sole user this deployment serves is read before the startup gate that reads it has run. "
                        + "Either the process is still starting, which the startup probe reports until every gate has "
                        + "completed, or the caller is composed outside the host's own startup ordering."),
                    { Sole: { } sole } => sole,
                    { Held: 0 } => throw DeploymentUserUnresolvedException.NoUserToActFor(),
                    _ => throw DeploymentUserUnresolvedException.NoSoleUserToActFor(),
                };
            }
        }
    }

    /// <summary>Reads how many users the deployment holds, as far as telling nobody, one, and several apart.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Zero, one, or two, the last standing for any number past one.</returns>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the rows could not be read, which leaves the previous answer in force.</exception>
    internal async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        var first = await documents.ReadVersionsAsync(2, cancellationToken);
        var counted = new DeploymentUserCount(first.Count, first.Count == 1 ? first[0].User : null);

        lock (this.mutex)
        {
            this.count = counted;
        }

        return counted.Held;
    }

    /// <summary>Reads one user this deployment serves, composing them from their record when this replica holds no current composition.</summary>
    /// <param name="user">The user asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The user, or <see langword="null" /> when the deployment holds no such user, their record does not bind and never has here, or their erasure is being decided.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the record could not be read.</exception>
    internal async Task<ServedUser?> ReadAsync(UserId user, CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A served user is read for a named user.", nameof(user));
        }

        lock (this.mutex)
        {
            if (this.withheld.ContainsKey(user))
            {
                return null;
            }

            if (this.held.TryGetValue(user, out var current))
            {
                current.LastRead = timeProvider.GetUtcNow();

                return current.Served;
            }
        }

        return await this.ComposeAsync(user, cancellationToken);
    }

    /// <summary>Finds one user this replica already holds, without reading anything.</summary>
    /// <param name="user">The user asked for.</param>
    /// <returns>The user, or <see langword="null" /> when this replica holds no composition of them or their erasure is being decided.</returns>
    /// <remarks>
    /// For the answers about a person that are synchronous by contract — their language, their zone, their client's
    /// telemetry level. Whatever acts for a user first prepares its scope with their accounts, which reads them through
    /// <see cref="ReadAsync" />, so a caller asking here acts for somebody this replica is already holding; asking keeps
    /// them held as reading them does, so work running longer than <see cref="IdleLifetime" /> is not let go of halfway.
    /// </remarks>
    internal ServedUser? Peek(UserId user)
    {
        lock (this.mutex)
        {
            if (this.withheld.ContainsKey(user) || !this.held.TryGetValue(user, out var current))
            {
                return null;
            }

            current.LastRead = timeProvider.GetUtcNow();

            return current.Served;
        }
    }

    /// <summary>Holds one user's committed record as the version this replica serves them from.</summary>
    /// <param name="user">The user whose document committed.</param>
    /// <param name="displayName">The label the user record carries.</param>
    /// <param name="record">The validated record the committed document bound to.</param>
    /// <param name="version">The committed document version.</param>
    /// <exception cref="ArgumentException">Thrown when the user is unspecified, the display name is blank, or the version is not positive.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the record is <see langword="null" />.</exception>
    /// <remarks>
    /// The whole bound record rather than the accounts alone, because everything of a user's this serves comes from one
    /// document. A version older than the one already held changes nothing, so a write overtaken by a newer reading
    /// cannot put the older record back.
    /// </remarks>
    internal void UserDocumentPublished(
        UserId user,
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

        this.Published(
            new ServedUser(
                user,
                displayName,
                [.. record.MailAccounts],
                record.ReadingLanguage ?? UserLanguage.English)
            {
                TimeZone = record.ReadingTimeZone,
                ClientTelemetryLevel = record.ReadingClientTelemetryLevel,
            },
            version);
    }

    /// <summary>Holds one user composed from a committed record as the version this replica serves them from.</summary>
    /// <param name="served">The composed user.</param>
    /// <param name="version">The committed document version.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="served" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="version" /> is not positive.</exception>
    /// <remarks>A version older than the one already held changes nothing, exactly as <see cref="UserDocumentPublished" /> states.</remarks>
    internal void Published(ServedUser served, long version)
    {
        ArgumentNullException.ThrowIfNull(served);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        this.Hold(served.User, served, version);
    }

    /// <summary>Lets go of an erased user and of everything reported about their record.</summary>
    /// <param name="user">The user whose record was erased.</param>
    /// <exception cref="ArgumentException">Thrown when the user is unspecified.</exception>
    internal void UserErased(UserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("An erased user is named.", nameof(user));
        }

        lock (this.mutex)
        {
            this.held.Remove(user);
        }

        heldBackRecords.Cleared(user);
    }

    /// <summary>Serves a user nothing while something decides whether to erase them, and serves them again unless it did.</summary>
    /// <param name="user">The user whose erasure is being decided.</param>
    /// <returns>The withholding, which serves the user again when it is disposed without <see cref="Withholding.Erased" /> having been called.</returns>
    /// <exception cref="ArgumentException">Thrown when the user is unspecified.</exception>
    /// <remarks>
    /// An erasure has to stop this process serving the user <em>before</em> it deletes anything, so no request acts for
    /// them while the deletion runs: every reader of the assignment relation is answered through
    /// <see cref="WithholdingMailAccountAssignments" />, which gives a withheld user no mailbox although their rows are
    /// still there to be deleted, and this resolution answers nobody for them. But an erasure can still be refused, and a
    /// person nothing erased must go on being served rather than disappear. Each withholding is counted against its own
    /// user, so two erasures deciding about two people at once each keep theirs withheld, and one disposed first never
    /// serves the other again.
    /// </remarks>
    internal Withholding Withhold(UserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A withheld user is named.", nameof(user));
        }

        lock (this.mutex)
        {
            this.withheld[user] = this.withheld.GetValueOrDefault(user) + 1;
        }

        return new Withholding(this, user);
    }

    /// <summary>Gets whether an erasure is deciding about this user right now.</summary>
    /// <param name="user">The user asked about.</param>
    /// <returns><see langword="true" /> from the moment the user is withheld until the withholding ends either way.</returns>
    internal bool IsWithheld(UserId user)
    {
        lock (this.mutex)
        {
            return this.withheld.ContainsKey(user);
        }
    }

    /// <summary>Answers which of a bounded set of users this replica serves, composing none of them.</summary>
    /// <param name="users">The users asked about, at most <see cref="ComparedPerStatement" /> of them.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>The users among <paramref name="users" /> this replica serves.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="users" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more users are asked about than one statement compares.</exception>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the rows could not be read.</exception>
    /// <remarks>
    /// A held user is answered from what was composed, and a withheld one is served nothing. A user this replica holds
    /// no answer for is served when their record exists, read for all of them in one statement, because composing each
    /// would turn one listing into a read and a secret resolution per person; a record that turns out not to bind is
    /// held back the first time somebody acts for them.
    /// </remarks>
    internal async Task<IReadOnlySet<UserId>> ReadServedAmongAsync(
        IReadOnlyCollection<UserId> users,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(users.Count, ComparedPerStatement);

        HashSet<UserId> served = [];
        UserId[] unanswered;

        lock (this.mutex)
        {
            served.UnionWith(users.Where(user =>
                !this.withheld.ContainsKey(user) && this.held.TryGetValue(user, out var current) && current.Served is not null));
            unanswered = [.. users.Where(user => !this.withheld.ContainsKey(user) && !this.held.ContainsKey(user))];
        }

        if (unanswered.Length > 0)
        {
            served.UnionWith((await documents.ReadVersionsAsync(unanswered, cancellationToken)).Select(static row => row.User));
        }

        return served;
    }

    /// <summary>Compares every held user against the version their record stands at, and recomposes or lets go of whoever moved.</summary>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>A task that completes once every held user matches their row, or has been let go.</returns>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the rows could not be read, which leaves every held user as they were.</exception>
    /// <remarks>
    /// A user nobody has asked for over <see cref="IdleLifetime" /> is let go rather than compared, and a user whose row
    /// is gone is let go; either takes what was reported about them along, because a user nothing compares again would
    /// otherwise leave their refusals listed after their record was repaired or erased. A user whose record moved is recomposed here rather
    /// than on their next request, because that is what keeps them served from the last version that bound while a newer
    /// one is refused.
    /// </remarks>
    internal async Task ConvergeAsync(CancellationToken cancellationToken)
    {
        await this.CountAsync(cancellationToken);

        foreach (var chunk in this.LetGoOfIdleUsers().Chunk(ComparedPerStatement))
        {
            var stored = (await documents.ReadVersionsAsync(chunk, cancellationToken))
                .ToDictionary(static row => row.User, static row => row.Version);

            foreach (var user in chunk)
            {
                await this.ConvergeAsync(user, stored.TryGetValue(user, out var version) ? version : null, cancellationToken);
            }
        }
    }

    private async Task ConvergeAsync(UserId user, long? storedVersion, CancellationToken cancellationToken)
    {
        if (storedVersion is not { } version)
        {
            this.UserErased(user);

            return;
        }

        lock (this.mutex)
        {
            if (!this.held.TryGetValue(user, out var current) || current.Version >= version)
            {
                return;
            }
        }

        await this.ComposeAsync(user, cancellationToken);
    }

    /// <summary>Lets go of every user nobody asked for within the idle lifetime, and answers the users still held.</summary>
    private UserId[] LetGoOfIdleUsers()
    {
        var oldestKept = timeProvider.GetUtcNow() - IdleLifetime;
        UserId[] idle;
        UserId[] kept;

        lock (this.mutex)
        {
            idle = [.. this.held.Where(entry => entry.Value.LastRead < oldestKept).Select(entry => entry.Key)];

            foreach (var user in idle)
            {
                this.held.Remove(user);
            }

            kept = [.. this.held.Keys];
        }

        foreach (var user in idle)
        {
            heldBackRecords.Cleared(user);
        }

        return kept;
    }

    /// <summary>Composes one user from their record and holds what it composed.</summary>
    private async Task<ServedUser?> ComposeAsync(UserId user, CancellationToken cancellationToken)
    {
        if (await resolution.ResolveAsync(user, cancellationToken) is not { } resolved)
        {
            lock (this.mutex)
            {
                this.held.Remove(user);
            }

            return null;
        }

        return this.Hold(user, resolved.User, resolved.Version);
    }

    /// <summary>Holds a composition unless a newer one is already held, keeping the last one that bound where this one did not.</summary>
    /// <returns>The user this replica now serves them as.</returns>
    private ServedUser? Hold(UserId user, ServedUser? composed, long version)
    {
        lock (this.mutex)
        {
            if (this.held.TryGetValue(user, out var current) && current.Version >= version)
            {
                return this.withheld.ContainsKey(user) ? null : current.Served;
            }

            var served = composed ?? current?.Served;

            this.held[user] = new HeldUser(served, version) { LastRead = timeProvider.GetUtcNow() };

            return this.withheld.ContainsKey(user) ? null : served;
        }
    }

    /// <summary>Ends a withholding, serving the user again unless they were erased.</summary>
    private void Release(UserId user, bool erased)
    {
        lock (this.mutex)
        {
            if (this.withheld.GetValueOrDefault(user) > 1)
            {
                this.withheld[user]--;
            }
            else
            {
                this.withheld.Remove(user);
            }
        }

        if (erased)
        {
            this.UserErased(user);
        }
    }

    /// <summary>One user's composition and the version of their record it was composed at.</summary>
    /// <param name="Served">The user, or <see langword="null" /> where no version this replica read has bound.</param>
    /// <param name="Version">The version of the record last read, which is newer than the one <paramref name="Served" /> was composed at where that version was refused.</param>
    private sealed record HeldUser(ServedUser? Served, long Version)
    {
        /// <summary>Gets or sets when anything last asked for this user, which is what an idle user is let go by.</summary>
        public DateTimeOffset LastRead { get; set; }
    }

    /// <summary>How many users the deployment holds, as far as telling nobody, one, and several apart.</summary>
    /// <param name="Held">Zero, one, or two, the last standing for any number past one.</param>
    /// <param name="Sole">The one user held, where there is exactly one.</param>
    private sealed record DeploymentUserCount(int Held, UserId? Sole);

    /// <summary>One user served nothing while their erasure is decided.</summary>
    /// <remarks>Nested because what it holds is this resolution's decision to serve the user again, which no other type has business naming.</remarks>
    internal sealed class Withholding(ServedUsers servedUsers, UserId user) : IDisposable
    {
        private bool erased;

        /// <summary>Says the user was erased, so they are let go rather than served again.</summary>
        internal void Erased() => this.erased = true;

        /// <inheritdoc />
        public void Dispose() => servedUsers.Release(user, this.erased);
    }
}
