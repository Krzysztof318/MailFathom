// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Observability.ClientTelemetry;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>One user this deployment serves, composed from the record that is the whole of what it knows about them.</summary>
/// <param name="User">The identity every mail account and every stored message of theirs hangs on.</param>
/// <param name="DisplayName">The label an operator tells this user apart by.</param>
/// <param name="MailAccounts">The mail accounts assigned to this user, each carrying its own settings.</param>
/// <param name="Language">The language this deployment writes for them in; a record held from before the property existed states none and reads as English.</param>
/// <remarks>
/// One source reaches a user, and it is their own record, so everything here is bound out of that document rather than
/// out of a section. What is about a mailbox rather than about the person is not here at all: how its mail is
/// classified and what it is scanned for are the account's, so they are read off the declaration rather than off this
/// record and a mailbox two people share is judged once.
/// </remarks>
internal sealed record ServedMailUser(
    MailUserId User,
    string DisplayName,
    IReadOnlyList<MailSynchronizationAccountOptions> MailAccounts,
    MailUserLanguage Language = MailUserLanguage.English)
{
    /// <summary>Gets the zone this person's own record states, or <see langword="null" /> where it states none.</summary>
    /// <remarks>
    /// A property rather than a positional value, for the reason
    /// <see cref="Application.Access.MailUserRecord.EndpointAccess" /> is one: every composition that has nothing to
    /// say about a zone says nothing rather than repeating an answer. It stays nothing rather than falling to
    /// <see cref="MailUserTimeZone.Coordinated" /> here, because a person who chose UTC and a person nobody has asked
    /// yet are answered differently — the first is left alone and the second is offered the zone their client reports —
    /// and the roster is the last place holding both facts.
    /// </remarks>
    public MailUserTimeZone? TimeZone { get; init; }

    /// <summary>Gets the level this person's own record asks their client to record at, or <see langword="null" /> where it states none.</summary>
    /// <remarks>
    /// Nothing rather than the deployment's level, for the reason <see cref="TimeZone" /> stays nothing: the roster
    /// carries what the record stated, and the session route is the one place the two are resolved into the single
    /// answer a client is served. Reading it here as the deployment's would put that resolution in the roster, where
    /// nothing could tell a person who asked for the deployment's level from a person nobody has raised.
    /// </remarks>
    public ClientTelemetryLevel? ClientTelemetryLevel { get; init; }
}
