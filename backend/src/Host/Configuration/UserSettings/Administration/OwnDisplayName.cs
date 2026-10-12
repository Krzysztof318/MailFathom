// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>Hands the signed-in person the name this deployment records them under, and takes back the one they correct it to.</summary>
/// <remarks>
/// <para>
/// The name is the envelope rather than the document — the column an operator tells one user from another by, unique
/// across the deployment and keyed by nothing — which is why this stands beside
/// <see cref="UserRecordAdministration" /> instead of inside it. It does not share that service's grant either: what a
/// person is called belongs to their profile, beside the time zone and the portrait, and is written under the
/// profile's own grant.
/// </para>
/// <para>
/// <b>Neither act names a user.</b> The person is the one the credential authenticated, resolved from the principal
/// exactly as the record's own acts resolve it, so a request about somebody else is something a caller cannot express.
/// The read is a key lookup on that one user rather than a roster filtered down to them, which is what keeps a
/// user-facing surface from composing a deployment-wide catalog of people.
/// </para>
/// <para>
/// Reading is <see cref="MailFathomPermission.ProfileRead" /> and writing is
/// <see cref="MailFathomPermission.ProfileWrite" />, granted apart because a person who may not change their name must
/// still be shown it — and the read says whether a write would be accepted, so a client draws the name as text rather
/// than discovering the refusal by submitting one. Neither is the record's own write: a name says nothing about which
/// mailboxes this deployment connects to.
/// </para>
/// <para>
/// A settings policy may withhold the name from the person as it may any property of their record, although the name
/// is a column beside the record rather than a key of it. So what the read answers and what the write accepts are both
/// the grant and the policy together: a name the organization or the deployment holds is refused naming which, and a
/// request stating the name already held passes, because it changes nothing. An administrator's relabel is not bound,
/// since an editing restriction binds the person alone and a policy never forces a name.
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class OwnDisplayName(
    AccessAuthorization authorization,
    IUserDirectory directory,
    IUserProvisioning provisioning,
    IUserSettingsDocumentReader documents)
{
    /// <summary>Reads the name this deployment records the signed-in person under.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The name and whether this caller could change it, or <see langword="null" /> when this deployment holds no record for them.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ProfileRead" />.</exception>
    internal async Task<OwnDisplayNameReading?> ReadAsync(CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.ProfileRead);

        var user = authorization.RequireUser();

        return await directory.ReadUserAsync(user, cancellationToken) is { } held
            ? new OwnDisplayNameReading(
                held.DisplayName,
                authorization.Permits(MailFathomPermission.ProfileWrite)
                && await this.FindPolicyRefusalAsync(user, cancellationToken) is null)
            : null;
    }

    /// <summary>Records the signed-in person under the name they corrected theirs to.</summary>
    /// <param name="displayName">The name they would be recorded under.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What the change did: whether this deployment holds them at all, the name the row now carries, and the sentence naming what to correct where it holds them and refused.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.ProfileWrite" />.</exception>
    /// <remarks>
    /// The version the record's writes state has no counterpart here, for the reason <c>RelabelAsync</c> gives: the
    /// version guards the document a change is composed over and the name is not part of that document. The
    /// uniqueness is guarded by the statement itself rather than by a roster read before it, so a name taken between
    /// this read and that write is a refusal rather than the server's own unique-violation sentence.
    /// </remarks>
    internal async Task<OwnDisplayNameChange> ChangeAsync(string? displayName, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.ProfileWrite);

        var user = authorization.RequireUser();

        if (await directory.ReadUserAsync(user, cancellationToken) is not { } held)
        {
            return OwnDisplayNameChange.NoSuchUser;
        }

        if (FindNameRefusal(displayName) is { } unusable)
        {
            return OwnDisplayNameChange.Refused(unusable);
        }

        var name = displayName!.Trim();

        if (await this.FindPolicyRefusalAsync(user, cancellationToken) is { } withheld)
        {
            // Carried through as it stands, which changes nothing and so is nothing a policy holds against anybody.
            return name == held.DisplayName
                ? OwnDisplayNameChange.Recording(name) with { IsHeld = true }
                : OwnDisplayNameChange.Refused(withheld);
        }

        return await provisioning.RelabelAsync(user, name, cancellationToken)
            ? OwnDisplayNameChange.Recording(name)
            : OwnDisplayNameChange.Refused(NameTaken);
    }

    /// <summary>Says which settings policy does not leave the name to the person to change, or nothing where every policy does.</summary>
    /// <remarks>
    /// A policy this build does not read is answered as one that holds the name, for the reason a record is never
    /// served on part of what governs it: a restriction that lifts wherever a replica cannot read it is not one.
    /// </remarks>
    private async Task<string?> FindPolicyRefusalAsync(UserId user, CancellationToken cancellationToken)
    {
        if (await documents.ReadAsync(user, cancellationToken) is not { } record)
        {
            return null;
        }

        return SettingsPolicyLayers.Read(record.Policies, SettingsPolicySection.Users, out var unreadable) is { } layers
            ? GovernedRecordResolution.Resolve(SettingsPolicySection.Users, layers, record.Json)
                .Find(SettingsPolicySection.UserDisplayNameColumn)
                .FindRefusal(byThePerson: true)
            : SettingsPolicyLayers.DescribeUnreadable(unreadable);
    }

    /// <summary>Says why a stated name is not one this deployment would record, or nothing where it is.</summary>
    /// <remarks>
    /// The rules are the ones an administrator's own relabel applies, because both write the same column; the
    /// sentences are not, because this one is read by the person being named rather than by whoever administers them.
    /// </remarks>
    private static string? FindNameRefusal(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "Your name is what this deployment records you as, so a change of it states one. Write a name, unique across this deployment.";
        }

        var name = displayName.Trim();

        return name.Length > UserRecord.MaximumDisplayNameLength
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"The name is {name.Length} characters, past the {UserRecord.MaximumDisplayNameLength} this deployment stores. Shorten it.")
            : null;
    }

    /// <summary>The sentence a name another person of this deployment already carries is refused with.</summary>
    /// <remarks>It names no one: that the name is taken is what the person has to act on, and who took it is somebody else's record.</remarks>
    private const string NameTaken =
        "Somebody else on this deployment is already recorded under that name, and a name is unique across it. Choose another.";
}
