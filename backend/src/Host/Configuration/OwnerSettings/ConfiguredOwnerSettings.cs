// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Configuration;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Spam;

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>Finds what a configuration source supplies for one owner, and what adopting it would write into their record.</summary>
/// <remarks>
/// <para>
/// An owner is served from one of three sources and only two of them are configuration. Which of the two it is decides
/// the section their declarations are written in, and the two sections are not interchangeable: the deployment's own
/// <c>MailSynchronization:Accounts</c> names no owner and therefore belongs to whichever sole owner such a deployment
/// holds, while a declared owner's mailboxes are a numbered entry of the top-level collection of owners.
/// </para>
/// <para>
/// Their mailboxes are not the whole of what a file supplies them. Everything a configuration source still decides for
/// an owner has to move in the one act that ends the file's reach over them, so an adoption carries their classification
/// posture beside their accounts — and each further block the owner record grows joins the same act rather than being
/// left behind by it.
/// </para>
/// <para>
/// What an adoption writes is those same settings as configuration keys rather than as a serialized object, and that is
/// the whole reason this reads the section instead of the bound records beside it. A key survives a property the binder
/// does not know about, a value the file wrote in a shape the type would have normalized, and a setting a later release
/// adds — so what is persisted is what the operator wrote, which is what makes an adoption a move rather than a
/// rewrite.
/// </para>
/// <para>
/// It reads the deployment's live configuration rather than the roster's copy for the same reason: the roster is what
/// the start reconciled, and what an adoption moves is what the files say now.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this reading.")]
internal sealed class ConfiguredOwnerSettings(IConfiguration configuration, ServedMailOwners servedOwners)
{
    /// <summary>The property an owner's record holds their mail accounts under, which every adopted account key is rooted at.</summary>
    private const string MailAccountsProperty = nameof(OwnerAccountOptions.MailAccounts);

    /// <summary>The settings of the deployment's classification section that are an owner's own to hold.</summary>
    /// <remarks>
    /// Matched case-insensitively, because a configuration key keeps whatever casing the operator wrote it in and every
    /// reader that acts on one compares it that way. A section written as <c>"enabled"</c> in JSON or as
    /// <c>SPAMCLASSIFICATION__ENABLED</c> in the environment decides an owner's classification exactly as the spelling
    /// here does, so an ordinal comparison would drop their posture at the one act that cannot be undone.
    /// </remarks>
    private static readonly string[] OwnPostureSettings =
    [
        nameof(OwnerSpamClassificationOptions.Enabled),
        nameof(OwnerSpamClassificationOptions.UseScanner),
        nameof(OwnerSpamClassificationOptions.ScannedFolders),
        nameof(OwnerSpamClassificationOptions.ScannerThreshold),
        nameof(OwnerSpamClassificationOptions.Actions),
    ];

