// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What an administrator does to the roster itself: who this deployment holds, who joins it, and who leaves.</summary>
/// <remarks>
/// <para>
/// The roster is the relational envelope rather than anybody's record, and this is the whole of what an administrator
/// does to it. What one user has configured is a separate service over the document beside the envelope, because the
/// two are granted apart and reached apart: a user maintains their own record and never the roster, and the roster is
/// deployment-wide and therefore administrative and nothing else.
/// </para>
/// <para>
/// Provisioning writes the envelope and then commits the empty record, which is two statements and one act. The second
/// is what makes the user's mail accounts their own from the start, and it replaces nothing: no configuration source
/// names a user or declares a mailbox, so there is no section for the record to be quietly superseding. The refusal
/// written for a user a source did supply is unreachable and stays until
/// <see href="https://github.com/Krzysztof318/MailFathom/issues/1829">issue 1829</see> retires it with the marker it
/// goes with.
/// </para>
/// <para>
/// Every operation asks for its own permission with the transport absent, as every other permission-bearing use case in
/// this system does, so an entrypoint added later cannot widen this surface by forgetting a route filter. Which
/// permission is which follows what the act costs: reading the roster is
/// <see cref="MailFathomPermission.AdminRead" />, adding somebody is
/// <see cref="MailFathomPermission.AdminConfigurationWrite" /> because it changes who this deployment serves rather
/// than what it does next, and removing somebody is <see cref="MailFathomPermission.AdminErase" /> because it disposes
/// of every message this deployment holds for them.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this service.")]
internal sealed partial class UserRosterAdministration(
    AccessAuthorization authorization,
    IMailUserDirectory directory,
    IMailUserProvisioning provisioning,
    IMailUserErasure erasure,
    IUserSettingsDocumentWriter documents,
    ServedMailUsers servedUsers,
    SeveralUserAdmission admission,
    ConfiguredUserSettings configured,
    ILogger<UserRosterAdministration> logger)
{
    /// <summary>The record a user is provisioned with, which is the empty one until they declare something.</summary>
    private const string EmptyRecord = "{}";

    /// <summary>The version a freshly provisioned row stands at, which the record's first commit is composed over.</summary>
    private const long ProvisionedVersion = 1;

    /// <summary>Reads the users this deployment holds.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The users, in the order they were recorded in, each annotated with what this process is doing about them.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <remarks>
    /// One more than a deployment may declare is read, so a roster past the bound is observable rather than silently
    /// truncated into a listing an administrator would then act on as though it were complete.
    /// </remarks>
    internal async Task<IReadOnlyList<UserRosterEntry>> ReadRosterAsync(CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.AdminRead);

        var held = await directory.ReadUsersAsync(ServedMailUsers.MaximumUsers + 1, cancellationToken);

        // Read once rather than per entry: the declarations are a reflection bind of the whole collection, and this
        // route is read unconditionally by six of the commands `mfctl user` publishes.
        var declaredInConfiguration = configured.UsersAConfigurationSourceDeclares();

        return
        [
            .. held.Select(record => new UserRosterEntry(
                record.User,
                record.DisplayName,
                record.DocumentWrittenAtRuntime,
                Served: servedUsers.Users.Any(served => served.User == record.User),
                DeclaredInConfiguration: declaredInConfiguration.Contains(record.User))),
        ];
    }

    /// <summary>Records a user this deployment did not hold, under an identifier it mints.</summary>
    /// <param name="displayName">The label the user is told apart by, which is unique across the deployment.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The identifier the user was minted under, or the sentence naming what has to change first.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    /// <exception cref="UserSettingsUnwritableException">Thrown when the record's first commit did not complete, which leaves the envelope written and the marker unset.</exception>
    /// <remarks>
    /// The identifier is minted here rather than supplied, and it is a version 4 value for the reason the column is:
    /// a user identifier reaches administrative APIs, audit records, and logs, and a time-ordered one would publish
    /// when each user was provisioned and in what order relative to every other.
    /// </remarks>
    internal async Task<UserProvisioningOutcome> ProvisionAsync(
        string? displayName,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        if (FindLabelRefusal(displayName) is { } unusable)
        {
            return UserProvisioningOutcome.Refused(unusable);
        }

        var label = displayName!.Trim();
        var held = await directory.ReadUsersAsync(ServedMailUsers.MaximumUsers + 1, cancellationToken);

        if (held.Count > 0 && admission.AdmitsACallerNamingNoUser)
        {
            return UserProvisioningOutcome.Refused(admission.Refusal);
        }

        if (held.Count + 1 > ServedMailUsers.MaximumUsers)
        {
            return UserProvisioningOutcome.Refused(
                $"This deployment already holds the {ServedMailUsers.MaximumUsers} users one deployment may serve. Remove a user it no longer serves before recording another.");
        }

        if (held.Any(record => StringComparer.Ordinal.Equals(record.DisplayName, label)))
        {
            return UserProvisioningOutcome.Refused(LabelTaken(label));
        }

        var user = MailUserId.Create(Guid.NewGuid());
        await servedUsers.WaitForRosterPublicationAsync(cancellationToken);

        try
        {
            if (!await provisioning.ProvisionAsync(user, label, cancellationToken))
            {
                // The label was taken between the roster being read and the insert reaching the table, which no reading of
                // a snapshot could have refused earlier.
                return UserProvisioningOutcome.Refused(LabelTaken(label));
            }

            // The record rather than only the envelope, because a user nothing declares is served from their own record
            // or from nothing at all. It is the empty object the envelope already carries, so what the commit changes is
            // the marker beside it — which is what the next start reads to decide that this user is not waiting on a
            // configuration section that does not exist.
            if (await documents.CommitAsync(user, EmptyRecord, ProvisionedVersion, cancellationToken) is not { } committed)
            {
                // The envelope was written and the row is gone again, which is another administrator erasing this user
                // between the two statements. Reporting the user as recorded would hand back an identifier nothing holds;
                // reporting it as provisioned without the marker would leave the next start reading their mail accounts
                // out of a configuration section that was never written for them, and refusing to start over the second
                // such row it met.
                return UserProvisioningOutcome.Refused(
                    "The user was recorded and then removed before their record could be written, so this deployment holds nobody under that label. Record them again.");
            }

            // The committed record is empty, so the user it publishes declares no mailbox, classifies nothing,
            // and reads the deployment's own scanning posture until they write one.
            servedUsers.UserDocumentPublished(user, label, new UserAccountOptions(), committed);

            this.LogUserProvisioned(label);

            return UserProvisioningOutcome.Provisioned(user);
        }
        finally
        {
            servedUsers.ReleaseRosterPublication();
        }
    }

    /// <summary>Puts a new label on a user this deployment already holds.</summary>
    /// <param name="user">The user to relabel.</param>
    /// <param name="displayName">The label the user is told apart by from now on.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the relabel did: whether the deployment holds this user at all, and the sentence naming what has to change first where it holds them and refused.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    /// <remarks>
    /// The label is what an administrator selects a user by and is keyed by nothing, so changing it moves no mail and
    /// invalidates no identifier — which is why this is the configuration grant rather than the erasing one. It reaches
    /// every user this deployment holds, the one its own mail section belongs to included: a label lives on the row and
    /// no configuration source states one.
    /// </remarks>
    internal async Task<UserRelabelOutcome> RelabelAsync(
        MailUserId user,
        string? displayName,
        CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user is relabelled for a named user.", nameof(user));
        }

        authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        if (FindLabelRefusal(displayName) is { } unusable)
        {
            return UserRelabelOutcome.Refused(unusable);
        }

        var label = displayName!.Trim();
        var held = await directory.ReadUsersAsync(ServedMailUsers.MaximumUsers + 1, cancellationToken);

        if (held.All(record => record.User != user))
        {
            return UserRelabelOutcome.NoSuchUser;
        }

        if (held.Any(record => record.User != user && StringComparer.Ordinal.Equals(record.DisplayName, label)))
        {
            return UserRelabelOutcome.Refused(LabelTaken(label));
        }

        if (!await provisioning.RelabelAsync(user, label, cancellationToken))
        {
            // The label was taken between the roster being read and the statement reaching the table, which no reading
            // of a snapshot could have refused earlier.
            return UserRelabelOutcome.Refused(LabelTaken(label));
        }

        this.LogUserRelabelled();

        return UserRelabelOutcome.Relabelled;
    }

    /// <summary>Erases one user and everything this deployment recorded for them.</summary>
    /// <param name="user">The user to remove.</param>
    /// <param name="cancellationToken">Cancels the erasure before it commits.</param>
    /// <returns>What was removed, and whether this process was serving the person it removed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminErase" />.</exception>
    /// <remarks>
    /// <para>
    /// Whether the user was served is read before the erasure rather than after, because the answer must describe the
    /// deployment the caller asked about rather than the roster the erasure left.
    /// </para>
    /// <para>
    /// The user the deployment's own mail section belongs to is refused rather than erased. The next start records a
    /// user for that section wherever it holds none, under an identifier it mints — so the erasure would run, the mail
    /// would go, and the person would be recreated and their mailboxes downloaded again. A deletion request answered
    /// that way is worse than one refused, so what comes back names the section to clear first.
    /// </para>
    /// </remarks>
    internal async Task<UserErasureOutcome> EraseAsync(MailUserId user, CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user is erased for a named user.", nameof(user));
        }

        authorization.RequirePermission(MailFathomPermission.AdminErase);

        if (configured.DeclaredByAConfigurationSource(user))
        {
            var served = servedUsers.Users.Any(candidate => candidate.User == user);
            return new UserErasureOutcome(UserErased: false, served, DeclaredElsewhere);
        }

        await servedUsers.WaitForRosterPublicationAsync(cancellationToken);

        try
        {
            var served = servedUsers.Users.Any(candidate => candidate.User == user);
            var erased = await erasure.EraseAsync(user, cancellationToken);

            if (erased)
            {
                servedUsers.UserErased(user);
                this.LogUserErased(served);
            }

            return new UserErasureOutcome(erased, served);
        }
        finally
        {
            servedUsers.ReleaseRosterPublication();
        }
    }

    /// <summary>Says why a label cannot be a user's, or nothing when it can.</summary>
    /// <remarks>The rules the column and the declared collection are held to, asked here so a label refused in a file is refused over a route.</remarks>
    private static string? FindLabelRefusal(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "A user is recorded with the label an administrator tells them apart by. Write one, unique across this deployment.";
        }

        var label = displayName.Trim();

        return label.Length > MailUserRecord.MaximumDisplayNameLength
            ? $"The label is {label.Length} characters, past the {MailUserRecord.MaximumDisplayNameLength} a user's label is stored as. Shorten it."
            : null;
    }

    /// <summary>The sentence an erasure a start would undo is refused with.</summary>
    /// <remarks>
    /// It names the source rather than the person, because what the operator has to act on is whatever file still
    /// supplies those mailboxes. Nothing reaches it in this release, no source declaring a mailbox any longer; it goes
    /// with the marker <see href="https://github.com/Krzysztof318/MailFathom/issues/1829">issue 1829</see> retires.
    /// </remarks>
    private const string DeclaredElsewhere =
        "A configuration source supplies this user's mail accounts, and a start records a user for it wherever it holds none — so erasing them here would destroy their mail and then recreate the person and download it again. Stop declaring them there, and erase them once no configuration source reaches them.";

    private static string LabelTaken(string label) =>
        $"Another user of this deployment is already recorded as '{label}'. A label is what an administrator selects a user by, so two users carrying one would leave nothing to select on: choose another.";

    /// <remarks>The label rather than the identifier, because it is the operator's own text and the identifier is a generated handle for a person.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "A user labelled {UserDisplayName} was recorded. Their mail accounts are read from their own record; no configuration source reaches them.")]
    private partial void LogUserProvisioned(string userDisplayName);

    /// <remarks>
    /// Neither label is written down, which is the opposite of the line above and deliberate: recording somebody is a
    /// deployment gaining a person, and the label is how an operator then finds the user the line is about, while
    /// renaming one would put two of that person's names in a record outliving the reason either was chosen.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "A user this deployment holds was relabelled. Which user, and under what label, is read from the roster rather than from here.")]
    private partial void LogUserRelabelled();

    /// <remarks>The record names no user at all: a person's whole record was disposed of, and a log line naming them would outlive the erasure it reports.</remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A user and everything recorded for them were erased. This process was serving them: {WasServed}. The runtime roster now excludes them.")]
    private partial void LogUserErased(bool wasServed);
}
