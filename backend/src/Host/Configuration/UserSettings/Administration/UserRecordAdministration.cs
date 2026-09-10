// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Configuration;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What is done to one user's own record, by an administrator or by that user.</summary>
/// <remarks>
/// <para>
/// Every change here produces a candidate record and puts it through the one binder both directions share, so a record
/// a write accepts is a record the next start would read. Nothing patches the row in place: the caller states an act —
/// a saved document, one mailbox added, one withdrawn — and what the act composes is judged whole.
/// </para>
/// <para>
/// Two callers reach it and the pairs of entry points are what separate them. An administrator names the user and
/// holds an administrative grant; a user names nobody and holds one of their own, so their entry points resolve the
/// user from the principal and there is no argument for a request to put another user's identifier in. Each pair
/// delegates to the same private work, which is where the rules live, so the two callers cannot come to be judged
/// differently.
/// </para>
/// <para>
/// One refusal is the reason this service exists rather than a writer being called directly. A user a configuration
/// source still supplies holds an empty record, so a change accepted into it would leave them served from a record
/// holding less than the file was supplying — a mailbox that stops being synchronized because somebody edited a
/// setting beside it. Every write is refused for that user, and the file is where their mail accounts are changed.
/// </para>
/// <para>
/// The one rule that reads which of the two is acting is the secret-bearing settings. A secret reference is a path into
/// whatever this deployment can read — a mounted file, a credential, an environment variable — and the server a mail
/// account names is the user's own, so a reference a user wrote would hand them whatever stands behind it. What a
/// user may name is therefore bounded to material provisioned for them, which an operator declares by naming it after
/// them; the references their record already carries survive a change that was never about them, and anything else is
/// declared by whoever administers the deployment.
/// </para>
/// <para>
/// Nothing here composes a configuration layer over the deployment's. A record is bound from the document alone, so no
/// value in it shadows a setting the deployment made, and the shadowing question the deployment's own writes answer
/// does not arise.
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class UserRecordAdministration(
    AccessAuthorization authorization,
    IUserSettingsDocumentReader documents,
    IUserSettingsDocumentWriter store,
    UserAccountDocumentBinder binder,
    SecretConfigurationValidator secrets,
    ServedMailUsers servedUsers,
    ConfiguredUserSettings configured)
{
    /// <summary>What a refused save is sent to, which is the act that states a mailbox and its credential afresh.</summary>
    /// <remarks>
    /// Not a narrower change, because a user's record has none: every setting of a mail account sits inside that
    /// account's own element, so a save that changes one while a secret beside it stands redacted is exactly the case
    /// the marker rule refuses. What the surface does have is the pair that replaces the whole element, and the
    /// credential is stated again as part of it — which is what the reader is sent to instead of a command that could
    /// not make the change.
    /// </remarks>
    private const string NarrowerChange =
        "withdraw that mail account with 'mfctl user account remove' and declare it again with 'mfctl user account add', which states its credential afresh.";

    /// <summary>Reads one user's record as an administrator sees it.</summary>
    /// <param name="user">The user asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The record, or <see langword="null" /> when this deployment holds no such user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the deployment holds the record and it could not be handed on.</exception>
    /// <exception cref="FormatException">Thrown when the row is JSON but not an object of settings.</exception>
    /// <exception cref="System.Text.Json.JsonException">Thrown when the row is not JSON, or is nested past what a document may be.</exception>
    internal Task<UserRecordReading?> ReadRecordAsync(MailUserId user, CancellationToken cancellationToken)
    {
        RequireNamed(user);
        authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.ReadAsync(user, cancellationToken);
    }

    /// <summary>Reads the signed-in user's own record.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The record, or <see langword="null" /> when this deployment holds no record for the acting user.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the deployment holds the record and it could not be handed on.</exception>
    internal Task<UserRecordReading?> ReadOwnRecordAsync(CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailRead);

        return this.ReadAsync(authorization.RequireUser(), cancellationToken);
    }

    /// <summary>Applies a whole edited record to one user.</summary>
    /// <param name="user">The user whose record is written.</param>
    /// <param name="documentJson">The record the caller saved.</param>
    /// <param name="expectedVersion">The version the buffer was opened over.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>What the write did, or <see langword="null" /> when this deployment holds no such user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="documentJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    internal Task<UserRecordWriteOutcome?> ApplyRecordAsync(
        MailUserId user,
        string documentJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        RequireNamed(user);
        ArgumentNullException.ThrowIfNull(documentJson);
        authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        return this.SaveAsync(
            user,
            documentJson,
            expectedVersion,
            UserRecordAuthority.Administrator,
            cancellationToken);
    }

    /// <summary>Applies a whole edited record to the signed-in user.</summary>
    /// <param name="documentJson">The record the user saved.</param>
    /// <param name="expectedVersion">The version the buffer was opened over.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>What the write did, or <see langword="null" /> when this deployment holds no record for the acting user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="documentJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailAccountsWrite" />.</exception>
    internal Task<UserRecordWriteOutcome?> ApplyOwnRecordAsync(
        string documentJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documentJson);
        authorization.RequirePermission(MailFathomPermission.MailAccountsWrite);

        return this.SaveAsync(
            authorization.RequireUser(),
            documentJson,
            expectedVersion,
            UserRecordAuthority.User,
            cancellationToken);
    }

    /// <summary>Declares one more mail account in a user's record.</summary>
    /// <param name="user">The user the mailbox belongs to.</param>
    /// <param name="accountJson">The declaration, as the JSON object a file would have written.</param>
    /// <param name="expectedVersion">The version the record was read at.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>What the write did, or <see langword="null" /> when this deployment holds no such user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accountJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    internal Task<UserRecordWriteOutcome?> AddMailAccountAsync(
        MailUserId user,
        string accountJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        RequireNamed(user);
        ArgumentNullException.ThrowIfNull(accountJson);
        authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        return this.AddAsync(
            user,
            accountJson,
            expectedVersion,
            UserRecordAuthority.Administrator,
            cancellationToken);
    }

    /// <summary>Declares one more mail account in the signed-in user's record.</summary>
    /// <param name="accountJson">The declaration, as the JSON object a file would have written.</param>
    /// <param name="expectedVersion">The version the record was read at.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>What the write did, or <see langword="null" /> when this deployment holds no record for the acting user.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accountJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailAccountsWrite" />.</exception>
    internal Task<UserRecordWriteOutcome?> AddOwnMailAccountAsync(
        string accountJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountJson);
        authorization.RequirePermission(MailFathomPermission.MailAccountsWrite);

        return this.AddAsync(
            authorization.RequireUser(),
            accountJson,
            expectedVersion,
            UserRecordAuthority.User,
            cancellationToken);
    }

    /// <summary>Withdraws one mail account from a user's record.</summary>
    /// <param name="user">The user the mailbox belongs to.</param>
    /// <param name="accountId">The identifier the declaration is named by.</param>
    /// <param name="expectedVersion">The version the record was read at.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>What the write did, or <see langword="null" /> when this deployment holds no such user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody, or <paramref name="accountId" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    /// <remarks>The mail this deployment already stored for that account is deliberately untouched, exactly as it is when a file stops declaring one: no configuration edit takes somebody's mail away, and erasing it is a separate act somebody means.</remarks>
    internal Task<UserRecordWriteOutcome?> RemoveMailAccountAsync(
        MailUserId user,
        string accountId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        RequireNamed(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        return this.RemoveAsync(
            user,
            accountId,
            expectedVersion,
            UserRecordAuthority.Administrator,
            cancellationToken);
    }

    /// <summary>Withdraws one mail account from the signed-in user's record.</summary>
    /// <param name="accountId">The identifier the declaration is named by.</param>
    /// <param name="expectedVersion">The version the record was read at.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>What the write did, or <see langword="null" /> when this deployment holds no record for the acting user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailAccountsWrite" />.</exception>
    internal Task<UserRecordWriteOutcome?> RemoveOwnMailAccountAsync(
        string accountId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        authorization.RequirePermission(MailFathomPermission.MailAccountsWrite);

        return this.RemoveAsync(
            authorization.RequireUser(),
            accountId,
            expectedVersion,
            UserRecordAuthority.User,
            cancellationToken);
    }

    /// <summary>Reads one user's record, redacted, with the source their mail accounts come from beside it.</summary>
    private async Task<UserRecordReading?> ReadAsync(MailUserId user, CancellationToken cancellationToken) =>
        await documents.ReadAsync(user, cancellationToken) is { } record
            ? new UserRecordReading(
                user,
                record.DisplayName,
                SettingRedaction.ApplyToDocument(record.Json),
                record.Version,
                this.SourceOf(user))
            : null;

    /// <summary>Applies a saved record as the difference between what it says and what the row holds.</summary>
    /// <remarks>
    /// The buffer becomes keyed changes rather than replacing the document wholesale, so one vocabulary reaches the
    /// commit whichever surface stated the change — and so a value left at the redaction marker leaves the reference
    /// beneath it exactly as it was rather than persisting the marker over somebody's credential.
    /// </remarks>
    private async Task<UserRecordWriteOutcome?> SaveAsync(
        MailUserId user,
        string documentJson,
        long expectedVersion,
        UserRecordAuthority authority,
        CancellationToken cancellationToken)
    {
        if (await this.OpenAsync(user, expectedVersion, cancellationToken)
            is not { } opened)
        {
            return null;
        }

        if (opened.Refusal is { } refusal)
        {
            return refusal;
        }

        var inForce = opened.Record;
        IReadOnlyList<ConfigurationEdit> edits;
        IReadOnlyList<string> unplaceable;

        try
        {
            var standing = RedactedDocumentSave.Flatten(inForce.Json);
            var saved = RedactedDocumentSave.Flatten(documentJson);

            edits = RedactedDocumentSave.DifferenceBetween(standing, saved);
            unplaceable = RedactedDocumentSave.FindMarkersTheSaveCannotPlace(standing, saved, NarrowerChange);
        }
        catch (Exception refused)
            when (refused is FormatException or System.Text.Json.JsonException or InvalidDataException
                or ArgumentException)
        {
            // The buffer is what somebody typed, so every way it can be wrong is theirs to correct rather than a
            // defect: a document that is not an object of settings, a key with no name, or a value carrying a
            // character PostgreSQL text cannot hold. The parser's own message names which, and it names no value.
            return UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                inForce.Version,
                [$"The saved record is not a document of settings this deployment can persist, so nothing was written: {refused.Message}"]);
        }

        if (unplaceable.Count > 0)
        {
            return UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                inForce.Version,
                unplaceable);
        }

        if (edits.Count == 0)
        {
            return UserRecordWriteOutcome.NothingToChange(
                inForce.Version,
                $"The saved record composes the settings the user's record already carries, so nothing was written and version {inForce.Version} stays in force.");
        }

        return await this.JudgeAndCommitAsync(
            user,
            inForce,
            SettingsDocumentPatch.Apply(inForce.Json, edits),
            authority,
            cancellationToken);
    }

    /// <summary>Declares one more mail account, composed over the record as the row holds it.</summary>
    /// <remarks>The standing document rather than a redacted reading, so every secret reference the record already carries survives a change that was never about them.</remarks>
    private async Task<UserRecordWriteOutcome?> AddAsync(
        MailUserId user,
        string accountJson,
        long expectedVersion,
        UserRecordAuthority authority,
        CancellationToken cancellationToken)
    {
        if (await this.OpenAsync(user, expectedVersion, cancellationToken)
            is not { } opened)
        {
            return null;
        }

        if (opened.Refusal is { } refusal)
        {
            return refusal;
        }

        string candidate;

        try
        {
            candidate = UserRecordComposition.WithMailAccountAdded(opened.Record.Json, accountJson);
        }
        catch (Exception refused) when (refused is FormatException or System.Text.Json.JsonException)
        {
            return UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                opened.Record.Version,
                [$"The mail account is not a JSON object of that account's settings, so nothing was written: {refused.Message}"]);
        }

        return await this.JudgeAndCommitAsync(user, opened.Record, candidate, authority, cancellationToken);
    }

    /// <summary>Withdraws one mail account, refusing an identifier the record does not declare.</summary>
    private async Task<UserRecordWriteOutcome?> RemoveAsync(
        MailUserId user,
        string accountId,
        long expectedVersion,
        UserRecordAuthority authority,
        CancellationToken cancellationToken)
    {
        if (await this.OpenAsync(user, expectedVersion, cancellationToken)
            is not { } opened)
        {
            return null;
        }

        if (opened.Refusal is { } refusal)
        {
            return refusal;
        }

        string? candidate;

        try
        {
            candidate = UserRecordComposition.WithMailAccountRemoved(opened.Record.Json, accountId);
        }
        catch (Exception refused) when (refused is FormatException or System.Text.Json.JsonException)
        {
            return UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                opened.Record.Version,
                [$"The user's record is not a document of settings this deployment can read, so nothing was written: {refused.Message}"]);
        }

        // Reported as a refusal rather than as nothing to change, because the two are different things to tell
        // somebody: a removal that matched nothing is an identifier they got wrong, and answering that the record is
        // fine would leave them believing a mailbox had stopped being synchronized.
        return candidate is null
            ? UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                opened.Record.Version,
                [$"This user declares no mail account '{accountId}'. Read their record to see the identifiers it holds."])
            : await this.JudgeAndCommitAsync(user, opened.Record, candidate, authority, cancellationToken);
    }

    /// <summary>Reads the record a change is composed over, and refuses the two cases nothing further should be done for.</summary>
    /// <remarks>
    /// The version is checked here as well as in the statement, so an edit authored against a record somebody else has
    /// replaced is refused before a candidate is composed and bound rather than after. The configuration refusal is
    /// checked first, because a user a file still supplies is refused whatever version they stated.
    /// </remarks>
    private async Task<OpenedRecord?> OpenAsync(
        MailUserId user,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        if (await documents.ReadAsync(user, cancellationToken) is not { } inForce)
        {
            return null;
        }

        if (this.SourceOf(user) != MailUserAccountSource.UserDocument)
        {
            return new OpenedRecord(inForce, UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.UserRecordReadFromConfiguration,
                inForce.Version,
                [
                    $"This user's mail accounts are supplied by a configuration source, so their record is empty and a change written into it would leave them served from less than the file supplies. Change them where they are declared; nothing moves a configuration source's declarations into a record.",
                ]));
        }

        return inForce.Version == expectedVersion
            ? new OpenedRecord(inForce, Refusal: null)
            : new OpenedRecord(inForce, UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationVersionSuperseded,
                inForce.Version,
                [
                    $"The change was composed over user record version {expectedVersion}, and version {inForce.Version} is in force. Read the record as it now stands and decide again against it.",
                ]));
    }

    /// <summary>Binds the candidate, judges it against what the deployment already serves, and commits it.</summary>
    /// <remarks>
    /// The binder is the same one a start reads a record with, so what a write accepts is what the next start would
    /// read. What it cannot answer is asked beside it: the rule that is about the deployment rather than about the
    /// record, the rule that is about who is writing, and whether the secrets the record names can actually be
    /// retrieved — which the binder cannot ask because it resolves nothing, and which a start refuses for the whole
    /// deployment rather than for the user whose record carries the reference.
    /// </remarks>
    private async Task<UserRecordWriteOutcome?> JudgeAndCommitAsync(
        MailUserId user,
        UserSettingsDocument inForce,
        string candidateJson,
        UserRecordAuthority authority,
        CancellationToken cancellationToken)
    {
        var binding = binder.Bind(candidateJson, UserRecordArrival.BeingWritten);

        if (binding.User is not { } bound)
        {
            return UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                inForce.Version,
                binding.Refusals);
        }

        if (authority == UserRecordAuthority.User
            && FindSecretsTheUserMayNotName(user, inForce.Json, candidateJson) is { Count: > 0 } introduced)
        {
            return UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                inForce.Version,
                introduced);
        }

        // Resolved here rather than left to the next start, which refuses the whole deployment over it: a reference
        // that is well formed and names nothing retrievable binds cleanly, commits, and then stops the host for every
        // user it serves until somebody corrects the row by hand.
        if (await secrets.FindUserMailAccountErrorsAsync(RecordPath, bound.MailAccounts, cancellationToken)
            is { Count: > 0 } unusable)
        {
            return UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                inForce.Version,
                unusable);
        }

        await servedUsers.WaitForRosterPublicationAsync(cancellationToken);

        try
        {
            if (this.FindNamesHeldByAnotherUser(user, bound.MailAccounts) is { Count: > 0 } taken)
            {
                return UserRecordWriteOutcome.Refused(
                    MailFathomErrorCode.ConfigurationCandidateInvalid,
                    inForce.Version,
                    taken);
            }

            if (await store.CommitAsync(user, candidateJson, inForce.Version, cancellationToken) is { } committed)
            {
                servedUsers.UserDocumentPublished(user, inForce.DisplayName, bound, committed);

                return UserRecordWriteOutcome.Committed(committed);
            }

            // The record moved while this candidate was being judged, or the user was erased under it. Which of the
            // two is settled by reading rather than assumed, because the statement distinguishes neither.
            return await documents.ReadAsync(user, cancellationToken) is { } current
                ? UserRecordWriteOutcome.Refused(
                    MailFathomErrorCode.ConfigurationVersionSuperseded,
                    current.Version,
                    [
                        $"The change was composed over user record version {inForce.Version}, and version {current.Version} is in force. Read the record as it now stands and decide again against it.",
                    ])
                : null;
        }
        finally
        {
            servedUsers.ReleaseRosterPublication();
        }
    }

    /// <summary>Names every secret-bearing value the candidate carries that this user may not point their record at.</summary>
    /// <remarks>
    /// <para>
    /// A reference is a path into whatever this deployment can read, and the server a mail account names is the
    /// user's own — so a reference a user wrote would present whatever stands behind it to a machine they control.
    /// Two things make one admissible, and nothing else does. A reference the record already carries is admissible
    /// whoever put it there, because a change that was never about the credential must not be refused over it. And a
    /// reference whose material was provisioned for this user is admissible, which is read from the name the operator
    /// gave it: the last segment of the target begins with this user's identifier. Anything else — the database
    /// password, a private key, another user's mailbox credential — is named by something that does not, so a user
    /// asking for it is refused rather than served.
    /// </para>
    /// <para>
    /// The last segment is what carries the bound, rather than the whole target, because that is the part a traversal
    /// cannot rewrite: <c>file:/run/secrets/user-&lt;id&gt;-imap</c> and any <c>../</c> written in front of it still
    /// name a file the operator called <c>user-&lt;id&gt;-imap</c>. Nothing here reads the material or the file
    /// system; what the reference reaches is proven a few lines below, by the same walk a start runs.
    /// </para>
    /// <para>
    /// What the record already holds is compared as values rather than per path, because a withdrawn mail account
    /// moves every position after it: the reference that stood at <c>MailAccounts:2</c> is at <c>MailAccounts:1</c>
    /// afterwards, and a per-path reading would report the shift as a reference somebody wrote.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> FindSecretsTheUserMayNotName(
        MailUserId user,
        string standingJson,
        string candidateJson)
    {
        var held = SecretValuesOf(RedactedDocumentSave.Flatten(standingJson));

        return
        [
            .. RedactedDocumentSave.Flatten(candidateJson)
                .Where(setting => NamesASecret(setting.Key)
                    && !held.Contains(setting.Value)
                    && !NamesMaterialProvisionedFor(user, setting.Value))
                .Select(setting => setting.Key)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                    $"{path} names a secret that was not provisioned for you: a reference is a path into what this deployment can read, and the mail server it would be presented to is yours. Name material this deployment holds for you — its own name begins with '{CredentialPrefixFor(user)}' — or ask whoever administers this deployment to declare the mailbox with 'mfctl user account add'."),
        ];
    }

    /// <summary>Reports whether a reference names material an operator provisioned for this user and nobody else.</summary>
    private static bool NamesMaterialProvisionedFor(MailUserId user, string configuredValue) =>
        SecretReference.TryParse(configuredValue, out var reference, out _)
        && LastSegmentOf(reference.Target)
            .StartsWith(CredentialPrefixFor(user), StringComparison.OrdinalIgnoreCase);

    /// <summary>Names what every credential provisioned for one user is called, whichever scheme delivers it.</summary>
    private static string CredentialPrefixFor(MailUserId user) => $"user-{user.Value:D}-";

    /// <summary>Reads the part of a reference's target that names the material rather than where it is kept.</summary>
    private static string LastSegmentOf(string target) => target[(target.LastIndexOfAny(['/', '\\']) + 1)..];

    /// <summary>Reads the values every secret-bearing setting of a flattened record carries.</summary>
    private static HashSet<string> SecretValuesOf(Dictionary<string, string> document) =>
    [
        .. document.Where(setting => NamesASecret(setting.Key)).Select(setting => setting.Value),
    ];

    /// <summary>Reports whether a configuration path names a secret, which is decided by its last segment alone.</summary>
    private static bool NamesASecret(string path) => SecretPropertyNaming.NamesASecret(path.Split(':')[^1]);

    /// <summary>Names every mail account of the candidate that another user this deployment serves already answers to.</summary>
    /// <remarks>
    /// <para>
    /// The deployment-wide rule <c>DeclaredUsers</c> states for a file, asked again of a record so that the two cannot
    /// disagree: a mail account belongs to its user, but this release resolves an account's settings by its identifier
    /// alone, so a name two users share would reach whichever of the two the lookup met first. It is asked of the
    /// published runtime roster rather than of every record the deployment holds, because the roster is what those
    /// lookups actually resolve through — and reading everybody's document per write would be a query about other
    /// people's records on every change to one, which is the shape the reader beside this deliberately does not have.
    /// </para>
    /// <para>
    /// Writes through this process are serialized from this check through publication, so each one sees the last one.
    /// Two replicas can still commit conflicting user documents independently; <c>ServedMailUsersStartupGate</c>
    /// holds the deployment-wide guarantee by composing every record together and refusing the next start.
    /// </para>
    /// </remarks>
    private IReadOnlyList<string> FindNamesHeldByAnotherUser(
        MailUserId user,
        IReadOnlyList<MailSynchronizationAccountOptions> candidate)
    {
        var held = servedUsers.Users
            .Where(served => served.User != user)
            .SelectMany(served => served.Source == MailUserAccountSource.DeploymentSection
                ? configured.DeclaredFor(served.User)
                : served.MailAccounts)
            .SelectMany(NamesOf)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var shared = candidate
            .SelectMany(NamesOf)
            .Where(held.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return shared.Length == 0
            ? []
            :
            [
                $"Another user this deployment serves already names a mail account {string.Join(", ", shared)}. A mail account belongs to its user, but this release resolves an account's settings by its identifier alone, so a name two users share would reach whichever of the two the lookup met first. Choose a name no other user uses.",
            ];
    }

    /// <summary>Names the strings one declaration makes a mail account answer to.</summary>
    private static IEnumerable<string> NamesOf(MailSynchronizationAccountOptions account) => new[]
        {
            MailSynchronizationOptions.TryReadAccountId(account.AccountId),
            string.IsNullOrWhiteSpace(account.DisplayName) ? null : account.DisplayName.Trim(),
        }
        .OfType<string>();

    /// <summary>Reads where one user's mail accounts come from, which the roster answers for this gate and for the label's.</summary>
    private MailUserAccountSource SourceOf(MailUserId user) => servedUsers.SourceFor(user);

    /// <summary>The path a refusal about a user's own record names, which is the record rather than a file.</summary>
    /// <remarks>The same word the startup gate uses for a user read from their own document, because an operator reading either one is being told there is no configuration key to go and correct.</remarks>
    private const string RecordPath = "document";

    private static void RequireNamed(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user record is administered for a named user.", nameof(user));
        }
    }

    /// <summary>The record a change was opened over, and the refusal that stopped it going further.</summary>
    /// <param name="Record">The record as the row holds it, which every refusal reports the version of.</param>
    /// <param name="Refusal">The refusal, or <see langword="null" /> when the change may be composed.</param>
    private readonly record struct OpenedRecord(UserSettingsDocument Record, UserRecordWriteOutcome? Refusal);
}
