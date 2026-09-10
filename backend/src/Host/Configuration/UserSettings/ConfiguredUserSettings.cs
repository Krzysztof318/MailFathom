// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Finds what a configuration source supplies for one user, which is nothing.</summary>
/// <remarks>
/// <para>
/// No section reaches a user any longer: the collection that declared them and the deployment's own mail section are
/// both withdrawn, so every user is a record and every mailbox is in it. The three questions below therefore have one
/// answer each, and they are still asked because three refusals are written against them — a record write, an erasure,
/// and a person correcting their own name — and each of those goes with the marker that says a document has never been
/// written, in <see href="https://github.com/Krzysztof318/MailFathom/issues/1829">issue 1829</see>.
/// </para>
/// <para>
/// One reading rather than a constant at each of the three, so that retiring them is one deletion and none of them can
/// be left behind answering differently in the meantime.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reading.")]
internal sealed class ConfiguredUserSettings
{
    /// <summary>Gets whether a configuration source names this user.</summary>
    /// <param name="user">The user asked about.</param>
    /// <returns><see langword="false" />, no configuration source naming anybody.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    public bool DeclaredByAConfigurationSource(MailUserId user)
    {
        RequireNamed(user);

        return false;
    }

    /// <summary>Reads which users a configuration source names, for a caller asking about more than one of them.</summary>
    /// <returns>Nobody.</returns>
    public IReadOnlySet<MailUserId> UsersAConfigurationSourceDeclares() => new HashSet<MailUserId>();

    /// <summary>Reads the mail accounts a configuration source declares for one user.</summary>
    /// <param name="user">The user asked about.</param>
    /// <returns>Nothing, every mailbox being its user's own record.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    public IReadOnlyList<MailSynchronizationAccountOptions> DeclaredFor(MailUserId user)
    {
        RequireNamed(user);

        return [];
    }

    private static void RequireNamed(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A configured declaration is looked up for a named user.", nameof(user));
        }
    }
}
