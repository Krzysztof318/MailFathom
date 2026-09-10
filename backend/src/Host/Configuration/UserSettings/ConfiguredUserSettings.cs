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
/// A user is served from one of three sources and only two of them are configuration. Which of the two it is decides
/// the section their declarations are written in, and the two sections are not interchangeable: the deployment's own
/// <c>MailSynchronization:Accounts</c> names no user and therefore belongs to whichever sole user such a deployment
/// holds, while a declared user's mailboxes are a numbered entry of the top-level collection of users.
/// </para>
/// <para>
/// It reads the deployment's live configuration rather than the roster's copy, because what a write into a user's
/// record is judged against is what the files say now rather than what the last start reconciled.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reading.")]
internal sealed class ConfiguredUserSettings(IConfiguration configuration, ServedMailUsers servedUsers)
{
    /// <summary>Gets whether a configuration source names this user, whatever their mail accounts are read from.</summary>
    /// <param name="user">The user asked about.</param>
    /// <returns><see langword="true" /> when a file declares them, or when they are the sole user of a deployment declaring none.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <remarks>
    /// A different question from <see cref="DeclaredFor" />, and the two answer apart for a user whose record is their
    /// own while a file goes on naming them: their mail accounts are their own, their label is still the declaration's,
    /// and their row is one a start writes again after it is removed. So this is what an act a start would undo asks —
    /// the relabel and the erasure — while the declarations are what a write into their record is judged against.
    /// </remarks>
    public bool DeclaredByAConfigurationSource(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A configured declaration is looked up for a named user.", nameof(user));
        }

        var declared = DeclaredUsers.ReadFrom(configuration);

        return declared.Count > 0
            ? declared.Any(declaration => DeclaredUsers.TryReadIdentifier(declaration.Id) == user.Value)
            : servedUsers.Users.Any(served =>
                served.User == user && served.Source == MailUserAccountSource.DeploymentSection);
    }

    /// <summary>Reads which users a configuration source names, for a caller asking about more than one of them.</summary>
    /// <returns>The users a file declares, or the sole user of a deployment declaring none, empty when no source names anybody.</returns>
    /// <remarks>
    /// The same question <see cref="DeclaredByAConfigurationSource" /> answers, asked once for a whole roster. Reading
    /// the declarations is a reflection bind of the collection and every mailbox in it, so asking per entry makes a
    /// listing of the deployment's users quadratic in a number an operator writes by hand and the roster route reads
    /// unconditionally.
    /// </remarks>
    public IReadOnlySet<MailUserId> UsersAConfigurationSourceDeclares()
    {
        var declared = DeclaredUsers.ReadFrom(configuration);

        return declared.Count > 0
            ? declared
                .Select(declaration => DeclaredUsers.TryReadIdentifier(declaration.Id))
                .OfType<Guid>()
                .Select(MailUserId.Create)
                .ToHashSet()
            : servedUsers.Users
                .Where(served => served.Source == MailUserAccountSource.DeploymentSection)
                .Select(served => served.User)
                .ToHashSet();
    }

    /// <summary>Reads the mail accounts a configuration source declares for one user.</summary>
    /// <param name="user">The user asked about.</param>
    /// <returns>The declarations, empty when no configuration source reaches this user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <remarks>
    /// A user whose record is their own, and a user this process's roster does not hold at all, both answer with
    /// nothing — the first because their record decides their mailboxes from now on, the second because they were
    /// provisioned after the roster was settled and no file has ever named them. Neither is a failure: both are users
    /// an ordinary write reaches.
    /// </remarks>
    public IReadOnlyList<MailSynchronizationAccountOptions> DeclaredFor(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A configured declaration is looked up for a named user.", nameof(user));
        }

        var served = servedUsers.Users.FirstOrDefault(candidate => candidate.User == user);

        IConfigurationSection? section = served?.Source switch
        {
            MailUserAccountSource.DeploymentSection => configuration.GetSection(
                $"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Accounts)}"),
            MailUserAccountSource.UserDeclaration => this.DeclaredSectionFor(user),
            _ => null,
        };

        return section?.Get<List<MailSynchronizationAccountOptions>>() ?? [];
    }

    /// <summary>Finds the mailbox section of the user-collection entry this user is declared in, by the key it was written under.</summary>
    /// <remarks>
    /// The key rather than the position the entry bound at, for the reason
    /// <see cref="Access.TransportAuthenticationOptions.ConfigurationKey" /> states about the other collection an
    /// operator numbers by hand: the binder appends one element per child and records no key, so a source numbering its
    /// entries with a gap makes the two different numbers and the position then addresses a section nobody wrote. What
    /// that would cost here is a user's declared mailboxes reading as somebody else's.
    /// <para>
    /// A declaration the file no longer carries answers with nothing, which is a file edited between the start that
    /// reconciled the roster and this read, and so does a collection whose children and bound elements no longer
    /// correspond — a shape only a source changing under the read produces, and one where no key can be trusted.
    /// </para>
    /// </remarks>
    private IConfigurationSection? DeclaredSectionFor(MailUserId user)
    {
        var declared = DeclaredUsers.ReadFrom(configuration);
        var entries = configuration.GetSection(DeclaredUserOptions.SectionName).GetChildren().ToArray();

        if (entries.Length != declared.Count)
        {
            return null;
        }

        var entry = entries
            .Zip(declared)
            .FirstOrDefault(candidate => DeclaredUsers.TryReadIdentifier(candidate.Second.Id) == user.Value);

        return entry.First?.GetSection(nameof(DeclaredUserOptions.MailAccounts));
    }
}
