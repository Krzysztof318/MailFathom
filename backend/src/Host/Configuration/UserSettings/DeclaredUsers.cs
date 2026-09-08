// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.SensitiveContent;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Reads the users a deployment declares in configuration, and states every rule a start judges them by.</summary>
/// <remarks>
/// <para>
/// The collection is read here rather than bound through the options framework because it is an array at the root of
/// the configuration, and because what it decides is settled before a container exists: how many users this
/// deployment serves decides whether an unauthenticated user-facing surface may be served at all, and which user
/// each declared mailbox belongs to decides what every synchronization run writes. So it is judged where the rest of
/// those decisions are judged, which is also what puts it in front of a configuration write.
/// </para>
/// <para>
/// Nothing here reaches the database. What a declaration says is judged on its own; what the deployment already holds
/// is reconciled against it afterwards, by the startup gate that can read the rows.
/// </para>
/// </remarks>
internal static class DeclaredUsers
{
    /// <summary>The greatest number of users one deployment may declare.</summary>
    /// <remarks>
    /// It bounds a list an operator writes by hand into a file, so it is generous against any deployment that declares
    /// people and far below the point at which a start would spend meaningful time reconciling them. Meeting it means
    /// a file was generated rather than written, which is worth stopping for.
    /// </remarks>
    public const int MaximumDeclaredUsers = 256;

