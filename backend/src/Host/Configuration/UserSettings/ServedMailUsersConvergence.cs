// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Brings this replica's roster up to the user records PostgreSQL holds, whichever replica committed them.</summary>
/// <remarks>
/// <para>
/// A committed record is published to the roster of the process that committed it and to no other. Every other replica
/// reaches the same roster here, by reading the version each record stands at and republishing the records newer than
/// the one it bound, and by dropping a user whose record is no longer held — which is what an erasure on another replica
/// leaves. Nothing is taken from the announcement that may have prompted the reading: the rows are the only source, so a
/// replica that missed every announcement converges on the roster a replica that heard them all holds.
/// </para>
/// <para>
/// The reader and the binder are resolved per reading rather than held, exactly as the startup gate resolves them: this
/// service lives as long as the process and is composed before the connection string it reads through exists.
/// </para>
/// <para>
/// The roster's publication lock is held for the whole reading, statements included, because an administrative write on
/// this replica publishes under the same lock. A reading that listed the versions before a local provisioning committed,
/// and compared them after it published, would otherwise drop the user that provisioning had just served.
/// </para>
/// <para>
/// A record that does not bind is not published, the version this replica last bound stays in force, and it is reported
/// once per version — which is what the persisted document's reload does with a document it will not publish. A write is
/// judged before it commits, so reaching this means a row changed behind MailFathom, or a build that judges records
/// differently from the one that committed it.
/// </para>
/// <para>
/// A label is not read from here. Relabelling a user moves no record version, so the label a replica serves is the one
/// it last bound that user's record under — on the replica that relabelled them as on every other — until the record
/// next changes or the replica restarts.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this service.")]
internal sealed partial class ServedMailUsersConvergence(
    IServiceScopeFactory scopes,
    ServedMailUsers servedUsers,
    ILogger<ServedMailUsersConvergence> logger)
{
    /// <summary>The version each user's record was last refused at, read and written only under the roster's publication lock.</summary>
    private readonly Dictionary<MailUserId, long> refusedVersions = [];

    /// <summary>Republishes every user record newer than the one this replica bound, and drops every user no longer held.</summary>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>A task that completes once the roster matches the rows, or once a record that would not bind was reported.</returns>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the rows could not be read, which leaves the roster exactly as it was.</exception>
    /// <remarks>A replica whose startup gate has not settled a roster yet reads nothing: the gate composes the whole roster from the rows itself.</remarks>
    public async Task ConvergeAsync(CancellationToken cancellationToken)
    {
        if (servedUsers.TryGetUsers() is null)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IUserSettingsDocumentReader>();
        var binder = scope.ServiceProvider.GetRequiredService<UserAccountDocumentBinder>();

        await servedUsers.WaitForRosterPublicationAsync(cancellationToken);

        try
        {
            var held = await documents.ReadVersionsAsync(ServedMailUsers.MaximumUsers + 1, cancellationToken);

            if (held.Count > ServedMailUsers.MaximumUsers)
            {
                this.LogTooManyUsersHeld(ServedMailUsers.MaximumUsers);

                return;
            }

            foreach (var erased in servedUsers.Users.Where(served => held.All(record => record.User != served.User)))
            {
                servedUsers.UserErased(erased.User);
                this.refusedVersions.Remove(erased.User);
            }

            foreach (var record in held)
            {
                await this.RepublishWhenNewerAsync(documents, binder, record, cancellationToken);
            }
        }
        finally
        {
            servedUsers.ReleaseRosterPublication();
        }
    }

    private async Task RepublishWhenNewerAsync(
        IUserSettingsDocumentReader documents,
        UserAccountDocumentBinder binder,
        UserSettingsDocumentVersion stored,
        CancellationToken cancellationToken)
    {
        if (stored.Version <= servedUsers.PublishedVersionOf(stored.User)
            || this.refusedVersions.GetValueOrDefault(stored.User) == stored.Version)
        {
            return;
        }

        // Absent when the user was erased between the two statements, which the next reading drops them for.
        if (await documents.ReadAsync(stored.User, cancellationToken) is not { } record)
        {
            return;
        }

        var binding = binder.Bind(
            MailAccountRecordComposition.Compose(record.Json, record.MailAccounts),
            UserRecordArrival.AlreadyHeld);

        if (binding.User is not { } bound)
        {
            this.refusedVersions[record.User] = record.Version;
            this.LogRecordRefused(record.DisplayName, record.Version, binding.Refusals.Count);

            return;
        }

        this.refusedVersions.Remove(record.User);
        servedUsers.UserDocumentPublished(record.User, record.DisplayName, bound, record.Version);
    }

    /// <remarks>The label rather than the identifier, for the reason the startup gate gives: it is the operator's own text, and the identifier is a generated handle for a person. The count rather than the refusals, because a refusal quotes what the record says.</remarks>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The record of the user labelled {UserDisplayName} was rejected at version {RejectedVersion} and is not in force on this replica; the version it last bound stays in force. It named {RefusalCount} settings to correct.")]
    private partial void LogRecordRefused(string userDisplayName, long rejectedVersion, int refusalCount);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "This deployment holds more than the {MaximumUsers} users one deployment may serve, so this replica left its roster as it was rather than serving a truncated one.")]
    private partial void LogTooManyUsersHeld(int maximumUsers);
}
