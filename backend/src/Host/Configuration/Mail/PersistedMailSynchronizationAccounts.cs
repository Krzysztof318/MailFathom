// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.Signals;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using Microsoft.Extensions.Primitives;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Reads the accounts synchronization supervises, and what one account's run reads, from PostgreSQL per pass and per run.</summary>
/// <remarks>
/// <para>
/// A run reads the users its account is assigned to and composes each of their records exactly as the served users do,
/// because what a run derives from an account's <em>neighbours</em> — the mail domains and addresses that are that
/// person's own — is read across the other accounts of the same user. That is a handful of records rather than the
/// deployment, and it is read when the run begins, so a commit reaches the next run of the account it changed whichever
/// replica supervises it.
/// </para>
/// <para>
/// A user whose own record does not bind, or whose record the reader refuses for what it holds, is left out of the run
/// rather than failing it, for the reason such a user is left unserved: composing them reports the record, and
/// an account nobody else is assigned is then no longer one this run can find, so its runs wait out their backoff until
/// the record is corrected. A database that declines the read fails the run instead.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this source.")]
internal sealed class PersistedMailSynchronizationAccounts(
    ISettingsSnapshot<MailSynchronizationOptions> boundSettings,
    IServedMailAccountReader servedAccounts,
    IMailAccountAssignments assignments,
    IUserSettingsDocumentReader documents,
    ServedUserRecordComposition composition,
    ConfigurationChangeAnnouncements announcements,
    WithheldMailAccounts withheldAccounts,
    ServedUsers servedUsers) : IMailSynchronizationAccountSource
{
    /// <summary>How many accounts one statement of a pass reads.</summary>
    /// <remarks>Large enough that a deployment of thousands of accounts is a handful of statements per pass, and small enough that one page is a few kilobytes of identifiers rather than a table.</remarks>
    internal const int PageSize = 500;

    /// <summary>What each run's settings were composed from, held for as long as the settings themselves are.</summary>
    /// <remarks>Keyed on the settings instance rather than on the account, so it holds nothing for an account whose supervisor has ended and needs nothing to remove it.</remarks>
    private readonly ConditionalWeakTable<MailSynchronizationOptions, RunComposition> compositions = new();

    /// <summary>The settings each held user was last served under, held for as long as that composition of the user is.</summary>
    /// <remarks>Keyed on the composition rather than on the user, so a user whose record moved is served under new settings and the old ones go with the composition they were built from.</remarks>
    private readonly ConditionalWeakTable<ServedUser, UserComposition> userSettings = new();

    /// <inheritdoc />
    public async IAsyncEnumerable<SupervisedMailAccount> ReadSupervisedAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guid? after = null;
        IReadOnlyList<ServedMailAccountVersion> page;

        do
        {
            page = await servedAccounts.ReadServedVersionsAsync(after, PageSize, cancellationToken);

            foreach (var account in page
                .Select(static served => new SupervisedMailAccount(MailAccountId.Create(served.Id.ToString("D")), served.Version))
                .Where(supervised => !withheldAccounts.IsWithheld(supervised.Account)))
            {
                yield return account;
            }

            after = page.Count > 0 ? page[^1].Id : after;
        }
        while (page.Count == PageSize);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Whether anything changed is read off the users' record versions rather than off the settings themselves, because
    /// every write of an account, and every assignment made or withdrawn, moves the version of each user it is assigned
    /// to. The records are still read on every run; what an unchanged answer saves is composing them again and the push
    /// sessions a new instance would recycle.
    /// </remarks>
    public async Task<MailSynchronizationOptions?> ReadRunSettingsAsync(
        MailAccountId account,
        MailSynchronizationOptions? previous,
        CancellationToken cancellationToken)
    {
        if (withheldAccounts.IsWithheld(account))
        {
            return null;
        }

        var users = await assignments.ReadUsersAssignedToAsync(account, cancellationToken);
        var records = new List<UserSettingsDocument>(users.Count);

        foreach (var user in users)
        {
            if (await this.ReadRecordAsync(user, cancellationToken) is { } document)
            {
                records.Add(document);
            }
        }

        var readFrom = new RunComposition(
            boundSettings.Current,
            string.Join(',', records.Select(static record => $"{record.User.Value:D}@{record.Version}")));

        if (previous is not null
            && this.compositions.TryGetValue(previous, out var previousReadFrom)
            && previousReadFrom == readFrom)
        {
            return previous;
        }

        var runSettings = readFrom.Bound.WithServedUsers([.. records.Select(this.Compose).OfType<ServedUser>()]);

        if (runSettings.FindConfiguredAccount(account) is null)
        {
            return null;
        }

        this.compositions.AddOrUpdate(runSettings, readFrom);

        return runSettings;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The same instance answers every request of one user while neither their record nor the deployment's section has
    /// moved, so the per-account maps its readers memoize are built once rather than once per request.
    /// </remarks>
    public async Task<MailSynchronizationOptions> ReadUserSettingsAsync(UserId user, CancellationToken cancellationToken)
    {
        var bound = boundSettings.Current;

        if (await servedUsers.ReadAsync(user, cancellationToken) is not { } served)
        {
            return bound.WithServedUsers([]);
        }

        if (this.userSettings.TryGetValue(served, out var composed) && ReferenceEquals(composed.Bound, bound))
        {
            return composed.Settings;
        }

        var settings = bound.WithServedUsers([served]);

        this.userSettings.AddOrUpdate(served, new UserComposition(bound, settings));

        return settings;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A committed change to any account is announced, here and to every other replica, and a withholding is this
    /// replica's own; either may move an account into or out of what a pass reads.
    /// </remarks>
    public IChangeToken GetChangeToken() => new CompositeChangeToken(
    [
        announcements.GetChangeToken(),
        withheldAccounts.GetChangeToken(),
    ]);

    /// <summary>Reads one assigned user's record, leaving out one the reader refused for what it holds.</summary>
    /// <remarks>
    /// A refusal carrying no inner failure is the record's own — a document past the octets one is bound from, or more
    /// accounts than one user is served with — and is left out like a record that does not bind. One with an inner
    /// failure is the database declining the read, which fails the run so it is backed off and read again.
    /// </remarks>
    private async Task<UserSettingsDocument?> ReadRecordAsync(UserId user, CancellationToken cancellationToken)
    {
        try
        {
            return await documents.ReadAsync(user, cancellationToken);
        }
        catch (UserSettingsUnreadableException refused) when (refused.InnerException is null)
        {
            return null;
        }
    }

    private ServedUser? Compose(UserSettingsDocument document) =>
        composition.Compose(document, UserRecordArrival.AlreadyHeld).Record is { } record
            ? new ServedUser(document.User, document.DisplayName, [.. record.MailAccounts])
            : null;

    /// <summary>One user's settings and the bound settings instance they were composed over.</summary>
    /// <param name="Bound">The bound settings instance, compared by reference because a reload binds a new one.</param>
    /// <param name="Settings">The bound settings carrying the user's accounts.</param>
    private sealed record UserComposition(MailSynchronizationOptions Bound, MailSynchronizationOptions Settings);

    /// <summary>What one run's settings were composed from: the bound settings, and each user's record at the version read.</summary>
    /// <param name="Bound">The bound settings instance, compared by reference because a reload binds a new one.</param>
    /// <param name="UserVersions">Each user read and the version their record stood at, in the order they were read.</param>
    private sealed record RunComposition(MailSynchronizationOptions Bound, string UserVersions);
}
