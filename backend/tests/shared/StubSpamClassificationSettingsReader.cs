// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.TestSupport;

/// <summary>Answers with one posture for every owner, for the paths that only read whether the feature is on.</summary>
/// <remarks>
/// The accounts are stated rather than derived because the deployed reader resolves them from the owner roster and the
/// mail section, neither of which a use-case test binds. Naming them is what lets a test reach the set-based half of the
/// gate: the scope covers exactly those accounts and, within each, exactly the folders the posture names.
/// </remarks>
internal sealed class StubSpamClassificationSettingsReader(
    SpamClassificationSettings settings,
    params MailAccountId[] accounts)
    : ISpamClassificationSettingsReader
{
    /// <summary>Gets a reader for the deployment that configured nothing, which classifies no mail.</summary>
    public static StubSpamClassificationSettingsReader Disabled { get; } = new(SpamClassificationSettings.Disabled);

    /// <inheritdoc />
    public SpamClassificationScope ScopeInForce => settings.IsEnabled
        ? SpamClassificationScope.Create(
            accounts,
            accounts.SelectMany(account => settings.ScannedFolderAliases
                .Select(alias => new MailFolderIdentity(account, alias))))
        : SpamClassificationScope.None;

    /// <inheritdoc />
    public SpamClassificationSettings SettingsFor(MailOwnerId owner) => settings;
}
