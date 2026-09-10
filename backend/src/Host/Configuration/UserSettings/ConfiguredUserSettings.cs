// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Finds what a configuration source supplies for one user.</summary>
/// <remarks>
/// <para>
/// One section is left that reaches a user at all: the deployment's own <c>MailSynchronization:Accounts</c>, which
/// names nobody and therefore belongs to whichever sole user such a deployment holds. Every other user is a record,
/// and no configuration source reaches them.
/// </para>
/// <para>
/// It reads the deployment's live configuration rather than the roster's copy, because what a write into a user's
/// record is judged against is what the files say now rather than what the last start reconciled.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reading.")]
internal sealed class ConfiguredUserSettings(IConfiguration configuration, ServedMailUsers servedUsers)
{
    /// <summary>Gets whether a configuration source names this user.</summary>
    /// <param name="user">The user asked about.</param>
    /// <returns><see langword="true" /> when they are the sole user the deployment's own mail section belongs to.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <remarks>
    /// What an act a start would undo asks — the relabel and the erasure — and what a write into a user's record is
    /// judged against, which are the same question now that one section is left.
    /// </remarks>
    public bool DeclaredByAConfigurationSource(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A configured declaration is looked up for a named user.", nameof(user));
        }

        return servedUsers.Users.Any(served =>
            served.User == user && served.Source == MailUserAccountSource.DeploymentSection);
    }

    /// <summary>Reads which users a configuration source names, for a caller asking about more than one of them.</summary>
    /// <returns>The sole user the deployment's own mail section belongs to, empty when every user is a record.</returns>
    public IReadOnlySet<MailUserId> UsersAConfigurationSourceDeclares() =>
        servedUsers.Users
            .Where(served => served.Source == MailUserAccountSource.DeploymentSection)
            .Select(served => served.User)
            .ToHashSet();

    /// <summary>Reads the mail accounts a configuration source declares for one user.</summary>
    /// <param name="user">The user asked about.</param>
    /// <returns>The declarations, empty when no configuration source reaches this user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <remarks>
    /// A user whose record is their own, and a user this process's roster does not hold at all, both answer with
    /// nothing — the first because their record decides their mailboxes, the second because they were provisioned
    /// after the roster was settled. Neither is a failure: both are users an ordinary write reaches.
    /// </remarks>
    public IReadOnlyList<MailSynchronizationAccountOptions> DeclaredFor(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A configured declaration is looked up for a named user.", nameof(user));
        }

        var served = servedUsers.Users.FirstOrDefault(candidate => candidate.User == user);

        return served?.Source == MailUserAccountSource.DeploymentSection
            ? MailSynchronizationOptions.AccountsDeclaredIn(configuration)
            : [];
    }
}
