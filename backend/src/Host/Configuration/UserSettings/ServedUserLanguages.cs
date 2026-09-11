// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Answers each user's language out of the roster their own records were published into.</summary>
/// <remarks>
/// <para>
/// The roster is followed rather than read per call: it is published by the startup gate and republished by each
/// user-document commit, so a language changed through the administrative surface reaches the next derivation without
/// a restart and without any pass holding a copy of its own.
/// </para>
/// <para>
/// A deployment before its gate has run, and a user it does not serve, are one answer rather than two — English, which
/// is what <see cref="IMailUserLanguages" /> states and why. Neither is a state a derivation can reach in an ordinary
/// run: nothing derives before the roster exists, and a pass acting for somebody erased mid-run is racing the erasure
/// rather than reading a record that says nothing.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reader.")]
internal sealed class ServedUserLanguages(ServedMailUsers servedUsers) : IMailUserLanguages
{
    /// <inheritdoc />
    public MailUserLanguage ForUser(MailUserId user) =>
        servedUsers.TryGetUsers()?.FirstOrDefault(served => served.User == user)?.Language
            ?? MailUserLanguage.English;
}