    /// <summary>Finds the configuration section one owner's mail accounts are declared in.</summary>
    /// <param name="owner">The owner asked about.</param>
    /// <returns>The section, or <see langword="null" /> when no configuration source reaches this owner.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// An owner who has adopted, and an owner this process's roster does not hold at all, both answer with nothing —
    /// the first because their record is their own from now on, the second because they were provisioned after the
    /// roster was settled and no file has ever named them. Neither is a failure: both are owners an ordinary write
    /// reaches.
    /// </remarks>
    public IConfigurationSection? SectionFor(MailOwnerId owner)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("A configured declaration is looked up for a named owner.", nameof(owner));
        }

        var served = servedOwners.Owners.FirstOrDefault(candidate => candidate.Owner == owner);

        return served?.Source switch
        {
            MailOwnerAccountSource.DeploymentSection => configuration.GetSection(
                $"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Accounts)}"),
            MailOwnerAccountSource.OwnerDeclaration => this.DeclaredSectionFor(owner),
            _ => null,
        };
    }

    /// <summary>Gets whether a configuration source names this owner, whatever their mail accounts are read from.</summary>
    /// <param name="owner">The owner asked about.</param>
    /// <returns><see langword="true" /> when a file declares them, or when they are the sole owner of a deployment declaring none.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// A different question from <see cref="SectionFor" />, and the two answer apart for an owner who has adopted while
    /// a file goes on naming them: their mail accounts are their own, their label is still the declaration's, and their
    /// row is one a start writes again after it is removed. So this is what an act a start would undo asks — the
    /// relabel and the erasure — while the section is what an adoption moves.
    /// </remarks>
    public bool DeclaredByAConfigurationSource(MailOwnerId owner)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("A configured declaration is looked up for a named owner.", nameof(owner));
        }

        var declared = DeclaredOwners.ReadFrom(configuration);

        return declared.Count > 0
            ? declared.Any(declaration => DeclaredOwners.TryReadIdentifier(declaration.Id) == owner.Value)
            : servedOwners.Owners.Any(served =>
                served.Owner == owner && served.Source == MailOwnerAccountSource.DeploymentSection);
    }

    /// <summary>Reads which owners a configuration source names, for a caller asking about more than one of them.</summary>
    /// <returns>The owners a file declares, or the sole owner of a deployment declaring none, empty when no source names anybody.</returns>
    /// <remarks>
    /// The same question <see cref="DeclaredByAConfigurationSource" /> answers, asked once for a whole roster. Reading
    /// the declarations is a reflection bind of the collection and every mailbox in it, so asking per entry makes a
    /// listing of the deployment's owners quadratic in a number an operator writes by hand and the roster route reads
    /// unconditionally.
    /// </remarks>
    public IReadOnlySet<MailOwnerId> OwnersAConfigurationSourceDeclares()
    {
        var declared = DeclaredOwners.ReadFrom(configuration);

        return declared.Count > 0
            ? declared
                .Select(declaration => DeclaredOwners.TryReadIdentifier(declaration.Id))
                .OfType<Guid>()
                .Select(MailOwnerId.Create)
                .ToHashSet()
            : servedOwners.Owners
                .Where(served => served.Source == MailOwnerAccountSource.DeploymentSection)
                .Select(served => served.Owner)
                .ToHashSet();
    }

    /// <summary>Reads the mail accounts a configuration source declares for one owner.</summary>
    /// <param name="owner">The owner asked about.</param>
    /// <returns>The declarations, empty when no configuration source reaches this owner.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>Bound rather than left as keys, because this is what a preview names the accounts by and an operator confirms an adoption against.</remarks>
    public IReadOnlyList<MailSynchronizationAccountOptions> DeclaredFor(MailOwnerId owner) =>
        this.SectionFor(owner)?.Get<List<MailSynchronizationAccountOptions>>() ?? [];

    /// <summary>States the changes that would materialize everything a configuration source decides for one owner into their record.</summary>
    /// <param name="owner">The owner asked about.</param>
    /// <returns>
    /// One change per configuration key the two sections supply — the owner's mail accounts and the classification
    /// posture <see cref="ClassificationAdoptionFor" /> reports — empty when no configuration source reaches this owner.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// The mail-account section's keys are taken relative to it and re-rooted at the record's own collection, so
    /// <c>Accounts:1:MailAccounts:0:Host</c> and <c>MailSynchronization:Accounts:0:Host</c> both become
    /// <c>MailAccounts:0:Host</c> — which is the one property an owner's record holds mailboxes under, whichever of the
    /// two sections the operator had been writing in. The posture is re-rooted the same way, at the record's own
    /// classification block, and moves for the reason stated on the method that reads it.
    /// </remarks>
    public IReadOnlyList<ConfigurationEdit> AdoptionEditsFor(MailOwnerId owner)
    {
        if (this.SectionFor(owner) is not { } section)
        {
            return [];
        }

        // A section enumerates itself under the empty key, and a key whose value is null is a section rather than a
        // setting: neither states a value, and an edit composed from either would address nothing.
        return
        [
            .. section.AsEnumerable(makePathsRelative: true)
                .Where(setting => !string.IsNullOrEmpty(setting.Key) && setting.Value is not null)
                .OrderBy(setting => setting.Key, StringComparer.Ordinal)
                .Select(setting => ConfigurationEdit.SetTo($"{MailAccountsProperty}:{setting.Key}", setting.Value!)),
            .. this.ClassificationAdoptionEdits(),
        ];
    }

    /// <summary>States the classification posture an adoption would commit into one owner's record.</summary>
    /// <param name="owner">The owner asked about.</param>
    /// <returns>One change per posture key the deployment's section supplies, empty when no configuration source reaches this owner.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// Published beside the accounts rather than left inside the adoption, because two of these settings act on the
    /// owner's own mail server and an adoption cannot be undone: what an operator confirms has to name them.
    /// </remarks>
    public IReadOnlyList<ConfigurationEdit> ClassificationAdoptionFor(MailOwnerId owner) =>
        this.SectionFor(owner) is null ? [] : [.. this.ClassificationAdoptionEdits()];

    /// <summary>States the changes that would carry the deployment's classification posture into an owner's record.</summary>
    /// <remarks>
    /// <para>
    /// The posture moves with the mailboxes because an adoption is a move rather than a rewrite: an owner served from a
    /// configuration source has their mail classified on the deployment's section's terms, and a handover that left the
    /// section behind would switch their classification off on the strength of an administrative act about where their
    /// settings live. From the commit onwards the record is what decides it, and the section reaches them no longer.
    /// </para>
    /// <para>
    /// Only the settings an owner's own block declares are carried. The section also states where the scanner daemon is,
    /// what one scan may spend, how long a verdict may hold the index back, and how wide a run's batches are, and none
    /// of those is an owner's to hold — a record carrying one would be refused by the strict binding, which is the same
    /// answer this filter reaches before the candidate is composed.
    /// </para>
    /// </remarks>
    private IEnumerable<ConfigurationEdit> ClassificationAdoptionEdits() =>
        configuration.GetSection(SpamClassificationOptions.SectionName)
            .AsEnumerable(makePathsRelative: true)
            .Where(setting => !string.IsNullOrEmpty(setting.Key) && setting.Value is not null)
            .Where(setting => OwnPostureSettings.Contains(
                setting.Key.Split(':')[0],
                StringComparer.OrdinalIgnoreCase))
            .OrderBy(setting => setting.Key, StringComparer.Ordinal)
            .Select(setting => ConfigurationEdit.SetTo(
                $"{OwnerSpamClassificationOptions.RecordProperty}:{setting.Key}",
                setting.Value!));

    /// <summary>Finds the entry of the owner collection this owner is declared in, by the key it was written under.</summary>
    /// <remarks>
    /// The key rather than the position the entry bound at, for the reason
    /// <see cref="Access.TransportAuthenticationOptions.ConfigurationKey" /> states about the other collection an
    /// operator numbers by hand: the binder appends one element per child and records no key, so a source numbering its
    /// entries with a gap makes the two different numbers and the position then addresses a section nobody wrote. What
    /// that would cost here is an adoption committing an empty record over an owner whose mailboxes the file declares.
    /// <para>
    /// A declaration the file no longer carries answers with nothing, which is a file edited between the start that
    /// reconciled the roster and this read, and so does a collection whose children and bound elements no longer
    /// correspond — a shape only a source changing under the read produces, and one where no key can be trusted.
    /// </para>
    /// </remarks>
    private IConfigurationSection? DeclaredSectionFor(MailOwnerId owner)
    {
        var declared = DeclaredOwners.ReadFrom(configuration);
        var entries = configuration.GetSection(DeclaredOwnerOptions.SectionName).GetChildren().ToArray();

        if (entries.Length != declared.Count)
        {
            return null;
        }

        var entry = entries
            .Zip(declared)
            .FirstOrDefault(candidate => DeclaredOwners.TryReadIdentifier(candidate.Second.Id) == owner.Value);

        return entry.First?.GetSection(nameof(DeclaredOwnerOptions.MailAccounts));
    }
}
