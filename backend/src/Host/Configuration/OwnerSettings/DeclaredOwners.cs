// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.Access;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>Reads the owners a deployment declares in configuration, and states every rule a start judges them by.</summary>
/// <remarks>
/// <para>
/// The collection is read here rather than bound through the options framework because it is an array at the root of
/// the configuration, and because what it decides is settled before a container exists: how many owners this
/// deployment serves decides whether an unauthenticated owner-facing surface may be served at all, and which owner
/// each declared mailbox belongs to decides what every synchronization run writes. So it is judged where the rest of
/// those decisions are judged, which is also what puts it in front of a configuration write.
/// </para>
/// <para>
/// Nothing here reaches the database. What a declaration says is judged on its own; what the deployment already holds
/// is reconciled against it afterwards, by the startup gate that can read the rows.
/// </para>
/// </remarks>
internal static class DeclaredOwners
{
    /// <summary>The greatest number of owners one deployment may declare.</summary>
    /// <remarks>
    /// It bounds a list an operator writes by hand into a file, so it is generous against any deployment that declares
    /// people and far below the point at which a start would spend meaningful time reconciling them. Meeting it means
    /// a file was generated rather than written, which is worth stopping for.
    /// </remarks>
    public const int MaximumDeclaredOwners = 256;

    /// <summary>Reads the declared owners, refusing a property nothing binds rather than dropping it.</summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <returns>The declared owners, in the order the collection declares them, empty when none are declared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the collection will not bind at all.</exception>
    public static IReadOnlyList<DeclaredOwnerOptions> ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(DeclaredOwnerOptions.SectionName)
            .Get<List<DeclaredOwnerOptions>>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? [];
    }

    /// <summary>Finds everything about the declared owners that would stop a start.</summary>
    /// <param name="configuration">The configuration to judge.</param>
    /// <param name="today">The current date the declared synchronization bounds are read against.</param>
    /// <returns>One sentence per refusal, in the order an operator can act on them, empty when the declarations are usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the collection will not bind at all.</exception>
    public static IReadOnlyList<string> FindConfigurationErrors(IConfiguration configuration, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var owners = ReadFrom(configuration);
        var deploymentAccounts = DeploymentMailAccountsIn(configuration);

        if (owners.Count > MaximumDeclaredOwners)
        {
            return
            [
                $"{DeclaredOwnerOptions.SectionName} declares {owners.Count} owners, past the {MaximumDeclaredOwners} one deployment may serve. A list this long was generated rather than written: check what produced the file.",
            ];
        }

        // The envelope first, because every rule after it names an owner and a declaration with no usable label has
        // nothing to be named by. The whole collection is judged rather than the first bad entry, so an operator
        // correcting a file learns about every entry at once.
        List<string> errors =
        [
            .. owners.SelectMany(FindEnvelopeErrors),
            .. FindIdentifierCollisions(owners),
            .. FindLabelCollisions(owners),
        ];

        if (errors.Count > 0)
        {
            return errors;
        }

        return
        [
            .. FindDeploymentSectionConflict(owners, deploymentAccounts),
            .. owners.Index().SelectMany(entry => FindMailAccountErrors(entry.Item, entry.Index, today)),
            .. FindCrossOwnerAccountNameCollisions(owners),
        ];
    }

    /// <summary>Reports whether this deployment asked for its mailboxes to be refreshed at all.</summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <returns><see langword="true" /> when the synchronization switch is on.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Read beside the declarations because the two used to be judged together here: a deployment with the switch on
    /// and nothing to synchronize is a worker with no work. Which deployment that is stopped being decidable from
    /// configuration when an owner's mailboxes became a record rather than a section, so the rule itself is held by
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
    /// <returns>The identifier, or <see langword="null" /> when the value does not name an owner.</returns>
    /// <remarks>
    /// The empty UUID is refused beside a malformed one. It is the value a template emits for a field nobody filled
    /// in, and it names nobody: a row provisioned under it would belong to no person and be unreachable by every read
    /// that resolves an owner.
    /// </remarks>
    public static Guid? TryReadIdentifier(string? declaredId) =>
        Guid.TryParse(declaredId, out var identifier) && identifier != Guid.Empty ? identifier : null;

    /// <summary>Gets the mail accounts the deployment's own section declares, which belong to whichever sole owner it holds.</summary>
    /// <param name="configuration">The configuration to read.</param>
    /// <returns>The declarations, empty when the deployment's own section declares none.</returns>
    /// <remarks>
    /// Read here rather than off the roster, because an owner served from this section carries no accounts of their own:
    /// the declarations stay in the reloadable mail snapshot so a reload can reach them, which leaves this the one place
    /// a rule about the whole deployment's mailboxes can see them.
    /// </remarks>
    public static List<MailSynchronizationAccountOptions> DeploymentMailAccountsIn(IConfiguration configuration) =>
        configuration.GetSection($"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Accounts)}")
            .Get<List<MailSynchronizationAccountOptions>>()
            ?? [];

    private static IEnumerable<string> FindEnvelopeErrors(DeclaredOwnerOptions owner, int index)
    {
        var path = $"{DeclaredOwnerOptions.SectionName}:{index}";
        var label = string.IsNullOrWhiteSpace(owner.DisplayName) ? null : owner.DisplayName.Trim();

        if (label is null)
        {
            yield return $"{path}:{nameof(DeclaredOwnerOptions.DisplayName)} — an owner is declared with the label an administrator tells them apart by. Write one, unique across this deployment.";
        }
        else if (label.Length > MailOwnerRecord.MaximumDisplayNameLength)
        {
            yield return $"{path}:{nameof(DeclaredOwnerOptions.DisplayName)} — the label is {label.Length} characters, past the {MailOwnerRecord.MaximumDisplayNameLength} an owner's label is stored as. Shorten it.";
        }

        if (TryReadIdentifier(owner.Id) is null)
        {
            yield return $"{path}:{nameof(DeclaredOwnerOptions.Id)} — an owner is declared with the identifier every mail account and every stored message of theirs hangs on, written as a UUID. Generate one that nothing else in this deployment uses, and never change it afterwards.";
        }
    }

    private static IEnumerable<string> FindIdentifierCollisions(IReadOnlyList<DeclaredOwnerOptions> owners)
    {
        var repeated = owners
            .Select(owner => TryReadIdentifier(owner.Id))
            .OfType<Guid>()
            .GroupBy(identifier => identifier)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        return repeated.Length == 0
            ? []
            :
            [
                $"{DeclaredOwnerOptions.SectionName} declares more than one owner under each of the identifiers {string.Join(", ", repeated)}. An identifier names one person, and everything either of them owns would be recorded against the same row.",
            ];
    }

    /// <summary>Reports every label carried by more than one declaration.</summary>
    /// <remarks>
    /// Compared exactly, which is how the unique index on the column compares it. What the uniqueness buys is a roster
    /// an administrator can read rather than a resolution rule, so nothing is normalized here beyond the surrounding
    /// white space a file routinely carries.
    /// </remarks>
    private static IEnumerable<string> FindLabelCollisions(IReadOnlyList<DeclaredOwnerOptions> owners)
    {
        var repeated = owners
            .Select(owner => owner.DisplayName?.Trim())
            .Where(label => !string.IsNullOrEmpty(label))
            .GroupBy(label => label, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        return repeated.Length == 0
            ? []
            :
            [
                $"{DeclaredOwnerOptions.SectionName} declares more than one owner under each of the labels {string.Join(", ", repeated)}. A label is what an administrator selects an owner by, so two owners carrying one leaves them nothing to select on.",
            ];
    }

    /// <summary>Refuses a deployment that declares owners and keeps mailboxes in the section that names none.</summary>
    /// <remarks>
    /// The deployment's own <c>MailSynchronization:Accounts</c> is the shape a single-owner deployment keeps, and every
    /// account in it belongs to whichever owner such a deployment holds. Once owners are declared there is no such
    /// owner to attribute them to, and picking one would hand somebody another person's mailbox.
    /// </remarks>
    private static IEnumerable<string> FindDeploymentSectionConflict(
        IReadOnlyList<DeclaredOwnerOptions> owners,
        List<MailSynchronizationAccountOptions> deploymentAccounts) =>
        owners.Count == 0 || deploymentAccounts.Count == 0
            ? []
            :
            [
                $"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Accounts)} declares {deploymentAccounts.Count} mail accounts while {DeclaredOwnerOptions.SectionName} declares {owners.Count} owners. That section names no owner, so its accounts belong to whichever sole owner a deployment holds and there is none here: move each of them under the owner who owns it, as an entry of that owner's {nameof(DeclaredOwnerOptions.MailAccounts)}.",
            ];

    private static IEnumerable<string> FindMailAccountErrors(DeclaredOwnerOptions owner, int index, DateOnly today)
    {
        var path = $"{DeclaredOwnerOptions.SectionName}:{index}:{nameof(DeclaredOwnerOptions.MailAccounts)}";

        ValidationResult[] refusals =
        [
            .. OwnerMailAccountRules.FindRefusals(owner.MailAccounts, path),
            .. OwnerMailAccountRules.FindSynchronizationWindowErrors(owner.MailAccounts, today),
        ];

        return refusals.Select(refusal => $"{path} — {Describe(owner)}: {refusal.ErrorMessage ?? "the declaration is invalid."}");
    }

    /// <summary>Reports a mail-account name two owners would both answer to.</summary>
    /// <remarks>
    /// <para>
    /// The pair <c>(owner, identifier)</c> is what a mail account is keyed by, so two owners each declaring
    /// <c>work</c> is a state persistence carries. What does not carry it yet is the settings read in front of it: the
    /// per-account ports a synchronization run and a mail read resolve are keyed by the identifier alone, so a name
    /// two owners share would resolve to whichever declaration the lookup met. This is the bound that keeps that from
    /// happening, and it is deployment-wide rather than per owner for exactly that reason.
    /// </para>
    /// <para>
    /// It is stated here rather than left to the per-owner naming space above, which is the rule that governs what a
    /// caller may name and stays within its owner. Removing this one is keying those ports by the pair, at which point
    /// the file needs no deployment-wide naming convention at all.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> FindCrossOwnerAccountNameCollisions(IReadOnlyList<DeclaredOwnerOptions> owners)
    {
        var named = owners
            .SelectMany(owner => NamesDeclaredBy(owner).Select(name => (Owner: Describe(owner), Name: name)))
            .ToArray();

        var shared = named
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.DistinctBy(entry => entry.Owner, StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        return shared.Length == 0
            ? []
            :
            [
                $"More than one declared owner names a mail account {string.Join(", ", shared)}. A mail account belongs to its owner, but this release resolves an account's settings by its identifier alone, so a name two owners share would reach whichever of the two the lookup met first. Give each of them a name no other owner uses.",
            ];
    }

    /// <summary>Names an owner in a refusal by the label they were declared under, which is what an operator reads their file by.</summary>
    private static string Describe(DeclaredOwnerOptions owner) =>
        string.IsNullOrWhiteSpace(owner.DisplayName) ? "an owner with no label" : owner.DisplayName.Trim();

    private static IEnumerable<string> NamesDeclaredBy(DeclaredOwnerOptions owner) =>
        (owner.MailAccounts ?? [])
            .SelectMany(account => new[]
            {
                MailSynchronizationOptions.TryReadAccountId(account.AccountId),
                string.IsNullOrWhiteSpace(account.DisplayName) ? null : account.DisplayName.Trim(),
            })
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
