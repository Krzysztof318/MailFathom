// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Signals;
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
/// only caller that assigns an account to a second user. A user adds an account for themselves alone, withdraws one of
/// their own, and changes the folders of one — and every one of those writes states the version of their own record,
/// which is what every account write moves, so the version a client already holds is the one it composes against.
/// </para>
/// <para>
/// An address is held by one account in the whole deployment. An administrator told the address is held is told so by
/// name, because assigning the account that holds it is what they would do next. A user is told only that the account
/// cannot be added, so no caller learns through this surface which addresses somebody else's mail arrives at.
/// </para>
/// <para>
/// A committed write is served by converging this replica's roster on the rows, which republishes every user whose record
/// version the write moved, and then announced so every other replica converges too.
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class MailAccountAdministration(
    AccessAuthorization authorization,
    IUserSettingsDocumentReader documents,
    IMailAccountRecordStore accounts,
    UserAccountDocumentBinder binder,
    SecretConfigurationValidator secrets,
    ServedMailUsersConvergence convergence,
    ConfigurationChangeAnnouncements announcements)
{
    /// <summary>The greatest number of accounts one listing reads.</summary>
    internal const int MaximumListed = 1024;

    /// <summary>What a save refused over a redaction marker it cannot place is sent to.</summary>
    private const string NarrowerChange = "state the setting afresh rather than leaving the redaction marker in its place.";

    /// <summary>The path a refusal about an account's credentials names, which is the declaration rather than a file.</summary>
    private const string DeclarationPath = "declaration";

    /// <summary>Lists the accounts this deployment holds.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The accounts, redacted, in the order they were created in.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminRead" />.</exception>
    internal async Task<IReadOnlyList<MailAccountReading>> ReadAllAsync(CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.AdminRead);

        return [.. (await accounts.ReadAllAsync(MaximumListed, cancellationToken)).Select(ReadingOf)];
    }

    /// <summary>Reads one account.</summary>
    /// <param name="accountId">The account asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The account, redacted, or <see langword="null" /> when this deployment holds none under that identifier.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminRead" />.</exception>
    internal async Task<MailAccountReading?> ReadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.AdminRead);

        return await accounts.ReadAsync(accountId, cancellationToken) is { } holding ? ReadingOf(holding) : null;
    }

    /// <summary>Creates an account and assigns it to one user.</summary>
    /// <param name="user">The user the account is created for.</param>
    /// <param name="declarationJson">The declaration.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>What the write did, or <see langword="null" /> when this deployment holds no such user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declarationJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    internal async Task<MailAccountCreation?> CreateAsync(
        MailUserId user,
        string declarationJson,
        CancellationToken cancellationToken)
    {
        RequireNamed(user);
        ArgumentNullException.ThrowIfNull(declarationJson);
        authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        if (await documents.ReadAsync(user, cancellationToken) is not { } record)
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
    /// <returns>What the write did, its version the account's, or <see langword="null" /> when this deployment holds no such account.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declarationJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    /// <remarks>A value left at the redaction marker leaves the reference beneath it as it was, exactly as a saved user record does.</remarks>
    internal async Task<UserRecordWriteOutcome?> SaveAsync(
        Guid accountId,
        string declarationJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declarationJson);
        authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        if (await accounts.ReadAsync(accountId, cancellationToken) is not { } holding)
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

        var judgement = await this.JudgeAsync(candidate, standing.Document, holding.Users, actingUser: null, cancellationToken);

        if (judgement.Refusals.Count > 0)
        {
            return UserRecordWriteOutcome.Refused(MailFathomErrorCode.ConfigurationCandidateInvalid, standing.Version, judgement.Refusals);
        }

        var write = await accounts.SaveAsync(candidate, cancellationToken);

        return write.Result switch
        {
            MailAccountWriteResult.NotFound => null,
            MailAccountWriteResult.Committed => await this.ServedAsync(
                UserRecordWriteOutcome.Committed(write.Version, judgement.StandingProblems),
                cancellationToken),
            MailAccountWriteResult.AddressHeld => AddressHeldForAdministrator(write.Version, candidate.EmailAddress),
            _ => Superseded(standing.Version, write.Version, "mail account"),
        };
    }

    /// <summary>Assigns an account to one more user.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="user">The user it is assigned to.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>What the write did, its version the account's, or <see langword="null" /> when this deployment holds no such account or user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    /// <remarks>The account is judged against the user's own set, so an account whose display name one of their accounts already carries is refused rather than served under a name that no longer tells two apart.</remarks>
    internal async Task<UserRecordWriteOutcome?> AssignAsync(
        Guid accountId,
        MailUserId user,
        CancellationToken cancellationToken)
    {
        RequireNamed(user);
        authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        if (await accounts.ReadAsync(accountId, cancellationToken) is not { } holding
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

        var judgement = await this.JudgeAsync(account, account.Document, [user], actingUser: null, cancellationToken);

        if (judgement.Refusals.Count > 0)
        {
            return UserRecordWriteOutcome.Refused(MailFathomErrorCode.ConfigurationCandidateInvalid, account.Version, judgement.Refusals);
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
            _ => Superseded(record.Version, write.Version, "user record"),
        };
    }

    /// <summary>Ends one user's assignment to an account, erasing the account and its mail when nobody else is assigned it.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="user">The user whose assignment ends.</param>
    /// <param name="cancellationToken">Cancels the write before it commits.</param>
    /// <returns>What the write did.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminErase" />.</exception>
    /// <remarks>The erasure grant rather than the configuration write, because ending the last assignment disposes of every message the deployment holds for the mailbox.</remarks>
    internal async Task<MailAccountUnassignment> UnassignAsync(
        Guid accountId,
        MailUserId user,
        CancellationToken cancellationToken)
    {
        RequireNamed(user);
        authorization.RequirePermission(MailFathomPermission.AdminErase);

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
    /// <returns>Whether an account was there to erase.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminErase" />.</exception>
    internal async Task<bool> EraseAsync(Guid accountId, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.AdminErase);

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
    /// <remarks>An account nobody else is assigned is erased with its mail, which is the same outcome an administrator's unassignment has.</remarks>
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
    /// The folder acts carry the grant an account's own settings carry rather than one of their own: a folder names a
    /// path on that account's server and decides what is mirrored from it, so a deployment that lets somebody state
    /// their own mailboxes has already let them state what is read out of one.
    /// </remarks>
    internal Task<UserRecordWriteOutcome?> AddOwnFolderAsync(
        string accountId,
        string folderJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(folderJson);

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

        return this.ChangeOwnFolderAsync(
            accountId,
            expectedVersion,
            document => MailAccountFolderComposition.WithFolderRemoved(document, alias),
            $"This mail account declares no folder '{alias}'.",
            cancellationToken);
    }

    private static MailAccountReading ReadingOf(MailAccountHolding holding) =>
        new(
            holding.Account.Id,
            SettingRedaction.ApplyToDocument(MailAccountDeclaration.Of(holding.Account)),
            holding.Account.Version,
            holding.Users);

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

    private static UserRecordWriteOutcome AddressHeldForAdministrator(long version, string? emailAddress) =>
        UserRecordWriteOutcome.Refused(
            MailFathomErrorCode.ConfigurationCandidateInvalid,
            version,
            [
                $"Another mail account already holds '{emailAddress}', and one address is held by one account in this deployment. Assign that account to the user with 'mfctl account assign' instead.",
            ]);

    /// <summary>The refusal a user receives for an address somebody's account already holds.</summary>
    /// <remarks>The same sentence whoever holds it, so the answer says nothing about which addresses this deployment serves for other people.</remarks>
    private static UserRecordWriteOutcome AddressHeldForUser(long version) =>
        UserRecordWriteOutcome.Refused(
            MailFathomErrorCode.ConfigurationCandidateInvalid,
            version,
            ["This mail account cannot be added for you. Ask whoever administers this deployment to add it."]);

    private static void RequireNamed(MailUserId user)
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
            declaration = MailAccountDeclaration.Read(declarationJson);
        }
        catch (Exception refused) when (refused is FormatException or JsonException)
        {
            return (null, NotADeclaration(record.Version, refused));
        }

        var candidate = new MailAccountRecord(
            Guid.NewGuid(),
            declaration.EmailAddress,
            declaration.DisplayName,
            declaration.Document,
            Version: 0);

        var judgement = await this.JudgeAsync(
            candidate,
            standingDocument: "{}",
            [record.User],
            authority == UserRecordAuthority.User ? record.User : null,
            cancellationToken);

        if (judgement.Refusals.Count > 0)
        {
            return (null, UserRecordWriteOutcome.Refused(MailFathomErrorCode.ConfigurationCandidateInvalid, record.Version, judgement.Refusals));
        }

        var write = await accounts.CreateAsync(record.User, record.Version, candidate, cancellationToken);

        if (write.Result != MailAccountWriteResult.Committed)
        {
            var current = await this.VersionOfAsync(record, cancellationToken);

            return (null, write.Result switch
            {
                MailAccountWriteResult.AddressHeld when authority == UserRecordAuthority.User => AddressHeldForUser(current),
                MailAccountWriteResult.AddressHeld => AddressHeldForAdministrator(current, candidate.EmailAddress),
                _ => Superseded(record.Version, current, "user record"),
            });
        }

        var version = authority == UserRecordAuthority.User
            ? await this.VersionOfAsync(record, cancellationToken)
            : write.Version;

        return (candidate.Id, await this.ServedAsync(UserRecordWriteOutcome.Committed(version, judgement.StandingProblems), cancellationToken));
    }

    /// <summary>Composes one folder change over one of the signed-in user's accounts, and saves the account it produced.</summary>
    /// <remarks>A change that matched no folder is refused with the sentence <c>unmatched</c> carries, which is absent only for an addition, since an addition always matches.</remarks>
    private async Task<UserRecordWriteOutcome?> ChangeOwnFolderAsync(
        string accountId,
        long expectedVersion,
        Func<string, string?> compose,
        string? unmatched,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailAccountsWrite);

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

        string? candidateDocument;

        try
        {
            candidateDocument = compose(account.Document);
        }
        catch (Exception refused) when (refused is FormatException or JsonException)
        {
            return UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                record.Version,
                [$"The folder change is not one this deployment can compose over the mail account, so nothing was written: {refused.Message}"]);
        }

        if (candidateDocument is null)
        {
            return UserRecordWriteOutcome.Refused(MailFathomErrorCode.ConfigurationCandidateInvalid, record.Version, [unmatched!]);
        }

        var candidate = account with { Document = candidateDocument };
        var users = (await accounts.ReadAsync(account.Id, cancellationToken))?.Users ?? [record.User];
        var judgement = await this.JudgeAsync(candidate, account.Document, users, record.User, cancellationToken);

        if (judgement.Refusals.Count > 0)
        {
            return UserRecordWriteOutcome.Refused(MailFathomErrorCode.ConfigurationCandidateInvalid, record.Version, judgement.Refusals);
        }

        var write = await accounts.SaveAsync(candidate, cancellationToken);
        var version = await this.VersionOfAsync(record, cancellationToken);

        return write.Result == MailAccountWriteResult.Committed
            ? await this.ServedAsync(UserRecordWriteOutcome.Committed(version, judgement.StandingProblems), cancellationToken)
            : Superseded(record.Version, version, "user record");
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

    /// <summary>Judges an account as it would stand against every user it is assigned to.</summary>
    /// <param name="candidate">The account as it would stand.</param>
    /// <param name="standingDocument">The settings the account holds now, which a credential already carried is compared against.</param>
    /// <param name="users">The users the account would be assigned to.</param>
    /// <param name="actingUser">The user making the change on their own behalf, or <see langword="null" /> for an administrator.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    private async Task<Judgement> JudgeAsync(
        MailAccountRecord candidate,
        string standingDocument,
        IReadOnlyList<MailUserId> users,
        MailUserId? actingUser,
        CancellationToken cancellationToken)
    {
        if (actingUser is { } user
            && UserRecordAdministration.FindSecretsTheUserMayNotName(user, standingDocument, candidate.Document) is { Count: > 0 } introduced)
        {
            return new Judgement(introduced, []);
        }

        List<string> refusals = [];
        UserAccountOptions? bound = null;

        foreach (var assignee in users)
        {
            // Absent when the user was erased while this change was judged, which the store's own write settles.
            if (await documents.ReadAsync(assignee, cancellationToken) is not { } record)
            {
                continue;
            }

            if (record.MailAccounts.Count >= MailAccountRecord.MaximumAssignedPerUser
                && record.MailAccounts.All(account => account.Id != candidate.Id))
            {
                refusals.Add($"A user is assigned at most {MailAccountRecord.MaximumAssignedPerUser} mail accounts, and '{record.DisplayName}' already is.");

                continue;
            }

            var binding = binder.Bind(
                MailAccountRecordComposition.Compose(record.Json, WithCandidate(record.MailAccounts, candidate)),
                UserRecordArrival.BeingWritten);

            refusals.AddRange(binding.Refusals);
            bound ??= binding.User;
        }

        if (refusals.Count > 0 || bound is null)
        {
            return new Judgement([.. refusals.Distinct(StringComparer.Ordinal)], []);
        }

        var identifier = candidate.Id.ToString("D");
        IReadOnlyList<MailSynchronizationAccountOptions> declared =
        [
            .. bound.MailAccounts.Where(account => MailSynchronizationOptions.TryReadAccountId(account.AccountId) == identifier),
        ];

        // Resolved here rather than left to the next start, which refuses the whole deployment over it. A change that
        // leaves every credential exactly as the account held it cannot have introduced one, so it is reported rather
        // than refused: refusing it would block every unrelated edit without making the next start any worse.
        var unusable = await secrets.FindUserMailAccountErrorsAsync(DeclarationPath, declared, cancellationToken);

        return unusable.Count == 0
            ? new Judgement([], [])
            : LeavesEverySecretAsItWas(standingDocument, candidate.Document)
                ? new Judgement([], [.. unusable.Select(UserRecordAdministration.DescribeAsAlreadyHeld)])
                : new Judgement(unusable, []);
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
            await convergence.ConvergeAsync(cancellationToken);
        }
        finally
        {
            await announcements.AnnounceAsync();
        }
    }

    private sealed record OpenedRecord(UserSettingsDocument Record, UserRecordWriteOutcome? Refusal);

    /// <summary>What judging an account found: refusals that stop the write, or problems the account already carried.</summary>
    private readonly record struct Judgement(IReadOnlyList<string> Refusals, IReadOnlyList<string> StandingProblems);
}
