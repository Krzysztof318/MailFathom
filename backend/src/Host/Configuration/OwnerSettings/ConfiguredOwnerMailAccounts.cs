// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Configuration;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.OwnerSettings;

/// <summary>Finds where a configuration source declares one owner's mail accounts, and what adopting them would write.</summary>
/// <remarks>
/// <para>
/// An owner is served from one of three sources and only two of them are configuration. Which of the two it is decides
/// the section their declarations are written in, and the two sections are not interchangeable: the deployment's own
/// <c>MailSynchronization:Accounts</c> names no owner and therefore belongs to whichever sole owner such a deployment
/// holds, while a declared owner's mailboxes are a numbered entry of the top-level collection of owners.
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
internal sealed class ConfiguredOwnerMailAccounts(IConfiguration configuration, ServedMailOwners servedOwners)
{
    /// <summary>The property an owner's record holds their mail accounts under, which every adopted key is rooted at.</summary>
    private const string MailAccountsProperty = nameof(OwnerAccountOptions.MailAccounts);

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

    /// <summary>States the changes that would materialize one owner's configured declarations into their record.</summary>
    /// <param name="owner">The owner asked about.</param>
    /// <returns>One change per configuration key the section supplies, empty when no configuration source reaches this owner.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// The section's keys are taken relative to it and re-rooted at the record's own collection, so
    /// <c>Accounts:1:MailAccounts:0:Host</c> and <c>MailSynchronization:Accounts:0:Host</c> both become
    /// <c>MailAccounts:0:Host</c> — which is the one property an owner's record holds mailboxes under, whichever of the
    /// two sections the operator had been writing in.
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
        ];
    }

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
