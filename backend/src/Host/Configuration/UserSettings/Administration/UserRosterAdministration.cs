// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.Signals;
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
/// is what proves the row still stands once the first has run, and it yields the version the record this process
/// publishes for the user is composed over.
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
    IUserDirectory directory,
    IUserProvisioning provisioning,
    IUserErasure erasure,
    IMailAccountRecordStore accounts,
    IMailAccountWorkQuiescing quiescing,
    WithheldMailAccounts withheldAccounts,
    IUserSettingsDocumentWriter documents,
    ServedUsers servedUsers,
    SeveralUserAdmission admission,
    ConfigurationChangeAnnouncements announcements,
    ILogger<UserRosterAdministration> logger)
{
    /// <summary>The language a user is provisioned reading, which is the one the client also opens in.</summary>
    /// <remarks>
    /// A provisioning states no language, so one is chosen here rather than asked for: a record being written is
    /// required to name one, and provisioning the empty record instead would hand back a user whose own record could
    /// not be committed again until somebody guessed which line to add. English is the same answer the client reaches
    /// when it can read no preference, and whoever records that user changes it in the same session.
    /// </remarks>
    private const string ProvisionedLanguage = nameof(UserLanguage.English);

    /// <summary>The record a user is provisioned with, which names their language and declares nothing else until they ask for something.</summary>
    /// <remarks>
    /// The language is the one value here that has no absence: everything else a person asks for is absent until they
    /// ask for it, while text composed for them comes out in some language whether or not anybody chose it. What their
    /// mail is read into is not this value and is named when each of their mail accounts is declared.
    /// </remarks>
    private const string ProvisionedRecord = $$"""{"Language":"{{ProvisionedLanguage}}"}""";

    /// <summary>The version a freshly provisioned row stands at, which the record's first commit is composed over.</summary>
    private const long ProvisionedVersion = 1;

    /// <summary>Reads one page of the users this deployment holds.</summary>
    /// <param name="query">The page asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page's users in identifier order, each annotated with what this process is doing about them, and where the following page continues.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminRead" />.</exception>
    internal async Task<AdministrativeListingPage<UserRosterEntry>> ReadRosterAsync(
        AdministrativeListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        authorization.RequirePermission(MailFathomPermission.AdminRead);

        var page = await directory.ReadUserPageAsync(query, cancellationToken);
        var served = await this.ReadServedAmongAsync([.. page.Entries.Select(static record => record.User)], cancellationToken);

        return new AdministrativeListingPage<UserRosterEntry>(
            [
                .. page.Entries.Select(record => new UserRosterEntry(
                    record.User,
                    record.DisplayName,
                    Served: served.Contains(record.User),
                    record.EndpointAccess)),
            ],
            page.ContinuesAfter);
    }

    /// <summary>Records a user this deployment did not hold, under an identifier it mints.</summary>
    /// <param name="displayName">The label the user is told apart by, which is unique across the deployment.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The identifier the user was minted under, or the sentence naming what has to change first.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    /// <exception cref="UserSettingsUnwritableException">Thrown when the record's first commit did not complete, which leaves the envelope written and nothing published for it.</exception>
    /// <remarks>
    /// The identifier is minted here rather than supplied, and it is a version 7 value like every identifier MailFathom
    /// mints. It reaches administrative APIs, audit records, and logs, so it says when each user was provisioned and in
    /// what order relative to every other — a residual ADR 0036 accepts.
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

        if (admission.AdmitsACallerNamingNoUser
            && (await directory.ReadUsersAsync(1, cancellationToken)).Count > 0)
        {
            return UserProvisioningOutcome.Refused(admission.Refusal);
        }

        var outcome = await this.RecordAndPublishAsync(UserId.Create(Guid.CreateVersion7()), label, cancellationToken);

        if (outcome.IsProvisioned)
        {
            await this.RecountAndAnnounceAsync(cancellationToken);
        }

        return outcome;
    }

    /// <summary>Records a user and their first record, and holds them on this replica.</summary>
    private async Task<UserProvisioningOutcome> RecordAndPublishAsync(
        UserId user,
        string label,
        CancellationToken cancellationToken)
    {
        if (!await provisioning.ProvisionAsync(user, label, cancellationToken))
        {
            // The label is unique in the table, so another user carrying it refuses the insert rather than anything read
            // ahead of it, which no read could have done without racing a concurrent write.
            return UserProvisioningOutcome.Refused(LabelTaken(label));
        }

        // Committed rather than published from the insert alone, because the commit is what proves the row still
        // stands and it answers the version the published record is composed over.
        if (await documents.CommitAsync(
                user,
                ProvisionedRecord,
                UserEndpointAccess.Everywhere,
                ProvisionedVersion,
                cancellationToken) is not { } committed)
        {
            // The envelope was written and the row is gone again, which is another administrator erasing this user
            // between the two statements. Reporting the user as recorded would hand back an identifier nothing holds.
            return UserProvisioningOutcome.Refused(
                "The user was recorded and then removed before their record could be written, so this deployment holds nobody under that label. Record them again.");
        }

        // The committed record names a language and declares nothing else, so the user it publishes holds no mailbox,
        // classifies nothing, and reads the deployment's own scanning posture until an account is declared for them.
        servedUsers.UserDocumentPublished(
            user,
            label,
            new UserAccountOptions { Language = ProvisionedLanguage },
            committed);

        this.LogUserProvisioned(label);

        return UserProvisioningOutcome.Provisioned(user);
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
    /// invalidates no identifier — which is why this is the configuration grant rather than the erasing one.
    /// </remarks>
    internal async Task<UserRelabelOutcome> RelabelAsync(
        UserId user,
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

        if (await directory.ReadUserAsync(user, cancellationToken) is null)
        {
            return UserRelabelOutcome.NoSuchUser;
        }

        if (!await provisioning.RelabelAsync(user, label, cancellationToken))
        {
            // The label is unique in the table, so another user carrying it refuses the statement rather than anything
            // read ahead of it, which no read could have done without racing a concurrent write.
            return UserRelabelOutcome.Refused(LabelTaken(label));
        }

        this.LogUserRelabelled();

        return UserRelabelOutcome.Relabelled;
    }

    /// <summary>Erases one user and everything this deployment recorded for them, once their own work has stopped.</summary>
    /// <param name="user">The user to remove.</param>
    /// <param name="cancellationToken">Cancels the erasure before it commits.</param>
    /// <returns>What was removed and whether this process was serving the person it removed, or the sentence naming the work that would not stop.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminErase" />.</exception>
    /// <remarks>
    /// <para>
    /// The order here is the whole of what makes an erasure true afterwards, and none of it is bookkeeping. The user
    /// is withheld from the served-user cache and the accounts they alone were assigned are withheld from synchronization
    /// <em>first</em>, so this replica's coordinator gives their supervision back — the records it reads them from have
    /// not changed yet, which is why the withholding is stated rather than read. No other replica is told before the
    /// deletion commits — the announcement follows a committed erasure and is not made on a refusal — so an account
    /// one of them still supervises refuses this erasure rather than being deleted under it. Only then are those
    /// accounts held stopped, and only under that hold is anything deleted —
    /// because a synchronization run or a job handler writes rows keyed to a mail account rather than to the user, and
    /// no lock the erasure's own transaction could take reaches such a writer.
    /// </para>
    /// <para>
    /// Work that will not stop within its bound refuses the erasure and deletes nothing. So does the transaction
    /// itself, on what only it can see: an account that became solely this user's after the set below was read, and a
    /// job claimed between the wait and the lock the transaction takes over those accounts' job rows. All three answer
    /// the caller identically, because all three leave the deployment exactly as it was. The user is therefore
    /// <em>withheld</em> rather than let go of until the deletion has actually committed: refusing to
    /// erase somebody is not a reason to stop serving them, and putting them back is what a refusal does.
    /// </para>
    /// <para>
    /// Whether the user was served is read before any of that, because the answer must describe the deployment the
    /// caller asked about rather than the one the erasure left.
    /// </para>
    /// </remarks>
    internal async Task<UserRosterErasureOutcome> EraseAsync(UserId user, CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user is erased for a named user.", nameof(user));
        }

        authorization.RequirePermission(MailFathomPermission.AdminErase);

        var solelyAssigned = await accounts.ReadSolelyAssignedAsync(user, cancellationToken);
        MailAccountId[] solelyAssignedAccounts = [.. solelyAssigned.Select(static account => MailAccountId.Create(account.ToString("D")))];
        var served = await this.IsServedAsync(user, cancellationToken);
        var erased = false;
        Guid? unquiesced = null;
        string? refusal;

        using (var withheld = servedUsers.Withhold(user))
        {
            using var unsupervised = withheldAccounts.Withhold(solelyAssignedAccounts);

            refusal = await quiescing.RunQuiescedAsync(
                solelyAssignedAccounts,
                async token =>
                {
                    var outcome = await erasure.EraseAsync(user, solelyAssigned, token);

                    erased = outcome.UserErased;
                    unquiesced = outcome.UnquiescedAccount;
                },
                cancellationToken);

            if (erased)
            {
                withheld.Erased();
                this.LogUserErased(served);
            }
        }

        if (refusal is { } stillRunning)
        {
            this.LogUserErasureRefused();

            return UserRosterErasureOutcome.Refused(stillRunning);
        }

        // The transaction refused on what only it could see: an account that became solely this user's after the set
        // above was read, or a job claimed between the wait and the lock the transaction takes. Nothing was written,
        // so it is the same answer to the caller as a wait that ran out.
        if (unquiesced is { } accountStillBusy)
        {
            this.LogUserErasureRefused();

            return UserRosterErasureOutcome.Refused(
                $"Mail account {accountStillBusy:D} was still being worked on when the erasure reached it, so nothing was erased. Ask again once that has ended.");
        }

        if (erased)
        {
            await this.RecountAndAnnounceAsync(cancellationToken);
        }

        return new UserRosterErasureOutcome(erased, served);
    }

    /// <summary>Counts the users again after a committed write and tells every other replica about it, whatever the count does.</summary>
    /// <remarks>
    /// The write is committed whether or not the count can be read, so a count that fails leaves the caller answered with
    /// what was committed rather than with a failure a retry would then refuse, and this replica's next comparison counts
    /// the rows like every other replica's. The announcement goes out either way, so no replica waits a whole interval to
    /// learn of a write that did happen.
    /// </remarks>
    private async Task RecountAndAnnounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await servedUsers.CountAsync(cancellationToken);
        }
        catch (UserSettingsUnreadableException)
        {
            // Answered by the remarks above: the committed write is the answer, and the next comparison recounts.
        }
        finally
        {
            await announcements.AnnounceAsync();
        }
    }

    /// <summary>Reads which users of one page this replica serves, in one reading for the page.</summary>
    /// <remarks>Rows that cannot be read answer nobody as served rather than failing the listing, since nothing can be served from them either.</remarks>
    private async Task<IReadOnlySet<UserId>> ReadServedAmongAsync(UserId[] users, CancellationToken cancellationToken)
    {
        try
        {
            return await servedUsers.ReadServedAmongAsync(users, cancellationToken);
        }
        catch (UserSettingsUnreadableException)
        {
            return new HashSet<UserId>();
        }
    }

    /// <summary>Reads whether this deployment serves one user, which is whether their record composes.</summary>
    /// <remarks>A record that cannot be read at all is answered as not served rather than failing the act that asked, since nothing can be served from it either.</remarks>
    private async Task<bool> IsServedAsync(UserId user, CancellationToken cancellationToken)
    {
        try
        {
            return await servedUsers.ReadAsync(user, cancellationToken) is not null;
        }
        catch (UserSettingsUnreadableException)
        {
            return false;
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

        return label.Length > UserRecord.MaximumDisplayNameLength
            ? $"The label is {label.Length} characters, past the {UserRecord.MaximumDisplayNameLength} a user's label is stored as. Shorten it."
            : null;
    }

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
        Message = "A user and everything recorded for them were erased. This process was serving them: {WasServed}. No replica serves them any more.")]
    private partial void LogUserErased(bool wasServed);

    /// <remarks>
    /// Named neither by user nor by account, for the reason the line above is: what an operator acts on came back in
    /// the refusal itself, and this records that the deployment declined to erase rather than failing to. It names no
    /// cause either, because every refusal reaches it — a wait that ran out of its bound, an account whose assignments
    /// changed under the request, a job claimed between the wait and the lock, and a supervision hold lost part-way —
    /// and the sentence the caller was answered with is what says which.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "An erasure was refused because work bound to the user's own mail accounts was still in flight. Nothing was erased, and this deployment goes on serving them.")]
    private partial void LogUserErasureRefused();
}
