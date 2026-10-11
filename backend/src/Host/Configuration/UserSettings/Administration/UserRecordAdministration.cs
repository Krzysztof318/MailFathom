// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Application.Access;
using MailFathom.Application.Configuration;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Signals;
using MailFathom.Infrastructure.Persistence.Policies;
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
/// differently. An administrator's entry points are held at a scope as well: a user the administrator's scope does not
/// cover is answered as one this deployment does not hold.
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
/// A record is judged as the settings policies governing its user resolve it, and stored as what somebody stated on
/// it. So the binder and every rule beside it read the effective record — a forced value in place of the record's
/// own, a default where the record states nothing — while the row keeps the own value, which is what comes back when
/// the force lifts. A change to a property a policy holds against whoever is writing is refused naming the property
/// and whether the organization or the deployment holds it, and one that carries the property through as a read
/// showed it passes. <see cref="GovernedRecordChange" /> holds that rule and <see cref="GovernedRecordResolution" />
/// the order a value resolves in.
/// </para>
/// <para>
/// Which policies govern a record is decided where the write commits rather than where it was read: the store hands
/// the policies in force to the judgement inside its own transaction, under the lock a policy write excludes record
/// writes with. A replica that has not yet adopted a policy change therefore cannot commit a record judged against
/// the policy it still holds, nor project endpoint switches derived from it.
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
    ServedUsers servedUsers,
    IStoredFileStore files,
    ConfigurationChangeAnnouncements announcements)
{
    /// <summary>How many times a one-property edit is composed again over a record another write moved underneath it.</summary>
    private const int MaximumRelinkAttempts = 3;

    /// <summary>The properties a person changes through a route of their own, which the grant that read the record already reaches.</summary>
    private static readonly string[] WrittenUnderTheReadGrant =
    [
        nameof(UserAccountOptions.TimeZone),
        nameof(UserAccountOptions.Portrait),
    ];

    /// <summary>What a save refused over a redaction marker it cannot place is sent to.</summary>
    private const string NarrowerChange =
        "state the setting afresh rather than leaving the redaction marker in its place.";

    /// <summary>Reads one user's record as an administrator sees it.</summary>
    /// <param name="user">The user asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The record, or <see langword="null" /> when this deployment holds no such user or the caller's scope does not cover them.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminRead" /> at every scope.</exception>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the deployment holds the record and it could not be handed on.</exception>
    /// <exception cref="FormatException">Thrown when the row is JSON but not an object of settings.</exception>
    /// <exception cref="JsonException">Thrown when the row is not JSON, or is nested past what a document may be.</exception>
    internal async Task<UserRecordReading?> ReadRecordAsync(UserId user, CancellationToken cancellationToken)
    {
        RequireNamed(user);
        authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRead);

        if (!await authorization.PermitsOverAsync(MailFathomPermission.AdminRead, user, cancellationToken))
        {
            return null;
        }

        var writes = await authorization.PermitsOverAsync(
            MailFathomPermission.AdminConfigurationWrite,
            user,
            cancellationToken);

        return await this.ReadAsync(user, property => writes && property.IsAnAdministratorsToChange, cancellationToken);
    }

    /// <summary>Reads the signed-in user's own record.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The record, or <see langword="null" /> when this deployment holds no record for the acting user.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the deployment holds the record and it could not be handed on.</exception>
    internal Task<UserRecordReading?> ReadOwnRecordAsync(CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailRead);

        var writesTheRecord = authorization.Permits(MailFathomPermission.MailAccountsWrite);

        return this.ReadAsync(
            authorization.RequireUser(),
            property => property.IsThePersonsToChange
                && (writesTheRecord || WrittenUnderTheReadGrant.Contains(property.Property.Path)),
            cancellationToken);
    }

    /// <summary>Applies a whole edited record to one user.</summary>
    /// <param name="user">The user whose record is written.</param>
    /// <param name="documentJson">The record the caller saved.</param>
    /// <param name="expectedVersion">The version the buffer was opened over.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>What the write did, or <see langword="null" /> when this deployment holds no such user or the caller's scope does not cover them.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="documentJson" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" /> at every scope.</exception>
    internal async Task<UserRecordWriteOutcome?> ApplyRecordAsync(
        UserId user,
        string documentJson,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        RequireNamed(user);
        ArgumentNullException.ThrowIfNull(documentJson);
        authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminConfigurationWrite);

        if (!await authorization.PermitsOverAsync(MailFathomPermission.AdminConfigurationWrite, user, cancellationToken))
        {
            return null;
        }

        return await this.SaveAsync(
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
    /// <returns>What the write did and the switches the user is served on afterwards, or <see langword="null" /> when this deployment holds no such user or the caller's scope does not cover them.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller's grant omits <see cref="MailFathomPermission.AdminConfigurationWrite" /> at every scope.</exception>
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
    /// <para>
    /// A switch a settings policy forces is not an administrator's to move either, so asking for the other position is
    /// refused naming who holds it. The switches answered are the ones the user is served on, which are the policy's
    /// where it forces them and the record's everywhere else.
    /// </para>
    /// </remarks>
    internal async Task<UserEndpointAccessWrite?> SetEndpointAccessAsync(
        UserId user,
        bool? mcpEndpoint,
        bool? clientEndpoint,
        CancellationToken cancellationToken)
    {
        RequireNamed(user);
        authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminConfigurationWrite);

        if (!await authorization.PermitsOverAsync(MailFathomPermission.AdminConfigurationWrite, user, cancellationToken)
            || await documents.ReadAsync(user, cancellationToken) is not { } inForce)
        {
            return null;
        }

        UserEndpointAccess standing;

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

        var resolved = ResolveUnderItsPolicies(inForce);
        var served = resolved is null
            ? standing
            : UserEndpointAccessOptions.ReadFrom(RedactedDocumentSave.Flatten(resolved.EffectiveJson));

        // Asked against what the user is served on rather than what their record states. A switch a policy's default
        // turns off is one the record never stated, and reading the record alone would call it already on.
        var requested = new UserEndpointAccess(
            mcpEndpoint ?? served.McpEndpoint,
            clientEndpoint ?? served.ClientEndpoint);

        if (requested == served)
        {
            return new UserEndpointAccessWrite(
                UserRecordWriteOutcome.NothingToChange(
                    inForce.Version,
                    $"The user is already served on these switches, so nothing was written and version {inForce.Version} stays in force."),
                served);
        }

        (string Key, bool Asked)[] moved =
        [
            .. new (string Key, bool Asked, bool Served)[]
                {
                    (UserEndpointAccessOptions.McpEndpointKey, requested.McpEndpoint, served.McpEndpoint),
                    (UserEndpointAccessOptions.ClientEndpointKey, requested.ClientEndpoint, served.ClientEndpoint),
                }
                .Where(named => named.Asked != named.Served)
                .Select(named => (named.Key, named.Asked)),
        ];

        // Refused here rather than left to the judgement of the document: a record already stating the position asked
        // for carries it through unchanged, which would answer a switch a policy forces the other way as settled.
        string[] held =
        [
            .. moved
                .Select(named => resolved?.Find(named.Key).FindRefusal(byThePerson: false))
                .OfType<string>(),
        ];

        if (held.Length > 0)
        {
            return new UserEndpointAccessWrite(UserRecordWriteOutcome.Held(inForce.Version, held), served);
        }

        var write = await this.JudgeAndCommitAsync(
            user,
            inForce,
            SettingsDocumentPatch.Apply(
                inForce.Json,
                [.. moved.Select(named => ConfigurationEdit.SetTo(named.Key, named.Asked ? "true" : "false"))]),
            UserRecordAuthority.Administrator,
            rewritesTheWholeRecord: false,
            cancellationToken);

        return write.Outcome is { } outcome
            ? new UserEndpointAccessWrite(outcome, write.Committed?.EndpointAccess.Access ?? served)
            : null;
    }

    /// <summary>Records the zone the signed-in person states their own days are read in.</summary>
    /// <param name="zone">The zone, already resolved against the zones this host knows.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>Whether this deployment holds a record for them; the zone they are served once the record is as they asked, which is the one a settings policy forces where one does rather than the one asked for; and, where the change was refused, the sentence naming who holds the zone where a settings policy does not leave it to them, or naming the policy where a settings policy governing them is one this build does not read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zone" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the record refused the zone for a reason a retry does not settle.</exception>
    /// <remarks>
    /// <para>
    /// Asked for under the grant a signed-in person already holds rather than under the record's own write, for the
    /// reason the portrait above is: what the zone decides is how that person's own days are read, which is not a
    /// decision about which mailboxes this deployment connects to and under whose credentials. Somebody whose mailboxes
    /// an administrator maintains still reads their mail in their own week.
    /// </para>
    /// <para>
    /// Composed over whatever version is in force rather than over one a caller read, and judged as a record already
    /// held, both for the reasons <see cref="RelinkOwnPortraitAsync" /> gives: nobody authored this change against a
    /// version, and a record committed before a rule a write is held to existed must not stop a person correcting
    /// their zone.
    /// </para>
    /// <para>
    /// A record already stating the zone is answered without a commit, as <see cref="RelinkOwnPortraitAsync" /> answers
    /// an unchanged portrait: a re-submitted or double-clicked choice would otherwise bump the record's version and
    /// have every replica recompose that user for a change that changed nothing.
    /// </para>
    /// </remarks>
    internal async Task<OwnSettingChange> ChangeOwnTimeZoneAsync(UserTimeZone zone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zone);

        authorization.RequirePermission(MailFathomPermission.MailRead);

        var user = authorization.RequireUser();

        for (var attempt = 1; ; attempt++)
        {
            if (await documents.ReadAsync(user, cancellationToken) is not { } inForce)
            {
                return OwnSettingChange.NoSuchUser;
            }

            var standing = ResolveUnderItsPolicies(inForce);

            // A record nothing resolves is left to the judgement below, which holds it and says which policy does.
            if (standing is not null && StatedTimeZoneIdOf(inForce.Json) == zone.Id)
            {
                return OwnSettingChange.SettledServing(ServedZoneIdOf(standing, zone));
            }

            var outcome = (await this.JudgeAndCommitAsync(
                user,
                inForce,
                SettingsDocumentPatch.Apply(
                    inForce.Json,
                    [ConfigurationEdit.SetTo(nameof(UserAccountOptions.TimeZone), zone.Id)]),
                UserRecordAuthority.User,
                rewritesTheWholeRecord: false,
                cancellationToken)).Outcome;

            if (outcome is null)
            {
                return OwnSettingChange.NoSuchUser;
            }

            if (outcome.IsSettled)
            {
                return OwnSettingChange.SettledServing(ServedZoneIdOf(standing, zone));
            }

            if (outcome.IsHeld)
            {
                return OwnSettingChange.Refused(string.Join(" ", outcome.Messages));
            }

            if (outcome.Refusal != MailFathomErrorCode.ConfigurationVersionSuperseded || attempt == MaximumRelinkAttempts)
            {
                throw new InvalidOperationException(
                    $"The user record refused the time zone: {string.Join(" ", outcome.Messages)}");
            }
        }
    }

    /// <summary>Points the signed-in user's record at one of their stored files as the portrait they are drawn by, or at none.</summary>
    /// <param name="portrait">The file to link, or <see langword="null" /> to link none.</param>
    /// <param name="cancellationToken">Cancels the reads and the commit.</param>
    /// <returns>Whether the record was held, the file the link displaced, if any, and, where the link was refused, the sentence naming who holds the portrait where a settings policy does not leave it to them, or naming the policy where a settings policy governing them is one this build does not read.</returns>
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

            var outcome = (await this.JudgeAndCommitAsync(
                user,
                inForce,
                SettingsDocumentPatch.Apply(inForce.Json, [edit]),
                UserRecordAuthority.User,
                rewritesTheWholeRecord: false,
                cancellationToken)).Outcome;

            if (outcome is null)
            {
                return PortraitRelinking.NoSuchUser;
            }

            if (outcome.IsSettled)
            {
                return new PortraitRelinking(UserHeld: true, Replaced: displaced);
            }

            if (outcome.IsHeld)
            {
                return new PortraitRelinking(UserHeld: true, Replaced: null) { Refusal = string.Join(" ", outcome.Messages) };
            }

            if (outcome.Refusal != MailFathomErrorCode.ConfigurationVersionSuperseded || attempt == MaximumRelinkAttempts)
            {
                throw new InvalidOperationException(
                    $"The user record refused the portrait link: {string.Join(" ", outcome.Messages)}");
            }
        }
    }

    /// <summary>Reads one user's record, redacted, with each of its properties as the policies governing the user resolve it.</summary>
    /// <remarks>
    /// The document answered stays the record's own, because that is what a save is composed over and what a lifted
    /// force brings back. What the user is served with is answered beside it, per property.
    /// </remarks>
    private async Task<UserRecordReading?> ReadAsync(
        UserId user,
        Func<ResolvedProperty, bool> isTheReadersToChange,
        CancellationToken cancellationToken)
    {
        if (await documents.ReadAsync(user, cancellationToken) is not { } record)
        {
            return null;
        }

        var layers = SettingsPolicyLayers.Read(record.Policies, SettingsPolicySection.Users, out var unreadable);
        ResolvedProperty[] properties = layers is null
            ? []
            :
            [
                .. GovernedRecordResolution.Resolve(SettingsPolicySection.Users, layers, record.Json).Properties
                    // The name is a column beside the document, so the resolution holds no value for it and
                    // the envelope read with the record does.
                    .Select(property => property.Property.Path == SettingsPolicySection.UserDisplayNameColumn
                        ? property with { EffectiveValue = JsonValue.Create(record.DisplayName) }
                        : property),
            ];

        return new UserRecordReading(
            user,
            record.DisplayName,
            SettingRedaction.ApplyToDocument(record.Json),
            record.Version)
        {
            Properties = properties,
            Changeable = [.. properties.Where(isTheReadersToChange).Select(property => property.Property.Path)],
            Unresolved = layers is null ? SettingsPolicyLayers.DescribeUnreadable(unreadable) : null,
        };
    }

    /// <summary>Answers the zone a person is read in once their record states the one they asked for.</summary>
    /// <returns>The zone a settings policy forces where one does, and otherwise the zone asked for, which the record now states.</returns>
    /// <remarks>
    /// A person may state their record's own zone under a force, which is carried through and writes nothing, so
    /// answering what they asked for would show them a zone no reader of theirs uses.
    /// </remarks>
    private static string ServedZoneIdOf(ResolvedRecord? standing, UserTimeZone asked) =>
        standing?.Find(nameof(UserAccountOptions.TimeZone)) is { ForcedBy: not null, EffectiveValue: { } forced }
            ? forced.ToString()
            : asked.Id;

    /// <summary>Resolves a record through the policies it was read with.</summary>
    /// <returns>The record as it resolves, or <see langword="null" /> where a policy governing the user is not one this build reads.</returns>
    private static ResolvedRecord? ResolveUnderItsPolicies(UserSettingsDocument record) =>
        SettingsPolicyLayers.Read(record.Policies, SettingsPolicySection.Users, out _) is { } layers
            ? GovernedRecordResolution.Resolve(SettingsPolicySection.Users, layers, record.Json)
            : null;

    /// <summary>Applies a saved record as the difference between what it says and what the row holds.</summary>
    /// <remarks>
    /// The buffer becomes keyed changes rather than replacing the document wholesale, so one vocabulary reaches the
    /// commit whichever surface stated the change — and so a value left at the redaction marker leaves the reference
    /// beneath it exactly as it was rather than persisting the marker over somebody's credential.
    /// </remarks>
    private async Task<UserRecordWriteOutcome?> SaveAsync(
        UserId user,
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

        return (await this.JudgeAndCommitAsync(
            user,
            inForce,
            SettingsDocumentPatch.Apply(inForce.Json, edits),
            authority,
            rewritesTheWholeRecord: true,
            cancellationToken)).Outcome;
    }

    /// <summary>Reads the record a change is composed over, and refuses a change authored over a version no longer in force.</summary>
    /// <remarks>
    /// The version is checked here as well as in the statement, so an edit authored against a record somebody else has
    /// replaced is refused before a candidate is composed and bound rather than after.
    /// </remarks>
    private async Task<OpenedRecord?> OpenAsync(
        UserId user,
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

    /// <summary>Judges the candidate against what the deployment already serves and the policies governing the user, and commits it.</summary>
    /// <remarks>
    /// <para>
    /// The binder is the same one a start reads a record with, so what a write accepts is what the next start would
    /// read. What it cannot answer is asked beside it: the rule that is about the deployment rather than about the
    /// record, the rule that is about who is writing, and whether the secrets the record names can actually be
    /// retrieved — which the binder cannot ask because it resolves nothing, and which a start refuses for the whole
    /// deployment rather than for the user whose record carries the reference.
    /// </para>
    /// <para>
    /// The candidate is judged twice where a policy moved in between. Once under the policies the record was read
    /// with, because two of the questions here are asked of the database and of the secret stores and neither may be
    /// asked inside a commit; and once more inside the commit, under the policies in force at that moment, which is
    /// the judgement that decides. Where the two readings are the same policies the first judgement is the second.
    /// </para>
    /// </remarks>
    private async Task<JudgedWrite> JudgeAndCommitAsync(
        UserId user,
        UserSettingsDocument inForce,
        string candidateJson,
        UserRecordAuthority authority,
        bool rewritesTheWholeRecord,
        CancellationToken cancellationToken)
    {
        var judged = this.Judge(user, inForce, candidateJson, authority, rewritesTheWholeRecord, inForce.Policies);

        if (judged.Refusal is { } refusal)
        {
            return new JudgedWrite(refusal, Committed: null);
        }

        if (judged.Bound is not { } bound)
        {
            return new JudgedWrite(NothingHeldMoves(inForce.Version), Committed: null);
        }

        // Asked of every write whoever makes it, because only the database can say whose a file is: a link to somebody
        // else's file would serve their octets as this user's picture.
        if (bound.Portrait is { } portraitLink
            && (portraitLink == Guid.Empty
                || !await files.HoldsAsync(user, StoredFileId.Create(portraitLink), cancellationToken)))
        {
            return new JudgedWrite(
                UserRecordWriteOutcome.Refused(
                    MailFathomErrorCode.ConfigurationCandidateInvalid,
                    inForce.Version,
                    [
                        $"{nameof(UserAccountOptions.Portrait)} names '{portraitLink:D}', which is not a stored file of this user's, so nothing was written. A portrait is linked by uploading one, never by naming a file.",
                    ]),
                Committed: null);
        }

        // Reported rather than refused. A user's own record carries no mail account, so a write to it cannot have
        // introduced a credential its accounts cannot use; refusing over one would block every unrelated edit without
        // making the next start any better. What it can do is say so, since the next start refuses the deployment.
        var unusable = await secrets.FindUserMailAccountErrorsAsync(RecordPath, bound.MailAccounts, cancellationToken);

        var committedAs = judged;
        var committedUnder = inForce.Policies;

        var committed = await store.CommitAsync(
            user,
            inForce.Version,
            policies =>
            {
                committedUnder = policies;
                committedAs = policies == inForce.Policies
                    ? judged
                    : this.Judge(user, inForce, candidateJson, authority, rewritesTheWholeRecord, policies);

                return committedAs is { Refusal: null, Bound: { } resolved }
                    ? new UserRecordCandidate(committedAs.Json, resolved.EndpointAccess.Access)
                    : null;
            },
            cancellationToken);

        if (committed is { } version)
        {
            servedUsers.UserDocumentPublished(user, inForce.DisplayName, committedAs.Bound!, version, committedUnder);

            await announcements.AnnounceAsync();

            return new JudgedWrite(
                UserRecordWriteOutcome.Committed(version, [.. unusable.Select(DescribeAsAlreadyHeld)]),
                committedAs.Bound);
        }

        // A policy that moved between the read and the commit refuses, or holds, what the earlier one let through.
        if (committedAs.Refusal is { } refusedAtCommit)
        {
            return new JudgedWrite(refusedAtCommit, Committed: null);
        }

        if (committedAs.Bound is null)
        {
            return new JudgedWrite(NothingHeldMoves(inForce.Version), Committed: null);
        }

        // The record moved while this candidate was being judged, or the user was erased under it. Which of the
        // two is settled by reading rather than assumed, because the statement distinguishes neither.
        return new JudgedWrite(
            await documents.ReadAsync(user, cancellationToken) is { } current
                ? UserRecordWriteOutcome.Refused(
                    MailFathomErrorCode.ConfigurationVersionSuperseded,
                    current.Version,
                    [
                        $"The change was composed over user record version {inForce.Version}, and version {current.Version} is in force. Read the record as it now stands and decide again against it.",
                    ])
                : null,
            Committed: null);
    }

    /// <summary>Judges one candidate under one reading of the policies governing the user.</summary>
    /// <remarks>Reads nothing and awaits nothing, which is what lets a commit ask it again inside its own transaction.</remarks>
    private JudgedCandidate Judge(
        UserId user,
        UserSettingsDocument inForce,
        string candidateJson,
        UserRecordAuthority authority,
        bool rewritesTheWholeRecord,
        GoverningSettingsPolicies policies)
    {
        JudgedCandidate Refused(IReadOnlyList<string> messages) =>
            new(
                UserRecordWriteOutcome.Refused(MailFathomErrorCode.ConfigurationCandidateInvalid, inForce.Version, messages),
                candidateJson,
                Bound: null);

        // Held rather than refused as a candidate somebody could correct: nothing about what was sent is wrong, and
        // a route changing one setting answers a held record with the sentence where it would fail on any other
        // refusal.
        if (SettingsPolicyLayers.Read(policies, SettingsPolicySection.Users, out var unreadable) is not { } layers)
        {
            return new JudgedCandidate(
                UserRecordWriteOutcome.Held(
                    inForce.Version,
                    [$"{SettingsPolicyLayers.DescribeUnreadable(unreadable)} Nothing was written."]),
                candidateJson,
                Bound: null);
        }

        // Which endpoints somebody is served on and what their client is asked to record are the deployment's
        // decisions about them, and a policy may hold anything else: a writer may carry a held property through as a
        // read showed it and may not move it.
        var change = GovernedRecordChange.Judge(
            SettingsPolicySection.Users,
            layers,
            inForce.Json,
            candidateJson,
            byThePerson: authority == UserRecordAuthority.User);

        if (change.Refusals.Count > 0)
        {
            return new JudgedCandidate(
                UserRecordWriteOutcome.Held(inForce.Version, change.Refusals),
                candidateJson,
                Bound: null);
        }

        // Everything the candidate would have moved was a held property carried through, so the document is the one
        // already in force and committing it would move the version, and recompose the user on every replica, for a
        // change that changed nothing.
        if (!ReferenceEquals(change.Json, candidateJson)
            && SettingsDocumentTree.StateTheSame(
                GovernedRecordResolution.ReadDocument(change.Json),
                GovernedRecordResolution.ReadDocument(inForce.Json)))
        {
            return new JudgedCandidate(Refusal: null, change.Json, Bound: null);
        }

        // Judged as the policies resolve it and with the accounts assigned to the user composed back in, because that
        // is the record a start serves them from: a rule over the whole set — that two of their mailboxes cannot share
        // a name, that a junk destination resolves within them — is only askable against it. The blocks themselves
        // arrive as records the deployment already holds, because this route cannot write one — a saved record naming
        // MailAccounts is refused above — so judging them here would refuse a user's own record over a posture or a
        // language the account route is the only thing that could correct.
        var binding = binder.Bind(
            MailAccountRecordComposition.Compose(
                GovernedRecordResolution.Resolve(SettingsPolicySection.Users, layers, change.Json).EffectiveJson,
                inForce.MailAccounts),
            UserRecordArrival.AlreadyHeld);

        if (binding.User is not { } bound)
        {
            return Refused(binding.Refusals);
        }

        // Asked of a record being rewritten and of no other write, because the document is then the one in front of
        // whoever is saving and the line is theirs to add. A start and a convergence read records committed before the
        // property existed, and refusing those would refuse the start itself through the surface the line would be
        // added from; a switch or a portrait link is the deployment changing one value on such a record, and refusing
        // that would keep a user off an endpoint over a rule that arrived after their record was accepted. It is asked
        // of the record as resolved, so a language a policy supplies is one the record need not state.
        if (rewritesTheWholeRecord
            && bound.FindMissingLanguageError().FirstOrDefault()?.ErrorMessage is { } missingLanguage)
        {
            return Refused([missingLanguage]);
        }

        if (authority == UserRecordAuthority.User
            && FindSecretsTheUserMayNotName(user, inForce.Json, change.Json) is { Count: > 0 } introduced)
        {
            return Refused(introduced);
        }

        return new JudgedCandidate(Refusal: null, change.Json, bound);
    }

    /// <summary>Answers a change every part of which is held where it stands.</summary>
    private static UserRecordWriteOutcome NothingHeldMoves(long version) =>
        UserRecordWriteOutcome.NothingToChange(
            version,
            $"Every setting the change names is held where it stands, so nothing was written and version {version} stays in force.");

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
        UserId user,
        string standingJson,
        string candidateJson) =>
    [
        .. FindSecretsProvisionedForNoneOf(
                [user],
                RedactedDocumentSave.Flatten(candidateJson),
                SecretValuesOf(RedactedDocumentSave.Flatten(standingJson)))
            .Select(path =>
                $"{path} names a secret that was not provisioned for you: a reference is a path into what this deployment can read, and the mail server it would be presented to is yours. Name material this deployment holds for you — its own name begins with '{CredentialPrefixFor(user)}' — or ask whoever administers this deployment to declare the mailbox with 'mfctl account add'."),
    ];

    /// <summary>Names every secret-bearing value a mail account's settings may not carry once an administrator below the deployment has changed them.</summary>
    /// <param name="assignees">The users the account is, or would be, assigned to.</param>
    /// <param name="standingJson">The settings the account holds now.</param>
    /// <param name="candidateJson">The settings it would hold.</param>
    /// <returns>One sentence per reference provisioned for none of those users, and none where the settings are left exactly as they stand.</returns>
    /// <remarks>
    /// <para>
    /// The bound <see cref="FindSecretsTheUserMayNotName" /> puts on a user is put on whoever administers them with less
    /// than the whole deployment, for the same reason: the server a mail account names is whatever its settings say,
    /// so a reference presents what stands behind it to a machine whoever wrote them chose. An administrator of one
    /// organization is trusted with that organization's mailboxes and not with the database password, a client secret
    /// a whole workspace shares, or another organization's credentials.
    /// </para>
    /// <para>
    /// It judges every reference the settings carry rather than the ones the change introduced, which is where it is
    /// stricter than the user's bound and has to be. A user's own save reaches their folders and nothing that decides
    /// where a credential goes; an administrator's reaches the host, the port, and the token endpoint, and moving one
    /// of those beside a reference left untouched presents that reference somewhere new exactly as writing it would.
    /// Which settings decide that is not enumerated here, because a list is what a setting added later would be
    /// missing from: any change to the settings is judged, and settings left exactly as they stand are not, which is
    /// what lets such a caller assign the account or rename it. A caller holding the permission over the whole
    /// deployment is not asked this at all.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> FindSecretsANarrowerScopeMayNotName(
        IReadOnlyCollection<UserId> assignees,
        string standingJson,
        string candidateJson)
    {
        var standing = RedactedDocumentSave.Flatten(standingJson);
        var candidate = RedactedDocumentSave.Flatten(candidateJson);

        if (candidate.Count == standing.Count
            && candidate.All(setting => standing.TryGetValue(setting.Key, out var held) && held == setting.Value))
        {
            return [];
        }

        return
        [
            .. FindSecretsProvisionedForNoneOf(assignees, candidate, alreadyHeld: [])
                .Select(path =>
                    $"{path} names a secret that was not provisioned for a user this mail account is assigned to: a reference is a path into what this deployment can read, and it is presented to whichever server the account's settings name. A grant held below the deployment may change those settings only while every secret they name is material this deployment holds for one of those users — its own name begins with 'user-', that user's identifier, and '-'. Name such material, or ask an administrator holding the permission over the whole deployment to make the change."),
        ];
    }

    /// <summary>Lists the paths of the secret-bearing values that are neither held already nor provisioned for one of some users, in a stable order.</summary>
    private static IEnumerable<string> FindSecretsProvisionedForNoneOf(
        IReadOnlyCollection<UserId> users,
        Dictionary<string, string> candidate,
        HashSet<string> alreadyHeld) =>
        candidate
            .Where(setting => NamesASecret(setting.Key)
                && !alreadyHeld.Contains(setting.Value)
                && !users.Any(user => NamesMaterialProvisionedFor(user, setting.Value)))
            .Select(setting => setting.Key)
            .Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reports whether a reference names material an operator provisioned for this user and nobody else.</summary>
    private static bool NamesMaterialProvisionedFor(UserId user, string configuredValue) =>
        SecretReference.TryParse(configuredValue, out var reference, out _)
        && LastSegmentOf(reference.Target)
            .StartsWith(CredentialPrefixFor(user), StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the zone a stored record states, which is not the zone it is read in where it states none.</summary>
    /// <param name="documentJson">The record as its row holds it.</param>
    /// <returns>The identifier the record states, or <see langword="null" /> where it states none.</returns>
    /// <remarks>
    /// Read from the record a commit would be composed over rather than from the served snapshot, so what a change is
    /// compared against is what it would replace. A record stating nothing is read in UTC and still states nothing, so
    /// choosing UTC over it is a change rather than a repeat: it is what takes the record off the default, which is
    /// the whole of what a client proposes against. The key is matched case-insensitively, because that is how the
    /// configuration binder reads the same record; the value is compared as it was written, so a record carrying an
    /// identifier in another casing is rewritten in the canonical one rather than left disagreeing with it.
    /// </remarks>
    private static string? StatedTimeZoneIdOf(string documentJson)
    {
        using var document = JsonDocument.Parse(documentJson);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return document.RootElement
            .EnumerateObject()
            .Where(property => string.Equals(
                    property.Name,
                    nameof(UserAccountOptions.TimeZone),
                    StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            .Select(property => property.Value.GetString())
            .FirstOrDefault(stated => !string.IsNullOrWhiteSpace(stated));
    }

    /// <summary>Names what every credential provisioned for one user is called, whichever scheme delivers it.</summary>
    private static string CredentialPrefixFor(UserId user) => $"user-{user.Value:D}-";

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
    /// <remarks>The same word composing a served user uses for a user read from their own document, because an operator reading either one is being told there is no configuration key to go and correct.</remarks>
    private const string RecordPath = "document";

    private static void RequireNamed(UserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user record is administered for a named user.", nameof(user));
        }
    }

    /// <summary>What judging one candidate under one reading of the policies answered.</summary>
    /// <param name="Refusal">What refuses the candidate, or <see langword="null" /> where it may be committed.</param>
    /// <param name="Json">The document to commit, with every held property the candidate carried through left as the record states it.</param>
    /// <param name="Bound">The effective record the candidate binds to, or <see langword="null" /> where it was refused — or where everything it would have moved is held where it stands, which leaves nothing to commit and nothing refused.</param>
    private sealed record JudgedCandidate(UserRecordWriteOutcome? Refusal, string Json, UserAccountOptions? Bound);

    /// <summary>What one write did, and the effective record it committed.</summary>
    /// <param name="Outcome">What the write did, or <see langword="null" /> when this deployment no longer holds the user.</param>
    /// <param name="Committed">The record the user is served from after a commit, as the policies in force at the commit resolved it.</param>
    private readonly record struct JudgedWrite(UserRecordWriteOutcome? Outcome, UserAccountOptions? Committed);

    /// <summary>The record a change was opened over, and the refusal that stopped it going further.</summary>
    /// <param name="Record">The record as the row holds it, which every refusal reports the version of.</param>
    /// <param name="Refusal">The refusal, or <see langword="null" /> when the change may be composed.</param>
    private readonly record struct OpenedRecord(UserSettingsDocument Record, UserRecordWriteOutcome? Refusal);
}
