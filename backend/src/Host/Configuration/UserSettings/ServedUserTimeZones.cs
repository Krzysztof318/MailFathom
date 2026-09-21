// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Answers each person's zone out of the roster their own records were published into.</summary>
/// <remarks>
/// <para>
/// The roster is followed rather than read per call, exactly as <see cref="ServedUserLanguages" /> follows it: it is
/// published by the startup gate and republished by each user-record commit, so a zone somebody corrected reaches the
/// next anchor without a restart and without any use case holding a copy of its own.
/// </para>
/// <para>
/// A deployment before its gate has run, and a user it does not serve, are one answer rather than two —
/// <see cref="UserTimeZone.Coordinated" />, which is what <see cref="IUserTimeZones" /> states and why.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
internal sealed class ServedUserTimeZones(ServedUsers servedUsers) : IUserTimeZones
{
    /// <inheritdoc />
    public UserTimeZone ZoneOf(UserId user) => this.StatedZoneOf(user) ?? UserTimeZone.Coordinated;

    /// <inheritdoc />
    public UserTimeZone? StatedZoneOf(UserId user) =>
        servedUsers.TryGetUsers()?.FirstOrDefault(served => served.User == user)?.TimeZone;
}
