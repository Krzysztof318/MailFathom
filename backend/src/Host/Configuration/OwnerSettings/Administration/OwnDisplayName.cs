// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.OwnerSettings.Administration;

/// <summary>Hands the signed-in person the name this deployment records them under, and takes back the one they correct it to.</summary>
/// <remarks>
/// <para>
/// The name is the envelope rather than the document — the column an operator tells one owner from another by, unique
/// across the deployment and keyed by nothing — which is why this stands beside
/// <see cref="OwnerRecordAdministration" /> instead of inside it. What it shares with that service is the gate: the
/// envelope is written under the record's own grant and refused for the same owner, because both are what this
/// deployment holds about a person rather than what that person set about their client.
/// </para>
/// <para>
/// <b>Neither act names an owner.</b> The person is the one the credential authenticated, resolved from the principal
/// exactly as the record's own acts resolve it, so a request about somebody else is something a caller cannot express.
/// The read is a key lookup on that one owner rather than a roster filtered down to them, which is what keeps an
/// owner-facing surface from composing a deployment-wide catalog of people.
/// </para>
/// <para>
/// Reading is <see cref="MailFathomPermission.MailRead" />, the grant a signed-in person already holds, because a
/// person who may not change their name must still be shown it. Writing is
/// <see cref="MailFathomPermission.MailAccountsWrite" />, which is the record's write and is granted separately — and
/// the read says whether a write would be accepted, so a client draws the name as text rather than discovering the
/// refusal by submitting one.
/// </para>
/// <para>
/// A person whose mail accounts a configuration source still declares is refused, for the reason the record's own
/// write refuses them: a start relabels every declared owner from the declaration, so a name written here would stand
/// until the next restart and then silently revert. What comes back names the file to correct instead.
/// </para>
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The dependency injection container materializes this service.")]
internal sealed class OwnDisplayName(
    AccessAuthorization authorization,
    IMailOwnerDirectory directory,
    IMailOwnerProvisioning provisioning,
    ServedMailOwners servedOwners)
{
    /// <summary>Reads the name this deployment records the signed-in person under.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The name and whether this caller could change it, or <see langword="null" /> when this deployment holds no record for them.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no owner, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    internal async Task<OwnDisplayNameReading?> ReadAsync(CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailRead);

        var owner = authorization.RequireOwner();

        return await directory.ReadOwnerAsync(owner, cancellationToken) is { } held
            ? new OwnDisplayNameReading(held.DisplayName, this.WouldAcceptAWriteFor(owner))
            : null;
    }

    /// <summary>Records the signed-in person under the name they corrected theirs to.</summary>
    /// <param name="displayName">The name they would be recorded under.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What the change did: whether this deployment holds them at all, the name the row now carries, and the sentence naming what to correct where it holds them and refused.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no owner, or its grant omits <see cref="MailFathomPermission.MailAccountsWrite" />.</exception>
    /// <remarks>
    /// The version the record's writes state has no counterpart here, for the reason <c>RelabelAsync</c> gives: the
    /// version guards the document a change is composed over and the name is not part of that document. The
    /// uniqueness is guarded by the statement itself rather than by a roster read before it, so a name taken between
    /// this read and that write is a refusal rather than the server's own unique-violation sentence.
    /// </remarks>
    internal async Task<OwnDisplayNameChange> ChangeAsync(string? displayName, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailAccountsWrite);

        var owner = authorization.RequireOwner();

        if (await directory.ReadOwnerAsync(owner, cancellationToken) is null)
        {
            return OwnDisplayNameChange.NoSuchOwner;
        }

        // Ahead of the bound, exactly as the record's own gate is checked ahead of the version it was composed over:
        // a person a file still supplies is refused whatever name they wrote, and telling them to shorten one first
        // would send them back to a field that was never going to be accepted.
        if (servedOwners.SourceFor(owner) != MailOwnerAccountSource.OwnerDocument)
        {
            return OwnDisplayNameChange.Refused(DeclaredElsewhere);
        }

        if (FindNameRefusal(displayName) is { } unusable)
        {
            return OwnDisplayNameChange.Refused(unusable);
        }

        var name = displayName!.Trim();

        return await provisioning.RelabelAsync(owner, name, cancellationToken)
            ? OwnDisplayNameChange.Recording(name)
            : OwnDisplayNameChange.Refused(NameTaken);
    }

    /// <summary>Reports whether this caller could change the name of the owner they act for.</summary>
    /// <remarks>
    /// Both halves of the write's own gate, asked without attempting one: the grant the credential carries, and the
    /// source this deployment reads the person's mail accounts from. A caller holding one and not the other is
    /// answered the same as one holding neither, because what a client does about either is the same — draw the name
    /// and offer nothing to change it with.
    /// </remarks>
    private bool WouldAcceptAWriteFor(MailOwnerId owner) =>
        authorization.Permits(MailFathomPermission.MailAccountsWrite)
        && servedOwners.SourceFor(owner) == MailOwnerAccountSource.OwnerDocument;

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

        return name.Length > MailOwnerRecord.MaximumDisplayNameLength
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"The name is {name.Length} characters, past the {MailOwnerRecord.MaximumDisplayNameLength} this deployment stores. Shorten it.")
            : null;
    }

    /// <summary>The sentence a name another person of this deployment already carries is refused with.</summary>
    /// <remarks>It names no one: that the name is taken is what the person has to act on, and who took it is somebody else's record.</remarks>
    private const string NameTaken =
        "Somebody else on this deployment is already recorded under that name, and a name is unique across it. Choose another.";

    /// <summary>The sentence a person whose mail accounts a configuration source declares is refused with.</summary>
    /// <remarks>
    /// It names both shapes the declaration takes, for the reason the erasure's own refusal does: which one an
    /// operator wrote is in their file rather than in anything this deployment could report back to the person.
    /// </remarks>
    private const string DeclaredElsewhere =
        "A configuration source declares your mail accounts, so this deployment reads your name from it too and writes that name back at every start — a change made here would stand until the next restart and then revert. Ask whoever administers this deployment to change the name in your entry of the top-level Accounts collection, or to move your accounts into your own record with 'mfctl owner adopt'.";
}
