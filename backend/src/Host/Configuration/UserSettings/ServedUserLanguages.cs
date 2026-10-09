// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Answers each person's language out of the served-user cache their own record is held in.</summary>
/// <remarks>
/// <para>
/// The cache is read rather than the record, because whatever composes text for somebody has already prepared its scope
/// with their accounts, which holds them here, and a held user is current until their record moves: a language changed
/// through an administrative surface or by the person themselves reaches the next derivation without a restart and
/// without any use case holding a copy of its own.
/// </para>
/// <para>
/// A user this replica does not hold, and a user it does not serve, are one answer rather than two — English, which is
/// what <see cref="IUserLanguages" /> states and why. Neither is a state a derivation reaches in an ordinary run:
/// nothing composes for somebody whose scope was not prepared for them, and a use case acting for somebody erased
/// mid-run is racing the erasure rather than reading a record that says nothing.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
internal sealed class ServedUserLanguages(ServedUsers servedUsers) : IUserLanguages
{
    /// <inheritdoc />
    public UserLanguage LanguageOf(UserId user) =>
        servedUsers.Peek(user)?.Language
            ?? UserLanguage.English;
}
