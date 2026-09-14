// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Application.Configuration;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.Signals;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>What is done to one user's own record, by an administrator or by that user.</summary>
/// <remarks>
/// <para>
/// Every change here produces a candidate record and puts it through the one binder both directions share, so a record
/// a write accepts is a record the next start would read. Nothing patches the row in place: the caller states an act —
/// a saved document, a portrait linked — and what the act composes is judged whole, with the mail accounts assigned
/// to the user composed back in. The accounts themselves are records of their own, which
/// <see cref="MailAccountAdministration" /> writes.
/// </para>
/// <para>
/// Two callers reach it and the pairs of entry points are what separate them. An administrator names the user and
/// holds an administrative grant; a user names nobody and holds one of their own, so their entry points resolve the
/// user from the principal and there is no argument for a request to put another user's identifier in. Each pair
/// delegates to the same private work, which is where the rules live, so the two callers cannot come to be judged
/// differently.
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
    IStoredFileStore files,
    ConfigurationChangeAnnouncements announcements)
{
    /// <summary>How many times a portrait link is composed again over a record another write moved underneath it.</summary>
    private const int MaximumRelinkAttempts = 3;

    /// <summary>What a save refused over a redaction marker it cannot place is sent to.</summary>
    private const string NarrowerChange =
        "state the setting afresh rather than leaving the redaction marker in its place.";

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

    /// <summary>Keeps one user off either mail-serving endpoint, or lets them back on, leaving a switch not named where it is.</summary>
    /// <param name="user">The user whose switches are written.</param>
    /// <param name="mcpEndpoint">Whether they are served on the MCP endpoint from now on, or <see langword="null" /> to leave it.</param>
    /// <param name="clientEndpoint">Whether they are served on the client endpoint from now on, or <see langword="null" /> to leave it.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>What the write did and the switches the record states afterwards, or <see langword="null" /> when this deployment holds no such user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" />.</exception>
    /// <remarks>
    /// <para>
    /// A keyed change to the record rather than a statement of its own, so the switch lands where a saved record would put
    /// it and a later edit of the whole record reads it back. It is composed over whatever version stands, because no
    /// other setting decides it; a writer committing in between is still refused rather than overwritten.
    /// </para>
    /// <para>
    /// The configuration grant rather than the erasing one, because it decides where the deployment serves somebody
    /// rather than disposing of anything they hold: no mail, credential, or session is removed, and a switch turned
    /// back on serves them again with what they already had.
    /// </para>
    /// </remarks>
    internal async Task<UserEndpointAccessWrite?> SetEndpointAccessAsync(
        MailUserId user,
        bool? mcpEndpoint,
        bool? clientEndpoint,
        CancellationToken cancellationToken)
    {
        RequireNamed(user);
        authorization.RequirePermission(MailFathomPermission.AdminConfigurationWrite);

        if (await documents.ReadAsync(user, cancellationToken) is not { } inForce)
        {
            return null;
        }

        MailUserEndpointAccess standing;

        try
        {
            standing = UserEndpointAccessOptions.ReadFrom(RedactedDocumentSave.Flatten(inForce.Json));
        }
        catch (Exception refused)
            when (refused is FormatException or System.Text.Json.JsonException or InvalidDataException)
        {
            // The parser's own message names the path it stopped at, composed from the row's own key names — which for a
            // user's record are their mailboxes — so the refusal says what to do rather than repeating it.
            return new UserEndpointAccessWrite(
                UserRecordWriteOutcome.Refused(
                    MailFathomErrorCode.ConfigurationCandidateInvalid,
                    inForce.Version,
                    ["This user's record is not a document of settings, so its endpoint switches cannot be written. Correct the row where it was written."]),
                default);
        }

        var requested = new MailUserEndpointAccess(
            mcpEndpoint ?? standing.McpEndpoint,
            clientEndpoint ?? standing.ClientEndpoint);

        if (requested == standing)
        {
            return new UserEndpointAccessWrite(
                UserRecordWriteOutcome.NothingToChange(
                    inForce.Version,
                    $"The user's record already states these switches, so nothing was written and version {inForce.Version} stays in force."),
                standing);
        }

        ConfigurationEdit[] edits =
        [
            .. new (bool? Value, string Key)[]
                {
                    (mcpEndpoint, UserEndpointAccessOptions.McpEndpointKey),
                    (clientEndpoint, UserEndpointAccessOptions.ClientEndpointKey),
                }
                .Where(named => named.Value is not null)
                .Select(named => ConfigurationEdit.SetTo(named.Key, named.Value!.Value ? "true" : "false")),
        ];

        var outcome = await this.JudgeAndCommitAsync(
            user,
            inForce,
            SettingsDocumentPatch.Apply(inForce.Json, edits),
            UserRecordAuthority.Administrator,
            UserRecordArrival.AlreadyHeld,
            cancellationToken);

        return outcome is null
            ? null
            : new UserEndpointAccessWrite(outcome, outcome.IsCommitted ? requested : standing);
    }

    /// <summary>Points the signed-in user's record at one of their stored files as the portrait they are drawn by, or at none.</summary>
    /// <param name="portrait">The file to link, or <see langword="null" /> to link none.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>Whether the record was held, and the file the link displaced, if any.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the record refused the link for a reason a retry does not settle.</exception>
    /// <remarks>
    /// <para>
    /// Asks for the grant the portrait use case is published under and takes the user from the caller, as every other
    /// entry point here does, so nothing that resolves this service can rewrite another user's record through it.
    /// </para>
    /// <para>
    /// Composed over whatever version is in force rather than over one a caller read, because nobody authored this change
    /// against a version: a person uploading a picture has no record buffer open. A write that moved the record between
    /// the read and the commit is therefore composed over again rather than reported, a bounded number of times.
    /// </para>
    /// <para>
    /// Judged as a record already held rather than as one being written. The link is the only thing that changes, and a
    /// record committed before a rule a write is held to existed must not stop a person replacing their picture.
    /// </para>
    /// </remarks>
    internal async Task<PortraitRelinking> RelinkOwnPortraitAsync(
        StoredFileId? portrait,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailRead);

        var user = authorization.RequireUser();

        for (var attempt = 1; ; attempt++)
        {
            if (await documents.ReadAsync(user, cancellationToken) is not { } inForce)
            {
                return PortraitRelinking.NoSuchUser;
            }

            var displaced = OwnPortraitLinks.PortraitOf(inForce.Json);

            if (displaced == portrait)
            {
                return new PortraitRelinking(UserHeld: true, Replaced: null);
            }

            var edit = portrait is { } linked
                ? ConfigurationEdit.SetTo(nameof(UserAccountOptions.Portrait), linked.ToString())
                : ConfigurationEdit.Removing(nameof(UserAccountOptions.Portrait));

            var outcome = await this.JudgeAndCommitAsync(
                user,
                inForce,
                SettingsDocumentPatch.Apply(inForce.Json, [edit]),
                UserRecordAuthority.User,
                UserRecordArrival.AlreadyHeld,
                cancellationToken);

            if (outcome is null)
            {
                return PortraitRelinking.NoSuchUser;
            }

            if (outcome.IsSettled)
            {
                return new PortraitRelinking(UserHeld: true, Replaced: displaced);
            }

            if (outcome.Refusal != MailFathomErrorCode.ConfigurationVersionSuperseded || attempt == MaximumRelinkAttempts)
            {
                throw new InvalidOperationException(
                    $"The user record refused the portrait link: {string.Join(" ", outcome.Messages)}");
            }
        }
    }

    /// <summary>Reads one user's record, redacted.</summary>
    private async Task<UserRecordReading?> ReadAsync(MailUserId user, CancellationToken cancellationToken) =>
        await documents.ReadAsync(user, cancellationToken) is { } record
            ? new UserRecordReading(
                user,
                record.DisplayName,
                SettingRedaction.ApplyToDocument(record.Json),
                record.Version)
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

            if (saved.Keys.Any(NamesAMailAccount))
            {
                return UserRecordWriteOutcome.Refused(
                    MailFathomErrorCode.ConfigurationCandidateInvalid,
                    inForce.Version,
                    [
                        $"The saved record names {MailAccountRecordComposition.MailAccountsProperty}, and a mail account is a record of its own rather than part of a user's. Remove it, and change an account with 'mfctl account edit'.",
                    ]);
            }

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
            UserRecordArrival.BeingWritten,
            cancellationToken);
    }

    /// <summary>Reads the record a change is composed over, and refuses a change authored over a version no longer in force.</summary>
    /// <remarks>
    /// The version is checked here as well as in the statement, so an edit authored against a record somebody else has
    /// replaced is refused before a candidate is composed and bound rather than after.
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
        UserRecordArrival arrival,
        CancellationToken cancellationToken)
    {
        // Judged with the accounts assigned to the user composed back in, because that is the record a start serves them
        // from: a language or a scanning posture is judged against the mailboxes it applies to.
        var binding = binder.Bind(MailAccountRecordComposition.Compose(candidateJson, inForce.MailAccounts), arrival);

        if (binding.User is not { } bound)
        {
            return UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                inForce.Version,
                binding.Refusals);
        }

        // Which endpoints somebody is served on is the deployment's decision about them, so a user saving their own
        // record may carry the switches through unchanged and may not move either one.
        if (authority == UserRecordAuthority.User
            && bound.EndpointAccess.Access
                != UserEndpointAccessOptions.ReadFrom(RedactedDocumentSave.Flatten(inForce.Json)))
        {
            return UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                inForce.Version,
                [
                    $"{UserEndpointAccessOptions.BlockName} states which endpoints this deployment serves you on, and whoever administers it decides that. Leave it as your record already states it.",
                ]);
        }

        if (authority == UserRecordAuthority.User
            && FindSecretsTheUserMayNotName(user, inForce.Json, candidateJson) is { Count: > 0 } introduced)
        {
            return UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                inForce.Version,
                introduced);
        }

        // Asked of every write whoever makes it, because only the database can say whose a file is: a link to somebody
        // else's file would serve their octets as this user's picture.
        if (bound.Portrait is { } portraitLink
            && (portraitLink == Guid.Empty
                || !await files.HoldsAsync(user, StoredFileId.Create(portraitLink), cancellationToken)))
        {
            return UserRecordWriteOutcome.Refused(
                MailFathomErrorCode.ConfigurationCandidateInvalid,
                inForce.Version,
                [
                    $"{nameof(UserAccountOptions.Portrait)} names '{portraitLink:D}', which is not a stored file of this user's, so nothing was written. A portrait is linked by uploading one, never by naming a file.",
                ]);
        }

        // Reported rather than refused. A user's own record carries no mail account, so a write to it cannot have
        // introduced a credential its accounts cannot use; refusing over one would block every unrelated edit without
        // making the next start any better. What it can do is say so, since the next start refuses the deployment.
        var unusable = await secrets.FindUserMailAccountErrorsAsync(RecordPath, bound.MailAccounts, cancellationToken);

        UserRecordWriteOutcome? outcome;
        await servedUsers.WaitForRosterPublicationAsync(cancellationToken);

        try
        {
            if (await store.CommitAsync(
                    user,
                    candidateJson,
                    bound.EndpointAccess.Access,
                    inForce.Version,
                    cancellationToken) is { } committed)
            {
                servedUsers.UserDocumentPublished(user, inForce.DisplayName, bound, committed);
                outcome = UserRecordWriteOutcome.Committed(committed, [.. unusable.Select(DescribeAsAlreadyHeld)]);
            }
            else
            {
                // The record moved while this candidate was being judged, or the user was erased under it. Which of the
                // two is settled by reading rather than assumed, because the statement distinguishes neither.
                outcome = await documents.ReadAsync(user, cancellationToken) is { } current
                    ? UserRecordWriteOutcome.Refused(
                        MailFathomErrorCode.ConfigurationVersionSuperseded,
                        current.Version,
                        [
                            $"The change was composed over user record version {inForce.Version}, and version {current.Version} is in force. Read the record as it now stands and decide again against it.",
                        ])
                    : null;
            }
        }
        finally
        {
            servedUsers.ReleaseRosterPublication();
        }

        // Announced once the roster is released: nothing on this replica waits for the announcement, and a backplane
        // slow to answer would otherwise hold every other roster write and the convergence reading behind it.
        if (outcome is { IsCommitted: true })
        {
            await announcements.AnnounceAsync();
        }

        return outcome;
    }

    /// <summary>Says that a problem a committed record still carries was there before the write, and what clears it.</summary>
    internal static string DescribeAsAlreadyHeld(string problem) =>
        $"{problem} The record already carried this before the change, so the change was committed and left it as it was. A start refuses a record carrying it, so correct it before the deployment next restarts: provision what the reference names, or state the credential afresh with 'mfctl account edit'.";

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
    internal static IReadOnlyList<string> FindSecretsTheUserMayNotName(
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
                    $"{path} names a secret that was not provisioned for you: a reference is a path into what this deployment can read, and the mail server it would be presented to is yours. Name material this deployment holds for you — its own name begins with '{CredentialPrefixFor(user)}' — or ask whoever administers this deployment to declare the mailbox with 'mfctl account add'."),
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

    /// <summary>Reports whether a configuration path lies within the mail accounts a record no longer carries.</summary>
    private static bool NamesAMailAccount(string path) =>
        path.Equals(MailAccountRecordComposition.MailAccountsProperty, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith($"{MailAccountRecordComposition.MailAccountsProperty}:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reports whether a configuration path names a secret, which is decided by its last segment alone.</summary>
    private static bool NamesASecret(string path) => SecretPropertyNaming.NamesASecret(path.Split(':')[^1]);

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
