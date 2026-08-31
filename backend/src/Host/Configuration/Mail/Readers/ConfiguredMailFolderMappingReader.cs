// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads one account's folder mappings from the bound section.</summary>
/// <remarks>
/// The mappings are built once for the snapshot this reader was constructed over rather than per lookup, because the
/// two lookups below are asked once per rule evaluated against every email of a run, and each rebuild walks the
/// account's folders and constructs a domain value per entry.
/// </remarks>
internal sealed class ConfiguredMailFolderMappingReader : IMailFolderMappingReader
{
    private readonly MailSynchronizationOptions settings;

    private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<MailFolderMapping>>> mappingsByAccount;

    /// <summary>Initializes the reader over one snapshot of the mail section.</summary>
    /// <param name="settings">The snapshot the mappings are read from.</param>
    internal ConfiguredMailFolderMappingReader(MailSynchronizationOptions settings)
    {
        this.settings = settings;
        this.mappingsByAccount = new(this.ReadMappings, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public MailFolderMapping? FindFolderPlayingRole(MailAccountId accountId, MailFolderSpecialUse role) =>
        this.MappingsOf(accountId).FirstOrDefault(mapping => mapping.Plays(role));

    /// <inheritdoc />
    public MailFolderMapping? FindFolderNamed(MailAccountId accountId, MailFolderAlias folderAlias) =>
        this.MappingsOf(accountId).FirstOrDefault(mapping => mapping.Alias == folderAlias);

    /// <summary>Reads one account's folders as the mappings this port answers with.</summary>
    private IReadOnlyList<MailFolderMapping> MappingsOf(MailAccountId accountId) =>
        this.mappingsByAccount.Value.TryGetValue(accountId.Value, out var mappings) ? mappings : [];

    /// <summary>Builds every account's mappings once, keyed by the account identifier the lookups arrive with.</summary>
    /// <remarks>
    /// It walks <see cref="MailSynchronizationAccountOptions.EffectiveFolders" /> for the reason
    /// <see cref="ConfiguredMailFolders.Of(MailSynchronizationOptions)" /> does, so an account that configures no
    /// folder answers for the inbox mapping it is actually run with. An entry whose names are unusable is skipped rather than raised over: startup
    /// validation refuses that configuration, and a reload being rejected must not make a lookup throw. Two accounts
    /// configured under one identifier is the same refusal, so the first is kept here rather than the build failing
    /// over what validation already reports.
    /// </remarks>
    private Dictionary<string, IReadOnlyList<MailFolderMapping>> ReadMappings() =>
        (this.settings.Accounts ?? [])
            .Select(static account => (
                Id: MailSynchronizationOptions.TryReadAccountId(account.AccountId),
                account.EffectiveFolders))
            .Where(static account => account.Id is not null)
            .GroupBy(static account => account.Id!, StringComparer.Ordinal)
            .ToDictionary(
                static account => account.Key,
                static account => MappingsIn(account.First().EffectiveFolders),
                StringComparer.Ordinal);

    /// <summary>Reads one account's configured folders as the mappings the port answers with, dropping the unusable ones.</summary>
    private static IReadOnlyList<MailFolderMapping> MappingsIn(IReadOnlyList<MailFolderMappingOptions> folders) =>
    [
        .. folders.Select(static folder => TryCreateMapping(folder)).OfType<MailFolderMapping>(),
    ];

    /// <summary>Builds one configured folder's mapping, or nothing when its configured names are not values this system issues.</summary>
    private static MailFolderMapping? TryCreateMapping(MailFolderMappingOptions folder)
    {
        try
        {
            return folder.CreateMapping();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
