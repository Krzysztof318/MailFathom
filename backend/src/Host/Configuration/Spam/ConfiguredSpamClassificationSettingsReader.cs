// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Reads the classification settings out of the bound section, resolving the scope an operator left unset.</summary>
/// <remarks>
/// <para>
/// The default scope lives here rather than in the section because it is not a constant: it is whichever alias each
/// account maps to its inbox, which is in the mail section. An operator whose server presents the inbox under another
/// name configures the role, and the default has to follow the role rather than the literal text.
/// </para>
/// <para>
/// Both sections are read per request rather than captured, so a reload takes effect on the next classification without
/// a restart — and reading them changes nothing about what is already recorded.
/// </para>
/// </remarks>
internal sealed class ConfiguredSpamClassificationSettingsReader(
    IOptionsMonitor<SpamClassificationOptions> classificationOptions,
    MailSynchronizationOptions synchronizationOptions)
    : ISpamClassificationSettingsReader
{
    /// <inheritdoc />
    public SpamClassificationSettings Settings
    {
        get
        {
            var options = classificationOptions.CurrentValue;

            return SpamClassificationSettings.Create(
                options.Enabled,
                options.UseScanner,
                this.ScannedFolderAliases(options),
                options.ScannerThreshold,
                options.ClassificationWait);
        }
    }

    /// <summary>Reads the aliases the scope names, or every account's inbox alias when the operator named none.</summary>
    /// <remarks>
    /// An explicitly empty list is honoured as an empty scope, which is the distinction the nullable property exists to
    /// preserve: an operator who wrote no folders asked for the default, and one who wrote none asked for none.
    /// </remarks>
    private IEnumerable<MailFolderAlias> ScannedFolderAliases(SpamClassificationOptions options) =>
        options.ScannedFolders is { } configured
            ? configured
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Select(MailFolderAlias.Create)
            : ConfiguredMailFolders.InboxAliasesOf(synchronizationOptions);
}
