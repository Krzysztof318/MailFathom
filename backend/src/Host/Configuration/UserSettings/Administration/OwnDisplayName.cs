// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>Hands the signed-in person the name this deployment records them under, and takes back the one they correct it to.</summary>
/// <remarks>
/// <para>
/// The name is the envelope rather than the document — the column an operator tells one user from another by, unique
/// across the deployment and keyed by nothing — which is why this stands beside
/// <see cref="UserRecordAdministration" /> instead of inside it. What it shares with that service is the grant: the
/// envelope is written under the record's own, because both are what this deployment holds about a person rather than
/// what that person set about their client.
/// </para>
/// <para>
/// <b>Neither act names a user.</b> The person is the one the credential authenticated, resolved from the principal
/// exactly as the record's own acts resolve it, so a request about somebody else is something a caller cannot express.
/// The read is a key lookup on that one user rather than a roster filtered down to them, which is what keeps a
/// user-facing surface from composing a deployment-wide catalog of people.
/// </para>
/// <para>
/// Reading is <see cref="MailFathomPermission.MailRead" />, the grant a signed-in person already holds, because a
/// person who may not change their name must still be shown it. Writing is
/// <see cref="MailFathomPermission.MailAccountsWrite" />, which is the record's write and is granted separately — and
/// the read says whether a write would be accepted, so a client draws the name as text rather than discovering the
/// refusal by submitting one.
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class OwnDisplayName(
    AccessAuthorization authorization,
    IMailUserDirectory directory,
    IMailUserProvisioning provisioning)
{
    /// <summary>Reads the name this deployment records the signed-in person under.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The name and whether this caller could change it, or <see langword="null" /> when this deployment holds no record for them.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    internal async Task<OwnDisplayNameReading?> ReadAsync(CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailRead);

        var user = authorization.RequireUser();

        return await directory.ReadUserAsync(user, cancellationToken) is { } held
            ? new OwnDisplayNameReading(
                held.DisplayName,
                authorization.Permits(MailFathomPermission.MailAccountsWrite))
            : null;
    }

    /// <summary>Records the signed-in person under the name they corrected theirs to.</summary>
    /// <param name="displayName">The name they would be recorded under.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What the change did: whether this deployment holds them at all, the name the row now carries, and the sentence naming what to correct where it holds them and refused.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailAccountsWrite" />.</exception>
    /// <remarks>
    /// The version the record's writes state has no counterpart here, for the reason <c>RelabelAsync</c> gives: the
    /// version guards the document a change is composed over and the name is not part of that document. The
    /// uniqueness is guarded by the statement itself rather than by a roster read before it, so a name taken between
    /// this read and that write is a refusal rather than the server's own unique-violation sentence.
    /// </remarks>
    internal async Task<OwnDisplayNameChange> ChangeAsync(string? displayName, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailAccountsWrite);

        var user = authorization.RequireUser();

        if (await directory.ReadUserAsync(user, cancellationToken) is null)
        {
            return OwnDisplayNameChange.NoSuchUser;
        }

        if (FindNameRefusal(displayName) is { } unusable)
        {
            return OwnDisplayNameChange.Refused(unusable);
        }

        var name = displayName!.Trim();

        return await provisioning.RelabelAsync(user, name, cancellationToken)
            ? OwnDisplayNameChange.Recording(name)
            : OwnDisplayNameChange.Refused(NameTaken);
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

        return name.Length > MailUserRecord.MaximumDisplayNameLength
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"The name is {name.Length} characters, past the {MailUserRecord.MaximumDisplayNameLength} this deployment stores. Shorten it.")
            : null;
    }

    /// <summary>The sentence a name another person of this deployment already carries is refused with.</summary>
    /// <remarks>It names no one: that the name is taken is what the person has to act on, and who took it is somebody else's record.</remarks>
    private const string NameTaken =
        "Somebody else on this deployment is already recorded under that name, and a name is unique across it. Choose another.";
}
