// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Application.Access;
using MailFathom.Application.Paging;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Signals;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What is done to the mail accounts a deployment holds, by an administrator or by a user assigned one.</summary>
/// <remarks>
/// <para>
/// An account is a record of its own, assigned to one user or several, and every rule about a user's mailboxes is a rule
/// over the set that user is served. So a change to an account is judged once per user it reaches: the account as it
/// would stand is composed into each assigned user's record and put through the binder a start reads that record with,
/// which is what refuses a display name two of one user's accounts would share.
/// </para>
/// <para>
/// An administrator names the account by the identifier this deployment generated and names users by theirs, and is the
/// only caller that assigns an account to a user. An account of an organization is served to as many of its members
/// as an administrator assigns it to: the mail graph is keyed by the account alone, so one mailbox is synchronized
/// once, holds one copy of its mail, and applies each rule and mutation once, whoever reads it. An account in no
/// organization is one person's, and a second assignment of it is refused. A user adds an account for themselves
/// alone, withdraws one of their own, and changes the folders of one — and every one of those writes states the version
/// of their own record, which is what every account write moves, so the version a client already holds is the one it
/// composes against.
/// </para>
/// <para>
/// An address is held by one account in the whole deployment. An administrator told the address is held is told so by
/// name. A user is refused with one sentence whether the address is held, whoever holds it, or is not an address at all,
/// so the refusal never says whose mailbox it is nor tells a held address from an unusable one; a free, usable address
/// commits.
/// </para>
/// <para>
/// An account is governed by the settings policy of the deployment and of the organization it stands in. So a change
/// to one is judged against those before anything else: a property a policy forces is refused to everybody, a
/// property the editing restriction withholds is refused to a person changing a mailbox assigned to them and to
/// nobody administering it, and a held property stated as a read showed it is carried through and leaves the account
/// stating what it stated. What is then bound, and what every rule here is asked of, is the account as those policies
/// resolve it — so a declaration may leave out what a default supplies, and a combination a declaration would be
/// refused for is refused however its halves were supplied. The account's own document keeps only what was stated
/// on it.
/// </para>
/// <para>
/// A write is judged twice where a policy moved in between: once under the policies the account was read with,
/// because the reads of the users it reaches and of the secret stores may not be made inside a commit, and once more
/// inside the commit, under the policies in force at that moment, which is the judgement that decides and the one the
/// account's queryable settings are derived under.
/// </para>
/// <para>
/// A committed write is served by comparing the users this replica holds against the rows, which recomposes every user
/// whose record version the write moved, and then announced so every other replica compares too.
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class MailAccountAdministration(
    AccessAuthorization authorization,
    IAdministrativeTargets targets,
    IUserSettingsDocumentReader documents,
    IMailAccountRecordStore accounts,
    UserAccountDocumentBinder binder,
    SecretConfigurationValidator secrets,
    ServedUsers servedUsers,
    ConfigurationChangeAnnouncements announcements)
{
    /// <summary>What a save refused over a redaction marker it cannot place is sent to.</summary>
    private const string NarrowerChange = "state the setting afresh rather than leaving the redaction marker in its place.";

    /// <summary>The path a refusal about an account's credentials names, which is the declaration rather than a file.</summary>
    private const string DeclarationPath = "declaration";

    /// <summary>How many times a folder declaration is composed afresh over a record another write moved on.</summary>
    /// <remarks>Bounded rather than persistent, because losing three times in a row is contention nothing here resolves by trying a fourth: the act is reported as superseded and the person asks again.</remarks>
    private const int DeclarationWriteAttempts = 3;

    /// <summary>The one property of an account the folder routes write, which a person changes after the account is declared.</summary>
    private const string FoldersProperty = nameof(MailSynchronizationAccountOptions.Folders);

    /// <summary>The document of an account that states nothing, which a creation is judged against.</summary>
    private const string StatingNothing = "{}";

    /// <summary>Lists one page of the accounts this deployment holds.</summary>
    /// <param name="query">The page asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page's accounts the caller's scope covers, in identifier order, and where the following page continues.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <remarks>The settings are not parsed, so one unreadable row never hides every other account. A listing never refuses on scope: it answers with the accounts the scopes the caller holds the read at cover.</remarks>
    internal Task<AdministrativeListingPage<MailAccountSummary>> ReadPageAsync(
        AdministrativeListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRead);

        return accounts.ReadPageAsync(query, authorization.ScopesOf(MailFathomPermission.AdminRead), cancellationToken);
    }

    /// <summary>Reads one account.</summary>
    /// <param name="accountId">The account asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The account, redacted, or <see langword="null" /> when this deployment holds none under that identifier or the caller's scope does not cover it.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminRead" /> at every scope.</exception>
    /// <remarks>
    /// The declaration answered stays the account's own, because that is what a save is composed over and what a
    /// lifted force brings back. What the account is served with is answered beside it, per property.
    /// </remarks>
    internal async Task<MailAccountReading?> ReadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRead);

        if (await accounts.ReadAsync(accountId, cancellationToken) is not { } holding
            || !authorization.PermitsOver(MailFathomPermission.AdminRead, TargetOf(holding)))
        {
            return null;
        }

        var writes = authorization.PermitsOver(MailFathomPermission.AdminConfigurationWrite, TargetOf(holding));

        return new MailAccountReading(
            holding.Account.Id,
            SettingRedaction.ApplyToDocument(MailAccountDeclaration.Of(holding.Account)),
            holding.Account.Version,
            holding.Users,
            holding.OrganizationId)
        {
            Policy = PolicyReadingOf(holding.Account, property => writes && property.IsAnAdministratorsToChange),
        };
    }

    /// <summary>Reads how the settings policies governing the signed-in user's mail accounts resolve each of them.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every account assigned to the user as it is served, and what an account they declared now would start from; or <see langword="null" /> when this deployment holds no record for the acting user.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// A property of an account already declared is the reader's to change only where a route of theirs writes it,
    /// which is the list of folders and nothing else; every property is stated when an account is declared, so there
    /// it is every property no policy holds against them. Both need <see cref="MailFathomPermission.MailAccountsWrite" />,
    /// so a reader without it is told nothing is theirs to change rather than offered a control whose change is refused.
    /// </remarks>
    internal async Task<OwnMailAccountsPolicyReading?> ReadOwnAccountPoliciesAsync(CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailRead);

        if (await documents.ReadAsync(authorization.RequireUser(), cancellationToken) is not { } record)
        {
            return null;
        }

        var writes = authorization.Permits(MailFathomPermission.MailAccountsWrite);

        return new OwnMailAccountsPolicyReading(
            record.MailAccounts.ToDictionary(
                account => account.Id,
                account => PolicyReadingOf(
                    account,
                    property => writes && property.IsThePersonsToChange && property.Property.Path == FoldersProperty)),
            PolicyReadingOf(
                new MailAccountRecord(Guid.Empty, EmailAddress: null, string.Empty, StatingNothing, Version: 0)
                {
                    Policies = record.Policies,
                },
                property => writes && property.IsThePersonsToChange));
    }

    /// <summary>Creates an account in the organization of the user it is created for, and assigns it to them.</summary>
    /// <param name="user">The user the account is created for.</param>
    /// <param name="declarationJson">The declaration.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>What the write did, or <see langword="null" /> when this deployment holds no such user or the caller's scope does not cover them.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declarationJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" /> at every scope, or holds it below the deployment alone for a user in no organization, since an account recorded in none is the deployment's to record.</exception>
    internal async Task<MailAccountCreation?> CreateAsync(
        UserId user,
        string declarationJson,
        CancellationToken cancellationToken)
    {
        RequireNamed(user);
        ArgumentNullException.ThrowIfNull(declarationJson);
        authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminConfigurationWrite);

        if (!await this.AdmitsNewAccountForAsync(user, cancellationToken)
            || await documents.ReadAsync(user, cancellationToken) is not { } record)
        {
            return null;
        }

        var outcome = await this.CreateForAsync(record, declarationJson, UserRecordAuthority.Administrator, cancellationToken);

        return new MailAccountCreation(outcome.AccountId, outcome.Outcome);
    }

    /// <summary>Saves an account's declaration as the difference between what it says and what the record holds.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="declarationJson">The declaration as the administrator saved it.</param>
    /// <param name="expectedVersion">The account version the declaration was read at.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>What the write did, its version the account's, or <see langword="null" /> when this deployment holds no such account or the caller's scope does not cover it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declarationJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" /> at every scope.</exception>
    /// <remarks>A value left at the redaction marker leaves the reference beneath it as it was, exactly as a saved user record does.</remarks>
    internal async Task<UserRecordWriteOutcome?> SaveAsync(
        Guid accountId,
        string declarationJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declarationJson);
        authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminConfigurationWrite);

        if (await accounts.ReadAsync(accountId, cancellationToken) is not { } holding
            || !authorization.PermitsOver(MailFathomPermission.AdminConfigurationWrite, TargetOf(holding)))
        {
            return null;
        }

        var standing = holding.Account;

        if (standing.Version != expectedVersion)
        {
            return Superseded(expectedVersion, standing.Version, "mail account");
        }

        MailAccountRecord candidate;

        try
        {
            var standingDeclaration = MailAccountDeclaration.Of(standing);
            var standingSettings = RedactedDocumentSave.Flatten(standingDeclaration);
            var savedSettings = RedactedDocumentSave.Flatten(declarationJson);
            var unplaceable = RedactedDocumentSave.FindMarkersTheSaveCannotPlace(standingSettings, savedSettings, NarrowerChange);

            if (unplaceable.Count > 0)
            {
                return UserRecordWriteOutcome.Refused(MailFathomErrorCode.ConfigurationCandidateInvalid, standing.Version, unplaceable);
            }

            var edits = RedactedDocumentSave.DifferenceBetween(standingSettings, savedSettings);

            if (edits.Count == 0)
            {
                return UserRecordWriteOutcome.NothingToChange(
                    standing.Version,
                    $"The saved declaration states the account as it already stands, so nothing was written and version {standing.Version} stays in force.");
            }

            var declaration = MailAccountDeclaration.Read(SettingsDocumentPatch.Apply(standingDeclaration, edits));

            candidate = standing with
            {
                EmailAddress = declaration.EmailAddress,
                DisplayName = declaration.DisplayName,
                Document = declaration.Document,
            };
        }
        catch (Exception refused)
            when (refused is FormatException or JsonException or InvalidDataException or ArgumentException)
        {
            return NotADeclaration(standing.Version, refused);
        }

        var opened = await this.JudgeAsync(
            standing,
            candidate,
            holding.Users,
            actingUser: null,
            standing.Version,
            standing.Policies,
            cancellationToken);

        if (opened.Write is not { } pending)
        {
            return opened.Refusal;
        }

        var write = await accounts.SaveAsync(accountId, expectedVersion, pending.Compose, cancellationToken);

        return write.Result switch
        {
            MailAccountWriteResult.NotFound => null,
            MailAccountWriteResult.Committed => await this.ServedAsync(
                UserRecordWriteOutcome.Committed(write.Version, pending.StandingProblems),
                cancellationToken),
            MailAccountWriteResult.NothingComposed => pending.CommittedAs.Refusal ?? NothingHeldMoves(standing.Version),
            MailAccountWriteResult.AddressHeld => AddressHeldForAdministrator(write.Version, candidate.EmailAddress),
            _ => Superseded(standing.Version, write.Version, "mail account"),
        };
    }

    /// <summary>Assigns an account to a user: beside the other members it is assigned to where it belongs to an organization, and to nobody else where it belongs to none.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="user">The user it is assigned to.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>What the write did, its version the account's, or <see langword="null" /> when this deployment holds no such account or user, or the caller's scope does not cover both.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" /> at every scope.</exception>
    /// <remarks>
    /// An account of an organization that another member is already assigned is assigned again rather than refused,
    /// which is what serving one mailbox to several people is. An account in no organization is one person's, so a
    /// second assignment of it is refused naming the way to share it — here from what was read, and again by the store
    /// under the account's lock, which is what answers the later of two assignments landing at once. The account is
    /// judged against this user's own set, so an account whose display
    /// name one of their accounts already carries is refused rather than served under a name that no longer tells two
    /// apart — a judgement the other assignees' sets are unaffected by. An assignment across two organizations is
    /// refused naming both, counting none as one of them, and is decided by the store under both rows' locks rather
    /// than here, because a move of either side may land between a read here and the write. An account already
    /// assigned to <see cref="MailAccountRecord.MaximumUsersAssigned" /> users is refused, here and again by the store
    /// under the account's lock, because every read of who the account reaches stops there.
    /// </remarks>
    internal async Task<UserRecordWriteOutcome?> AssignAsync(
        Guid accountId,
        UserId user,
        CancellationToken cancellationToken)
    {
        RequireNamed(user);
        authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminConfigurationWrite);

        if (await accounts.ReadAsync(accountId, cancellationToken) is not { } holding
            || !authorization.PermitsOver(MailFathomPermission.AdminConfigurationWrite, TargetOf(holding))
            || !await authorization.PermitsOverAsync(MailFathomPermission.AdminConfigurationWrite, user, cancellationToken)
            || await documents.ReadAsync(user, cancellationToken) is not { } record)
        {
            return null;
        }

        var account = holding.Account;

        if (holding.Users.Contains(user))
        {
            return UserRecordWriteOutcome.NothingToChange(
                account.Version,
                "The account is already assigned to this user, so nothing was written.");
        }

        if (holding.Users.Count >= MailAccountRecord.MaximumUsersAssignedIn(holding.OrganizationId))
        {
            return holding.OrganizationId is null
                ? SharedOnlyInOrganization(account.Version)
                : AssignedToMostUsers(account.Version);
        }

        // Judged as the account stands, so nothing of it is held against anybody: what is asked is whether this user's
        // set still binds with the account in it.
        var opened = await this.JudgeAsync(
            account,
            account,
            [user],
            actingUser: null,
            account.Version,
            account.Policies,
            cancellationToken);

        if (opened.Write is not { } judgement)
        {
            return opened.Refusal;
        }

        var write = await accounts.AssignAsync(accountId, user, record.Version, cancellationToken);

        return write.Result switch
        {
            MailAccountWriteResult.NotFound => null,
            MailAccountWriteResult.Committed => await this.ServedAsync(
                UserRecordWriteOutcome.Committed(account.Version, judgement.StandingProblems),
                cancellationToken),
            MailAccountWriteResult.NothingToChange => UserRecordWriteOutcome.NothingToChange(
                account.Version,
                "The account is already assigned to this user, so nothing was written."),
            MailAccountWriteResult.OrganizationsDiffer => StraddlesOrganizations(account.Version, write.Straddled!),
            MailAccountWriteResult.AssignedToMostUsers => AssignedToMostUsers(account.Version),
            MailAccountWriteResult.SharedOnlyInOrganization => SharedOnlyInOrganization(account.Version),
            _ => Superseded(record.Version, await this.VersionOfAsync(record, cancellationToken), "user record"),
        };
    }

    /// <summary>Ends one user's assignment to an account, erasing the account and its mail when nobody else is assigned it.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="user">The user whose assignment ends.</param>
    /// <param name="cancellationToken">Cancels the write before it commits.</param>
    /// <returns>What the write did, which is nothing where the caller's scope does not cover both the account and the user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminErase" /> at every scope.</exception>
    /// <remarks>The erasure grant rather than the configuration write, because ending the last assignment disposes of every message the deployment holds for the mailbox.</remarks>
    internal async Task<MailAccountUnassignment> UnassignAsync(
        Guid accountId,
        UserId user,
        CancellationToken cancellationToken)
    {
        RequireNamed(user);
        authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminErase);

        if (!authorization.PermitsOver(MailFathomPermission.AdminErase, await this.ReadTargetAsync(accountId, cancellationToken))
            || !await authorization.PermitsOverAsync(MailFathomPermission.AdminErase, user, cancellationToken))
        {
            return new MailAccountUnassignment(Unassigned: false, AccountErased: false);
        }

        var unassignment = await accounts.UnassignAsync(accountId, user, cancellationToken);

        if (unassignment.Unassigned)
        {
            await this.PublishAsync(cancellationToken);
        }

        return unassignment;
    }

    /// <summary>Erases an account, every assignment to it, and everything stored for it.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="cancellationToken">Cancels the erasure before it commits.</param>
    /// <returns>Whether an account the caller's scope covers was there to erase.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminErase" /> at every scope.</exception>
    internal async Task<bool> EraseAsync(Guid accountId, CancellationToken cancellationToken)
    {
        authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminErase);

        if (!authorization.PermitsOver(MailFathomPermission.AdminErase, await this.ReadTargetAsync(accountId, cancellationToken)))
        {
            return false;
        }

        var erased = await accounts.EraseAsync(accountId, cancellationToken);

        if (erased)
        {
            await this.PublishAsync(cancellationToken);
        }

        return erased;
    }

    /// <summary>Creates an account for the signed-in user alone.</summary>
    /// <param name="declarationJson">The declaration.</param>
    /// <param name="expectedVersion">The version of the user's own record the change was composed over.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>What the write did, its version the user's record's, or <see langword="null" /> when this deployment holds no record for the acting user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declarationJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailAccountsWrite" />.</exception>
    internal async Task<UserRecordWriteOutcome?> AddOwnAsync(
        string declarationJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declarationJson);
        authorization.RequirePermission(MailFathomPermission.MailAccountsWrite);

        if (await this.OpenOwnAsync(expectedVersion, cancellationToken) is not { } opened)
        {
            return null;
        }

        return opened.Refusal ?? (await this.CreateForAsync(
            opened.Record,
            declarationJson,
            UserRecordAuthority.User,
            cancellationToken)).Outcome;
    }

    /// <summary>Ends the signed-in user's assignment to one of their accounts.</summary>
    /// <param name="accountId">The account's identifier, as the record the user was served names it.</param>
    /// <param name="expectedVersion">The version of the user's own record the change was composed over.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>What the write did, its version the user's record's, or <see langword="null" /> when this deployment holds no record for the acting user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailAccountsWrite" />.</exception>
    /// <remarks>
    /// Every removal ends the caller's assignment, and erases the account and every message, folder, and attachment
    /// stored for it only where nobody else is assigned it — a mailbox somebody else still reads keeps its mail, and
    /// what goes with the departing user is what they alone could read there: their drafts and their recurring sends.
    /// It stays under
    /// <see cref="MailFathomPermission.MailAccountsWrite" /> rather than <see cref="MailFathomPermission.AdminErase" />
    /// because a person removing their own mailbox disposes of their own data, unlike an administrator unassigning
    /// somebody else's.
    /// </remarks>
    internal async Task<UserRecordWriteOutcome?> RemoveOwnAsync(
        string accountId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        authorization.RequirePermission(MailFathomPermission.MailAccountsWrite);

        if (await this.OpenOwnAsync(expectedVersion, cancellationToken) is not { } opened)
        {
            return null;
        }

        if (opened.Refusal is { } refusal)
        {
            return refusal;
        }

        if (AssignedAccount(opened.Record, accountId) is not { } account)
        {
            return NotAssigned(opened.Record.Version, accountId);
        }

        var unassignment = await accounts.UnassignAsync(account.Id, opened.Record.User, cancellationToken);

        if (!unassignment.Unassigned)
        {
            return NotAssigned(opened.Record.Version, accountId);
        }

        await this.PublishAsync(cancellationToken);

        return UserRecordWriteOutcome.Committed(await this.VersionOfAsync(opened.Record, cancellationToken));
    }

    /// <summary>Declares one more folder in one of the signed-in user's accounts.</summary>
    /// <param name="accountId">The account's identifier.</param>
    /// <param name="folderJson">The folder, as the JSON object a file would have written.</param>
    /// <param name="expectedVersion">The version of the user's own record the change was composed over.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>What the write did, its version the user's record's, or <see langword="null" /> when this deployment holds no record for the acting user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folderJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailAccountsWrite" />.</exception>
    /// <remarks>
    /// <para>
    /// The folder acts carry the grant an account's own settings carry rather than one of their own: a folder names a
    /// path on that account's server and decides what is mirrored from it, so a deployment that lets somebody state
    /// their own mailboxes has already let them state what is read out of one.
    /// </para>
    /// <para>
    /// Part of what they may state is fixed, and <see cref="MailAccountFolderComposition" /> is where those rules are:
    /// the synchronization switches, the role a folder plays, and the name a folder playing one carries. They hold on
    /// all three folder acts and on a user's own account declaration, and on nothing an administrator states.
    /// </para>
    /// </remarks>
    internal Task<UserRecordWriteOutcome?> AddOwnFolderAsync(
        string accountId,
        string folderJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(folderJson);
        authorization.RequirePermission(MailFathomPermission.MailAccountsWrite);

        return this.ChangeOwnFolderAsync(
            accountId,
            expectedVersion,
            document => MailAccountFolderComposition.WithFolderAdded(document, folderJson),
            unmatched: null,
            cancellationToken);
    }

    /// <summary>States one folder of the signed-in user's afresh, in place of the one carrying an alias.</summary>
    /// <param name="accountId">The account's identifier.</param>
    /// <param name="alias">The alias the folder being changed is declared under.</param>
    /// <param name="folderJson">The folder as it is to stand.</param>
    /// <param name="expectedVersion">The version of the user's own record the change was composed over.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>What the write did, its version the user's record's, or <see langword="null" /> when this deployment holds no record for the acting user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> or <paramref name="alias" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folderJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailAccountsWrite" />.</exception>
    internal Task<UserRecordWriteOutcome?> ReplaceOwnFolderAsync(
        string accountId,
        string alias,
        string folderJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentNullException.ThrowIfNull(folderJson);
        authorization.RequirePermission(MailFathomPermission.MailAccountsWrite);

        return this.ChangeOwnFolderAsync(
            accountId,
            expectedVersion,
            document => MailAccountFolderComposition.WithFolderReplaced(document, alias, folderJson),
            $"This mail account declares no folder '{alias}'.",
            cancellationToken);
    }

    /// <summary>Withdraws one folder from one of the signed-in user's accounts.</summary>
    /// <param name="accountId">The account's identifier.</param>
    /// <param name="alias">The alias the folder being withdrawn is declared under.</param>
    /// <param name="expectedVersion">The version of the user's own record the change was composed over.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>What the write did, its version the user's record's, or <see langword="null" /> when this deployment holds no record for the acting user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> or <paramref name="alias" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailAccountsWrite" />.</exception>
    /// <remarks>The mail already stored out of that folder stays: what this does is stop the deployment reading the folder.</remarks>
    internal Task<UserRecordWriteOutcome?> RemoveOwnFolderAsync(
        string accountId,
        string alias,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        authorization.RequirePermission(MailFathomPermission.MailAccountsWrite);

        return this.ChangeOwnFolderAsync(
            accountId,
            expectedVersion,
            document => MailAccountFolderComposition.WithFolderRemoved(document, alias),
            $"This mail account declares no folder '{alias}'.",
            cancellationToken);
    }

    /// <summary>Reads the aliases one of the signed-in user's accounts declares in the user's own record.</summary>
    /// <param name="accountId">The account's identifier.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The aliases as the record writes them, empty where the account declares none, where a settings policy holds its folders against the user, or where the record names no such account.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// What the folder-management surface may act on is what this user may state afresh. That is the list the account
    /// states, or the one a policy's default supplies where it states none — a list is one value, so a change to one
    /// folder of a default states the whole list on the account. Folders a policy forces, and folders the editing
    /// restriction withholds, are nobody's to act on here, and neither are those of an account a policy this build
    /// does not read governs.
    /// </remarks>
    internal async Task<IReadOnlyList<string>> AliasesOwnAccountDeclaresAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        authorization.RequirePermission(MailFathomPermission.MailRead);

        if (await documents.ReadAsync(authorization.RequireUser(), cancellationToken) is not { } record
            || AssignedAccount(record, accountId) is not { } account)
        {
            return [];
        }

        if (MailAccountPolicyResolution.ResolveUnderItsPolicies(account)?.Find(FoldersProperty) is not { IsThePersonsToChange: true })
        {
            return [];
        }

        try
        {
            return MailAccountFolderComposition.AliasesIn(WithTheFoldersItIsServed(account));
        }
        catch (Exception unreadable) when (unreadable is FormatException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Writes one folder declaration of the signed-in user's, for an act the mail server has already carried out.</summary>
    /// <param name="accountId">The account's identifier.</param>
    /// <param name="compose">Composes the account's settings afresh out of the ones it holds.</param>
    /// <param name="unmatched">The sentence a change matching no folder is refused with, absent where the change always matches.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>What the write did, or <see langword="null" /> when this deployment holds no record for the acting user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="compose" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailFoldersWrite" />.</exception>
    /// <remarks>
    /// <para>
    /// It carries the grant the folder acts carry rather than the one an account's own settings carry, because the
    /// person asking has not stated a mailbox: they have asked for a folder, and this is the record of the folder the
    /// server made. The rules <see cref="MailAccountFolderComposition" /> fixes hold here exactly as they hold on the
    /// three routes an account's own settings are written through.
    /// </para>
    /// <para>
    /// The version is read here rather than supplied, because the act being recorded happened on the server before
    /// this was called and a refusal would leave a folder nothing declares. So a record another write moved on is read
    /// again and the change composed afresh over it, a bounded number of times; a write still losing after that is
    /// reported as superseded like any other.
    /// </para>
    /// </remarks>
    internal async Task<UserRecordWriteOutcome?> ChangeOwnFolderDeclarationAsync(
        string accountId,
        Func<string, MailAccountFolderChange> compose,
        string? unmatched,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(compose);
        authorization.RequirePermission(MailFathomPermission.MailFoldersWrite);

        UserRecordWriteOutcome? outcome = null;

        for (var attempt = 0; attempt < DeclarationWriteAttempts; attempt++)
        {
            if (await documents.ReadAsync(authorization.RequireUser(), cancellationToken) is not { } record)
            {
                return null;
            }

            outcome = await this.ChangeOwnFolderAsync(accountId, record.Version, compose, unmatched, cancellationToken);

            if (outcome?.Refusal != MailFathomErrorCode.ConfigurationVersionSuperseded)
            {
                return outcome;
            }
        }

        return outcome;
    }

    /// <summary>Resolves one account through the policies it was read with, and says which of its properties are the reader's to change.</summary>
    private static MailAccountPolicyReading PolicyReadingOf(
        MailAccountRecord account,
        Func<ResolvedProperty, bool> isTheReadersToChange)
    {
        if (MailAccountPolicyResolution.LayersOf(account, out var unreadable) is not { } layers)
        {
            return new MailAccountPolicyReading([], [], SettingsPolicyLayers.DescribeUnreadable(unreadable));
        }

        var properties = MailAccountPolicyResolution.Resolve(account, layers).Properties;

        return new MailAccountPolicyReading(
            properties,
            [.. properties.Where(isTheReadersToChange).Select(property => property.Property.Path)],
            Unresolved: null);
    }

    /// <summary>Composes the settings a folder change is made over: the account's own, with the folders it is served standing where it states none.</summary>
    /// <remarks>
    /// A list is one value across the layers of a policy, so an account that states no folders and is served a
    /// default's has a whole list to change one folder of. Composing the change over the account's own document would
    /// state a list holding that one folder and drop every other the person was looking at.
    /// </remarks>
    private static string WithTheFoldersItIsServed(MailAccountRecord account)
    {
        if (MailAccountPolicyResolution.ResolveUnderItsPolicies(account)?.Find(FoldersProperty)
                is not { EffectiveValue: { } served } folders
            || folders.Source == SettingsValueSource.Record
            || GovernedRecordResolution.ReadDocument(account.Document) is not { } document)
        {
            return account.Document;
        }

        SettingsDocumentTree.Replace(document, FoldersProperty, served);

        return document.ToJsonString();
    }

    /// <summary>States an account as a judged declaration leaves it: the name its row holds, and the settings beside it.</summary>
    private static MailAccountRecord WithDeclaration(MailAccountRecord account, string declarationJson)
    {
        if (GovernedRecordResolution.ReadDocument(declarationJson) is not { } declaration)
        {
            return account;
        }

        var displayName = SettingsDocumentTree.Find(declaration, MailAccountRecordComposition.DisplayNameProperty) is JsonValue stated
            && stated.TryGetValue<string>(out var name)
                ? name
                : account.DisplayName;

        MailAccountRecordComposition.RemoveEverySpelling(declaration, MailAccountRecordComposition.DisplayNameProperty);

        return account with { DisplayName = displayName, Document = declaration.ToJsonString() };
    }

    /// <summary>Reports whether an account as it would stand states exactly what it states now.</summary>
    private static bool StatesWhatStands(MailAccountRecord standing, MailAccountRecord candidate) =>
        string.Equals(standing.EmailAddress, candidate.EmailAddress, StringComparison.Ordinal)
        && string.Equals(standing.DisplayName, candidate.DisplayName, StringComparison.Ordinal)
        && SettingsDocumentTree.StateTheSame(
            GovernedRecordResolution.ReadDocument(standing.Document),
            GovernedRecordResolution.ReadDocument(candidate.Document));

    /// <summary>Says why a change to an account's address is refused, where it is.</summary>
    /// <remarks>
    /// The address is a column no declaration document states, so the judgement of a document cannot reach it. A
    /// policy takes no value for it and says only who may change it, which binds a person stating the address of a
    /// mailbox they declare and nobody administering one.
    /// </remarks>
    private static IEnumerable<string> FindAddressRefusal(
        SettingsPolicyLayers layers,
        MailAccountRecord? standing,
        MailAccountRecord stated,
        bool byThePerson) =>
        string.Equals(standing?.EmailAddress, stated.EmailAddress, StringComparison.OrdinalIgnoreCase)
        || GovernedRecordResolution.Resolve(SettingsPolicySection.MailAccounts, layers, StatingNothing)
            .Find(MailAccountRecordComposition.EmailAddressProperty)
            .FindRefusal(byThePerson) is not { } refusal
            ? []
            : [refusal];

    /// <summary>Answers a change every part of which is held where it stands.</summary>
    private static UserRecordWriteOutcome NothingHeldMoves(long version) =>
        UserRecordWriteOutcome.NothingToChange(
            version,
            $"Every setting the change names is held where it stands, so nothing was written and version {version} stays in force.");

    private static MailAccountRecord? AssignedAccount(UserSettingsDocument record, string accountId) =>
        Guid.TryParse(accountId, out var id) ? record.MailAccounts.FirstOrDefault(account => account.Id == id) : null;

    /// <summary>Reports whether a candidate carries every secret-bearing setting exactly as the account holds it.</summary>
    /// <remarks>Compared as settings rather than as the problems the walk reports, because a sentence names a path and a failure and never the target behind it: a broken reference replaced by a different broken one reads the same.</remarks>
    private static bool LeavesEverySecretAsItWas(string standingDocument, string candidateDocument)
    {
        var standing = SecretsOf(standingDocument);
        var candidate = SecretsOf(candidateDocument);

        return standing.Count == candidate.Count
            && standing.All(setting => candidate.TryGetValue(setting.Key, out var value)
                && string.Equals(value, setting.Value, StringComparison.Ordinal));
    }

    private static Dictionary<string, string> SecretsOf(string document) =>
        RedactedDocumentSave.Flatten(document)
            .Where(setting => SecretPropertyNaming.NamesASecret(setting.Key.Split(':')[^1]))
            .ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<MailAccountRecord> WithCandidate(
        IReadOnlyList<MailAccountRecord> assigned,
        MailAccountRecord candidate) =>
        assigned.Any(account => account.Id == candidate.Id)
            ? [.. assigned.Select(account => account.Id == candidate.Id ? candidate : account)]
            : [.. assigned, candidate];

    private static UserRecordWriteOutcome Superseded(long composedOver, long inForce, string what) =>
        UserRecordWriteOutcome.Refused(
            MailFathomErrorCode.ConfigurationVersionSuperseded,
            inForce,
            [
                $"The change was composed over {what} version {composedOver}, and version {inForce} is in force. Read it as it now stands and decide again against it.",
            ]);

    private static UserRecordWriteOutcome NotADeclaration(long version, Exception refused) =>
        UserRecordWriteOutcome.Refused(
            MailFathomErrorCode.ConfigurationCandidateInvalid,
            version,
            [$"The mail account is not a declaration this deployment can persist, so nothing was written: {refused.Message}"]);

    private static UserRecordWriteOutcome NotAssigned(long version, string accountId) =>
        UserRecordWriteOutcome.Refused(
            MailFathomErrorCode.ConfigurationCandidateInvalid,
            version,
            [$"You are assigned no mail account '{accountId}'. Read your mail accounts to see the identifiers they are served under."]);

    /// <summary>The refusal an administrator receives for an assignment that would straddle two organizations.</summary>
    /// <remarks>Both organizations are named, "none" among them, so the administrator knows which side to move rather than only that the two disagree.</remarks>
    private static UserRecordWriteOutcome StraddlesOrganizations(long version, StraddledOrganizations straddled) =>
        UserRecordWriteOutcome.Refused(
            MailFathomErrorCode.ConfigurationCandidateInvalid,
            version,
            [
                $"The mail account belongs to {OrganizationNamed(straddled.AccountOrganization)} and the user to "
                + $"{OrganizationNamed(straddled.UserOrganization)}. An account is assigned only to a user of its own "
                + "organization, and one in none only to a user in none, so nothing was written. Move the account or the "
                + "user first.",
            ]);

    private static UserRecordWriteOutcome AssignedToMostUsers(long version) =>
        UserRecordWriteOutcome.Refused(
            MailFathomErrorCode.ConfigurationCandidateInvalid,
            version,
            [$"A mail account is assigned to at most {MailAccountRecord.MaximumUsersAssigned} users, and this one already is, so nothing was written."]);

    /// <summary>The refusal an administrator receives for a second assignment of an account in no organization.</summary>
    /// <remarks>It names the way to share the mailbox, because the rule refuses the sharing and never the mailbox: the same account takes any number of an organization's members once it belongs to one.</remarks>
    private static UserRecordWriteOutcome SharedOnlyInOrganization(long version) =>
        UserRecordWriteOutcome.Refused(
            MailFathomErrorCode.ConfigurationCandidateInvalid,
            version,
            [
                "The mail account belongs to no organization and is already assigned to a user. An account in no "
                + "organization is assigned to one person, so nothing was written. To share it, move the account and "
                + "the users it is shared between into an organization first.",
            ]);

    private static string OrganizationNamed(Guid? organizationId) =>
        organizationId is { } named ? $"organization '{named:D}'" : "no organization";

    private static UserRecordWriteOutcome AddressHeldForAdministrator(long version, string? emailAddress) =>
        UserRecordWriteOutcome.Refused(
            MailFathomErrorCode.ConfigurationCandidateInvalid,
            version,
            [
                $"Another mail account already holds '{emailAddress}', and one address is held by one account in this deployment, so nothing was written.",
            ]);

    /// <summary>The refusal a user receives for an address somebody's account already holds, or one that is not an address at all.</summary>
    /// <remarks>The same sentence for both, so the answer never tells a held address from an unusable one, and never names whose mailbox a held address is.</remarks>
    private static UserRecordWriteOutcome AddressRefusedForUser(long version) =>
        UserRecordWriteOutcome.Refused(
            MailFathomErrorCode.ConfigurationCandidateInvalid,
            version,
            ["This mail account cannot be added for you. Ask whoever administers this deployment to add it."]);

    private static AdministrativeTarget TargetOf(MailAccountHolding holding) =>
        AdministrativeTarget.MailAccount(holding.OrganizationId, holding.Users);

    /// <summary>Decides whether the caller may record an account for one user, which places it in that user's organization.</summary>
    /// <remarks>
    /// The account a creation records is assigned to the user alone and sits in their organization, so it is covered
    /// wherever the user is. Recording one in no organization is the deployment's alone, so a user in none is refused
    /// naming the permission to a caller holding it only below the deployment — that user is already in the caller's
    /// scope, so the refusal discloses nothing about them.
    /// </remarks>
    private async Task<bool> AdmitsNewAccountForAsync(UserId user, CancellationToken cancellationToken)
    {
        if (authorization.Permits(MailFathomPermission.AdminConfigurationWrite))
        {
            return true;
        }

        var owner = await targets.PlaceUserAsync(user, cancellationToken);

        if (!authorization.PermitsOver(MailFathomPermission.AdminConfigurationWrite, owner))
        {
            return false;
        }

        if (owner.Organization is null)
        {
            authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);
        }

        return true;
    }

    private async Task<AdministrativeTarget> ReadTargetAsync(Guid accountId, CancellationToken cancellationToken) =>
        await accounts.ReadAsync(accountId, cancellationToken) is { } holding ? TargetOf(holding) : AdministrativeTarget.Unplaced;

    private static void RequireNamed(UserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A mail account is assigned to a named user.", nameof(user));
        }
    }

    /// <summary>Creates an account from a declaration and assigns it to the user whose record it was judged against.</summary>
    /// <remarks>
    /// The refusal version is the user's record's, because it is the only version the caller composed against: an
    /// account that was never created has none of its own. A committed account reports the version the caller composes
    /// its next change over — the account's to an administrator, the user's record's to the user.
    /// </remarks>
    private async Task<(Guid? AccountId, UserRecordWriteOutcome Outcome)> CreateForAsync(
        UserSettingsDocument record,
        string declarationJson,
        UserRecordAuthority authority,
        CancellationToken cancellationToken)
    {
        MailAccountDeclaration declaration;

        try
        {
            declaration = authority == UserRecordAuthority.User
                ? MailAccountDeclaration.ReadWithAnyAddress(declarationJson)
                : MailAccountDeclaration.Read(declarationJson);
        }
        catch (Exception refused) when (refused is FormatException or JsonException)
        {
            return (null, NotADeclaration(record.Version, refused));
        }

        if (!MailAccountDeclaration.IsAnAddress(declaration.EmailAddress))
        {
            return (null, AddressRefusedForUser(record.Version));
        }

        var document = declaration.Document;

        // A declared account carries its folders, so a user declaring one reaches the folder routes' rules through it
        // rather than around them. An administrator states a whole account, which those rules do not bind.
        if (authority == UserRecordAuthority.User)
        {
            MailAccountFolderChange folders;

            try
            {
                folders = MailAccountFolderComposition.WithFoldersDeclared(document);
            }
            catch (Exception refused) when (refused is FormatException or JsonException)
            {
                return (null, NotADeclaration(record.Version, refused));
            }

            if (folders.Refusal is { } broken)
            {
                return (
                    null,
                    UserRecordWriteOutcome.Refused(MailFathomErrorCode.ConfigurationCandidateInvalid, record.Version, [broken]));
            }

            document = folders.Candidate ?? document;
        }

        var candidate = new MailAccountRecord(
            Guid.CreateVersion7(),
            declaration.EmailAddress,
            declaration.DisplayName,
            document,
            Version: 0);

        var opened = await this.JudgeAsync(
            standing: null,
            candidate,
            [record.User],
            authority == UserRecordAuthority.User ? record.User : null,
            record.Version,
            record.Policies,
            cancellationToken);

        if (opened.Write is not { } pending)
        {
            return (null, opened.Refusal!);
        }

        var write = await accounts.CreateAsync(record.User, record.Version, pending.Compose, cancellationToken);

        if (write.Result != MailAccountWriteResult.Committed)
        {
            var current = await this.VersionOfAsync(record, cancellationToken);

            return (null, write.Result switch
            {
                MailAccountWriteResult.AddressHeld when authority == UserRecordAuthority.User => AddressRefusedForUser(current),
                MailAccountWriteResult.AddressHeld => AddressHeldForAdministrator(current, candidate.EmailAddress),
                MailAccountWriteResult.NothingComposed when pending.CommittedAs.Refusal is { } refusedAtCommit => refusedAtCommit,
                _ => Superseded(record.Version, current, "user record"),
            });
        }

        var version = authority == UserRecordAuthority.User
            ? await this.VersionOfAsync(record, cancellationToken)
            : write.Version;

        return (candidate.Id, await this.ServedAsync(UserRecordWriteOutcome.Committed(version, pending.StandingProblems), cancellationToken));
    }

    /// <summary>Composes one folder change over one of the signed-in user's accounts, and saves the account it produced.</summary>
    /// <remarks>A change that matched no folder is refused with the sentence <c>unmatched</c> carries, which is absent only for an addition, since an addition always matches.</remarks>
    private async Task<UserRecordWriteOutcome?> ChangeOwnFolderAsync(
        string accountId,
        long expectedVersion,
        Func<string, MailAccountFolderChange> compose,
        string? unmatched,
        CancellationToken cancellationToken)
    {
        if (await this.OpenOwnAsync(expectedVersion, cancellationToken) is not { } opened)
        {
            return null;
        }

        if (opened.Refusal is { } refusal)
        {
            return refusal;
        }

        var record = opened.Record;

        if (AssignedAccount(record, accountId) is not { } account)
        {
            return NotAssigned(record.Version, accountId);
        }

        MailAccountFolderChange change;

        try
        {
            change = compose(WithTheFoldersItIsServed(account));
        }
        catch (Exception refused) when (refused is FormatException or JsonException)
        {
            return UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                record.Version,
                [$"The folder change is not one this deployment can compose over the mail account, so nothing was written: {refused.Message}"]);
        }

        if (change.Refusal is { } broken)
        {
            return UserRecordWriteOutcome.Refused(MailFathomErrorCode.ConfigurationCandidateInvalid, record.Version, [broken]);
        }

        if (change.Candidate is not { } candidateDocument)
        {
            return UserRecordWriteOutcome.Refused(MailFathomErrorCode.ConfigurationCandidateInvalid, record.Version, [unmatched!]);
        }

        var candidate = account with { Document = candidateDocument };
        var users = (await accounts.ReadAsync(account.Id, cancellationToken))?.Users ?? [record.User];
        var judged = await this.JudgeAsync(
            account,
            candidate,
            users,
            record.User,
            record.Version,
            account.Policies,
            cancellationToken);

        if (judged.Write is not { } pending)
        {
            return judged.Refusal;
        }

        var write = await accounts.SaveAsync(account.Id, account.Version, pending.Compose, cancellationToken);
        var version = await this.VersionOfAsync(record, cancellationToken);

        return write.Result switch
        {
            MailAccountWriteResult.Committed => await this.ServedAsync(
                UserRecordWriteOutcome.Committed(version, pending.StandingProblems),
                cancellationToken),
            MailAccountWriteResult.NotFound => null,
            MailAccountWriteResult.NothingComposed => pending.CommittedAs.Refusal ?? NothingHeldMoves(version),
            _ => Superseded(record.Version, version, "user record"),
        };
    }

    /// <summary>Reads the signed-in user's record, and refuses a change composed over a version no longer in force.</summary>
    private async Task<OpenedRecord?> OpenOwnAsync(long expectedVersion, CancellationToken cancellationToken)
    {
        if (await documents.ReadAsync(authorization.RequireUser(), cancellationToken) is not { } record)
        {
            return null;
        }

        return record.Version == expectedVersion
            ? new OpenedRecord(record, Refusal: null)
            : new OpenedRecord(record, Superseded(expectedVersion, record.Version, "user record"));
    }

    /// <summary>Judges an account write before its commit, and prepares the judgement the commit makes again.</summary>
    /// <param name="standing">The account as it stands, or <see langword="null" /> for one being created.</param>
    /// <param name="stated">The account as the change states it.</param>
    /// <param name="users">The users the account would be assigned to.</param>
    /// <param name="actingUser">The user making the change on their own behalf, or <see langword="null" /> for an administrator.</param>
    /// <param name="versionInForce">The version a refusal answers, which is the one the caller composed over.</param>
    /// <param name="readUnder">The policies the account was read with.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The write to commit, or the outcome that stops it.</returns>
    /// <remarks>
    /// Everything that reads is done here, before the commit: the record of each user the account reaches, and whether
    /// the secrets it names can be retrieved. That last is resolved here rather than left to the next start, which
    /// refuses the whole deployment over it. A change that leaves every credential exactly as the account held it
    /// cannot have introduced one, so it is reported rather than refused: refusing it would block every unrelated edit
    /// without making the next start any worse.
    /// </remarks>
    private async Task<OpenedWrite> JudgeAsync(
        MailAccountRecord? standing,
        MailAccountRecord stated,
        IReadOnlyList<UserId> users,
        UserId? actingUser,
        long versionInForce,
        GoverningSettingsPolicies readUnder,
        CancellationToken cancellationToken)
    {
        List<UserSettingsDocument> assignees = [];

        foreach (var assignee in users)
        {
            // Absent when the user was erased while this change was judged, which the store's own write settles.
            if (await documents.ReadAsync(assignee, cancellationToken) is { } record)
            {
                assignees.Add(record);
            }
        }

        JudgedAccount JudgeUnder(GoverningSettingsPolicies policies) =>
            this.JudgeUnder(standing, stated, users, assignees, actingUser, versionInForce, policies);

        var judged = JudgeUnder(readUnder);

        if (judged.Refusal is { } refusal)
        {
            return new OpenedWrite(refusal, Write: null);
        }

        if (judged.Candidate is not { } candidate)
        {
            return new OpenedWrite(NothingHeldMoves(versionInForce), Write: null);
        }

        var unusable = judged.Declared.Count == 0
            ? []
            : await secrets.FindUserMailAccountErrorsAsync(DeclarationPath, judged.Declared, cancellationToken);

        if (unusable.Count > 0 && !LeavesEverySecretAsItWas(standing?.Document ?? StatingNothing, candidate.Document))
        {
            return new OpenedWrite(
                UserRecordWriteOutcome.Refused(MailFathomErrorCode.ConfigurationCandidateInvalid, versionInForce, unusable),
                Write: null);
        }

        return new OpenedWrite(
            Refusal: null,
            new PendingAccountWrite(judged, readUnder, JudgeUnder)
            {
                StandingProblems = [.. unusable.Select(UserRecordAdministration.DescribeAsAlreadyHeld)],
            });
    }

    /// <summary>Judges an account as it would stand, under one reading of the policies governing it, against every user it reaches.</summary>
    /// <remarks>
    /// <para>
    /// Reads nothing and awaits nothing, which is what lets a commit ask it again inside its own transaction.
    /// </para>
    /// <para>
    /// The policies are asked first. A property a policy forces is refused to everybody, one the editing restriction
    /// withholds to a user acting for themselves, and a held property stated as a read showed it is carried through
    /// and leaves the account stating what it stated. Held rather than refused as a candidate somebody could correct:
    /// nothing about what was sent is wrong.
    /// </para>
    /// <para>
    /// Which secrets the account may carry afterwards is asked next and follows who is making the change. A user may
    /// newly name their own material. An administrator holding <see cref="MailFathomPermission.AdminConfigurationWrite" />
    /// below the deployment may change the settings only while every secret they name is the material of a user the
    /// account is assigned to, so a reference left in place cannot be presented somewhere new. One holding it over the
    /// whole deployment names anything.
    /// </para>
    /// <para>
    /// What is bound last is the account as the policies resolve it, composed into the record of each user it reaches
    /// as those same policies resolve that record, beside their other accounts.
    /// </para>
    /// </remarks>
    private JudgedAccount JudgeUnder(
        MailAccountRecord? standing,
        MailAccountRecord stated,
        IReadOnlyList<UserId> users,
        List<UserSettingsDocument> assignees,
        UserId? actingUser,
        long versionInForce,
        GoverningSettingsPolicies policies)
    {
        JudgedAccount Refused(IReadOnlyList<string> messages) =>
            new(
                UserRecordWriteOutcome.Refused(MailFathomErrorCode.ConfigurationCandidateInvalid, versionInForce, messages),
                Candidate: null,
                []);

        JudgedAccount Held(IReadOnlyList<string> messages) =>
            new(UserRecordWriteOutcome.Held(versionInForce, messages), Candidate: null, []);

        if (SettingsPolicyLayers.Read(policies, SettingsPolicySection.MailAccounts, out var unreadable) is not { } layers)
        {
            return Held([$"{SettingsPolicyLayers.DescribeUnreadable(unreadable)} Nothing was written."]);
        }

        var byThePerson = actingUser is not null;
        var statedDeclaration = MailAccountPolicyResolution.GovernedDeclarationOf(stated);

        var change = GovernedRecordChange.Judge(
            SettingsPolicySection.MailAccounts,
            layers,
            standing is null ? StatingNothing : MailAccountPolicyResolution.GovernedDeclarationOf(standing),
            statedDeclaration,
            byThePerson);

        string[] held = [.. change.Refusals, .. FindAddressRefusal(layers, standing, stated, byThePerson)];

        if (held.Length > 0)
        {
            return Held(held);
        }

        var carriedThrough = !ReferenceEquals(change.Json, statedDeclaration);
        var candidate = (carriedThrough ? WithDeclaration(stated, change.Json) : stated) with { Policies = policies };

        // Everything the change would have moved was a held property carried through, so the account is the one
        // already in force and committing it would move its version, and recompose every user it reaches on every
        // replica, for a change that changed nothing.
        if (carriedThrough && standing is not null && StatesWhatStands(standing, candidate))
        {
            return new JudgedAccount(Refusal: null, Candidate: null, []);
        }

        var standingDocument = standing?.Document ?? StatingNothing;

        var introduced = actingUser is { } user
            ? UserRecordAdministration.FindSecretsTheUserMayNotName(user, standingDocument, candidate.Document)
            : authorization.Permits(MailFathomPermission.AdminConfigurationWrite)
                ? []
                : UserRecordAdministration.FindSecretsANarrowerScopeMayNotName(users, standingDocument, candidate.Document);

        if (introduced.Count > 0)
        {
            return Refused(introduced);
        }

        // An account is assigned only to users of its own organization, so the policies governing it are the ones
        // governing each of them and every other account they are assigned.
        var userLayers = SettingsPolicyLayers.Read(policies, SettingsPolicySection.Users, out _);
        List<string> refusals = [];
        UserAccountOptions? bound = null;

        foreach (var record in assignees)
        {
            if (record.MailAccounts.Count >= MailAccountRecord.MaximumAssignedPerUser
                && record.MailAccounts.All(account => account.Id != candidate.Id))
            {
                refusals.Add($"A user is assigned at most {MailAccountRecord.MaximumAssignedPerUser} mail accounts, and '{record.DisplayName}' already is.");

                continue;
            }

            var binding = binder.Bind(
                MailAccountRecordComposition.Compose(
                    userLayers is null
                        ? record.Json
                        : GovernedRecordResolution.Resolve(SettingsPolicySection.Users, userLayers, record.Json).EffectiveJson,
                    WithCandidate([.. record.MailAccounts.Select(account => account with { Policies = policies })], candidate)),
                UserRecordArrival.BeingWritten);

            refusals.AddRange(binding.Refusals);
            bound ??= binding.User;
        }

        if (refusals.Count > 0)
        {
            return Refused([.. refusals.Distinct(StringComparer.Ordinal)]);
        }

        var identifier = candidate.Id.ToString("D");

        return new JudgedAccount(
            Refusal: null,
            candidate,
            [
                .. (bound?.MailAccounts ?? [])
                    .Where(account => MailSynchronizationOptions.TryReadAccountId(account.AccountId) == identifier),
            ]);
    }

    /// <summary>Reads the version a user's record stands at now, which every account write moves.</summary>
    private async Task<long> VersionOfAsync(UserSettingsDocument record, CancellationToken cancellationToken) =>
        (await documents.ReadAsync(record.User, cancellationToken))?.Version ?? record.Version;

    private async Task<UserRecordWriteOutcome> ServedAsync(UserRecordWriteOutcome committed, CancellationToken cancellationToken)
    {
        await this.PublishAsync(cancellationToken);

        return committed;
    }

    /// <summary>Serves a committed account write on this replica and announces it to every other.</summary>
    /// <remarks>Announced whether or not this replica's own reading succeeded, because the rows are committed either way and every other replica converges on them.</remarks>
    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        try
        {
            await servedUsers.ConvergeAsync(cancellationToken);
        }
        catch (UserSettingsUnreadableException)
        {
            // The write is committed whether or not this reading succeeds, and this replica's next version poll
            // converges on the rows like every other, so the caller is answered with what was committed.
        }
        finally
        {
            await announcements.AnnounceAsync();
        }
    }

    private sealed record OpenedRecord(UserSettingsDocument Record, UserRecordWriteOutcome? Refusal);

    /// <summary>What judging an account under one reading of its policies found.</summary>
    /// <param name="Refusal">The outcome that stops the write, or <see langword="null" /> where it may stand.</param>
    /// <param name="Candidate">The account to commit, with every held property the change carried through left as the account states it; <see langword="null" /> where a refusal stops the write, and where everything the change named is held where it stands.</param>
    /// <param name="Declared">The account as a user it reaches would be served it, which its credentials are resolved from.</param>
    private sealed record JudgedAccount(
        UserRecordWriteOutcome? Refusal,
        MailAccountRecord? Candidate,
        IReadOnlyList<MailSynchronizationAccountOptions> Declared);

    /// <summary>An account write as it was judged before its commit: the write to commit, or the outcome that stops it.</summary>
    private readonly record struct OpenedWrite(UserRecordWriteOutcome? Refusal, PendingAccountWrite? Write);

    /// <summary>One account write that passed the judgement made before its commit, and what the commit decided on.</summary>
    /// <param name="judged">The judgement made under the policies the account was read with.</param>
    /// <param name="readUnder">Those policies.</param>
    /// <param name="judgeAgain">Judges the same change under another reading of the policies, reading nothing.</param>
    private sealed class PendingAccountWrite(
        JudgedAccount judged,
        GoverningSettingsPolicies readUnder,
        Func<GoverningSettingsPolicies, JudgedAccount> judgeAgain)
    {
        private readonly JudgedAccount judgedBeforeTheCommit = judged;

        /// <summary>Gets the judgement the commit decided on, which is the one made before it until the commit asks.</summary>
        public JudgedAccount CommittedAs { get; private set; } = judged;

        /// <summary>Gets the problems the account already carried, which a committed write reports rather than refuses.</summary>
        public IReadOnlyList<string> StandingProblems { get; init; } = [];

        /// <summary>Composes what the commit writes, under the policies in force inside it.</summary>
        /// <param name="policies">The policies the commit read inside its own transaction.</param>
        /// <returns>The account and the queryable settings it resolves to, or <see langword="null" /> where those policies refuse the change or leave nothing to write.</returns>
        /// <remarks>Where the commit read the policies the account was read with, the judgement made before it is the one that decides; a policy that moved in between is judged against here.</remarks>
        public MailAccountCandidate? Compose(GoverningSettingsPolicies policies)
        {
            this.CommittedAs = policies == readUnder ? this.judgedBeforeTheCommit : judgeAgain(policies);

            return this.CommittedAs is { Refusal: null, Candidate: { } candidate }
                && MailAccountQueryableSettingsReading.Of(candidate) is { } settings
                    ? new MailAccountCandidate(candidate, settings)
                    : null;
        }
    }
}