    /// <summary>Reads the declared users, refusing a property nothing binds rather than dropping it.</summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <returns>The declared users, in the order the collection declares them, empty when none are declared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the collection will not bind at all.</exception>
    public static IReadOnlyList<DeclaredUserOptions> ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(DeclaredUserOptions.SectionName)
            .Get<List<DeclaredUserOptions>>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? [];
    }

    /// <summary>Finds everything about the declared users that would stop a start.</summary>
    /// <param name="configuration">The configuration to judge.</param>
    /// <param name="today">The current date the declared synchronization bounds are read against.</param>
    /// <returns>One sentence per refusal, in the order an operator can act on them, empty when the declarations are usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the collection will not bind at all.</exception>
    public static IReadOnlyList<string> FindConfigurationErrors(IConfiguration configuration, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var users = ReadFrom(configuration);
        var deploymentAccounts = DeploymentMailAccountsIn(configuration);

        if (users.Count > MaximumDeclaredUsers)
        {
            return
            [
                $"{DeclaredUserOptions.SectionName} declares {users.Count} users, past the {MaximumDeclaredUsers} one deployment may serve. A list this long was generated rather than written: check what produced the file.",
            ];
        }

        // The envelope first, because every rule after it names a user and a declaration with no usable label has
        // nothing to be named by. The whole collection is judged rather than the first bad entry, so an operator
        // correcting a file learns about every entry at once.
        List<string> errors =
        [
            .. users.SelectMany(FindEnvelopeErrors),
            .. FindIdentifierCollisions(users),
            .. FindLabelCollisions(users),
        ];

        if (errors.Count > 0)
        {
            return errors;
        }

        // Read once for the whole collection, because every user's scanning block is judged against the same
        // deployment section and binding it per user would read the same keys once per declaration.
        var deploymentScanning = configuration
            .GetSection(SensitiveContentOptions.SectionName)
            .Get<SensitiveContentOptions>() ?? new SensitiveContentOptions();

        return
        [
            .. FindDeploymentSectionConflict(users, deploymentAccounts),
            .. users.Index().SelectMany(entry => FindMailAccountErrors(entry.Item, entry.Index, today)),
            .. users.Index().SelectMany(entry => FindSensitiveContentErrors(entry.Item, entry.Index, deploymentScanning)),
            .. FindCrossUserAccountNameCollisions(users),
        ];
    }

    /// <summary>Reports whether this deployment asked for its mailboxes to be refreshed at all.</summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <returns><see langword="true" /> when the synchronization switch is on.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Read beside the declarations because the two used to be judged together here: a deployment with the switch on
    /// and nothing to synchronize is a worker with no work. Which deployment that is stopped being decidable from
    /// configuration when a user's mailboxes became a record rather than a section, so the rule itself is held by
    /// the startup gate over the roster a start would serve, and this is the half a file still answers.
    /// </remarks>
    public static bool SynchronizationIsOn(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetValue(
            $"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Enabled)}",
            defaultValue: false);
    }

    /// <summary>Reads the identifier a declaration states, or nothing when it is not a UUID at all.</summary>
    /// <param name="declaredId">The identifier as the operator wrote it.</param>
    /// <returns>The identifier, or <see langword="null" /> when the value does not name a user.</returns>
    /// <remarks>
    /// The empty UUID is refused beside a malformed one. It is the value a template emits for a field nobody filled
    /// in, and it names nobody: a row provisioned under it would belong to no person and be unreachable by every read
    /// that resolves a user.
    /// </remarks>
    public static Guid? TryReadIdentifier(string? declaredId) =>
        Guid.TryParse(declaredId, out var identifier) && identifier != Guid.Empty ? identifier : null;

    /// <summary>Gets the mail accounts the deployment's own section declares, which belong to whichever sole user it holds.</summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <returns>The declarations, empty when the deployment's own section declares none.</returns>
    /// <remarks>
    /// Read here rather than off the roster, because a user served from this section carries no accounts of their own:
    /// the declarations stay in the reloadable mail snapshot so a reload can reach them, which leaves this the one place
    /// a rule about the whole deployment's mailboxes can see them.
    /// </remarks>
    public static List<MailSynchronizationAccountOptions> DeploymentMailAccountsIn(IConfiguration configuration) =>
        configuration.GetSection($"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Accounts)}")
            .Get<List<MailSynchronizationAccountOptions>>()
            ?? [];

    private static IEnumerable<string> FindEnvelopeErrors(DeclaredUserOptions user, int index)
    {
        var path = $"{DeclaredUserOptions.SectionName}:{index}";
        var label = string.IsNullOrWhiteSpace(user.DisplayName) ? null : user.DisplayName.Trim();

        if (label is null)
        {
            yield return $"{path}:{nameof(DeclaredUserOptions.DisplayName)} — a user is declared with the label an administrator tells them apart by. Write one, unique across this deployment.";
        }
        else if (label.Length > MailUserRecord.MaximumDisplayNameLength)
        {
            yield return $"{path}:{nameof(DeclaredUserOptions.DisplayName)} — the label is {label.Length} characters, past the {MailUserRecord.MaximumDisplayNameLength} a user's label is stored as. Shorten it.";
        }

        if (TryReadIdentifier(user.Id) is null)
        {
            yield return $"{path}:{nameof(DeclaredUserOptions.Id)} — a user is declared with the identifier every mail account and every stored message of theirs hangs on, written as a UUID. Generate one that nothing else in this deployment uses, and never change it afterwards.";
        }
    }

    private static IEnumerable<string> FindIdentifierCollisions(IReadOnlyList<DeclaredUserOptions> users)
    {
        var repeated = users
            .Select(user => TryReadIdentifier(user.Id))
            .OfType<Guid>()
            .GroupBy(identifier => identifier)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        return repeated.Length == 0
            ? []
            :
            [
                $"{DeclaredUserOptions.SectionName} declares more than one user under each of the identifiers {string.Join(", ", repeated)}. An identifier names one person, and everything either of them owns would be recorded against the same row.",
            ];
    }

    /// <summary>Reports every label carried by more than one declaration.</summary>
    /// <remarks>
    /// Compared exactly, which is how the unique index on the column compares it. What the uniqueness buys is a roster
    /// an administrator can read rather than a resolution rule, so nothing is normalized here beyond the surrounding
    /// white space a file routinely carries.
    /// </remarks>
    private static IEnumerable<string> FindLabelCollisions(IReadOnlyList<DeclaredUserOptions> users)
    {
        var repeated = users
            .Select(user => user.DisplayName?.Trim())
            .Where(label => !string.IsNullOrEmpty(label))
            .GroupBy(label => label, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        return repeated.Length == 0
            ? []
            :
            [
                $"{DeclaredUserOptions.SectionName} declares more than one user under each of the labels {string.Join(", ", repeated)}. A label is what an administrator selects a user by, so two users carrying one leaves them nothing to select on.",
            ];
    }

    /// <summary>Refuses a deployment that declares users and keeps mailboxes in the section that names none.</summary>
    /// <remarks>
    /// The deployment's own <c>MailSynchronization:Accounts</c> is the shape a single-user deployment keeps, and every
    /// account in it belongs to whichever user such a deployment holds. Once users are declared there is no such
    /// user to attribute them to, and picking one would hand somebody another person's mailbox.
    /// </remarks>
    private static IEnumerable<string> FindDeploymentSectionConflict(
        IReadOnlyList<DeclaredUserOptions> users,
        List<MailSynchronizationAccountOptions> deploymentAccounts) =>
        users.Count == 0 || deploymentAccounts.Count == 0
            ? []
            :
            [
                $"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Accounts)} declares {deploymentAccounts.Count} mail accounts while {DeclaredUserOptions.SectionName} declares {users.Count} users. That section names no user, so its accounts belong to whichever sole user a deployment holds and there is none here: move each of them under the user who owns it, as an entry of that user's {nameof(DeclaredUserOptions.MailAccounts)}.",
            ];

    private static IEnumerable<string> FindMailAccountErrors(DeclaredUserOptions user, int index, DateOnly today)
    {
        var path = $"{DeclaredUserOptions.SectionName}:{index}:{nameof(DeclaredUserOptions.MailAccounts)}";

        ValidationResult[] refusals =
        [
            .. UserMailAccountRules.FindRefusals(user.MailAccounts, path),
            .. UserMailAccountRules.FindSynchronizationWindowErrors(user.MailAccounts, today),
        ];

        return refusals.Select(refusal => $"{path} — {Describe(user)}: {refusal.ErrorMessage ?? "the declaration is invalid."}");
    }

    /// <summary>Finds what one declared user asks to have their mail scanned for that this deployment could not serve.</summary>
    /// <remarks>
    /// The rule is the one a user's own record is written under rather than a second copy of it, so an operator
    /// declaring a posture in the file and a user writing one into their record are refused for the same reasons in
    /// the same words — and a declaration that would be refused as a record is refused as a declaration.
    /// </remarks>
    private static IEnumerable<string> FindSensitiveContentErrors(
        DeclaredUserOptions user,
        int index,
        SensitiveContentOptions deployment) =>
        UserSensitiveContentRules.FindRefusals(
            user.SensitiveContent,
            deployment,
            $"{DeclaredUserOptions.SectionName}:{index}:{UserSensitiveContentOptions.BlockName}");

    /// <summary>Reports a mail-account name two users would both answer to.</summary>
    /// <remarks>
    /// <para>
    /// The pair <c>(user, identifier)</c> is what a mail account is keyed by, so two users each declaring
    /// <c>work</c> is a state persistence carries. What does not carry it yet is the settings read in front of it: the
    /// per-account ports a synchronization run and a mail read resolve are keyed by the identifier alone, so a name
    /// two users share would resolve to whichever declaration the lookup met. This is the bound that keeps that from
    /// happening, and it is deployment-wide rather than per user for exactly that reason.
    /// </para>
    /// <para>
    /// It is stated here rather than left to the per-user naming space above, which is the rule that governs what a
    /// caller may name and stays within its user. Removing this one is keying those ports by the pair, at which point
    /// the file needs no deployment-wide naming convention at all.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> FindCrossUserAccountNameCollisions(IReadOnlyList<DeclaredUserOptions> users)
    {
        var named = users
            .SelectMany(user => NamesDeclaredBy(user).Select(name => (User: Describe(user), Name: name)))
            .ToArray();

        var shared = named
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.DistinctBy(entry => entry.User, StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        return shared.Length == 0
            ? []
            :
            [
                $"More than one declared user names a mail account {string.Join(", ", shared)}. A mail account belongs to its user, but this release resolves an account's settings by its identifier alone, so a name two users share would reach whichever of the two the lookup met first. Give each of them a name no other user uses.",
            ];
    }

    /// <summary>Names a user in a refusal by the label they were declared under, which is what an operator reads their file by.</summary>
    private static string Describe(DeclaredUserOptions user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? "a user with no label" : user.DisplayName.Trim();

    private static IEnumerable<string> NamesDeclaredBy(DeclaredUserOptions user) =>
        (user.MailAccounts ?? [])
            .SelectMany(account => new[]
            {
                MailSynchronizationOptions.TryReadAccountId(account.AccountId),
                string.IsNullOrWhiteSpace(account.DisplayName) ? null : account.DisplayName.Trim(),
            })
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
