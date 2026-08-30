// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Answers with what a test stated about classifying mail, for every owner this suite serves.</summary>
/// <remarks>
/// The deployed reader resolves its answer from the owner roster, the bound configuration section, and the account's
/// inbox mapping, none of which the suite binds. Stating the posture directly keeps this suite's tests about what
/// classification does with it rather than about how a section is read, which the host's own unit tests already
/// establish.
/// </remarks>
internal sealed class FixedSpamClassificationSettingsReader(
    SpamClassificationSettings settings,
    params MailAccountId[] accounts)
    : ISpamClassificationSettingsReader
{
    /// <inheritdoc />
    public SpamClassificationScope ScopeInForce => settings.IsEnabled
        ? SpamClassificationScope.Create(
            accounts,
            accounts.SelectMany(account => settings.ScannedFolderAliases
                .Select(alias => new MailFolderIdentity(account, alias))),
            settings.MaximumClassificationWait)
        : SpamClassificationScope.None;

    /// <inheritdoc />
    public SpamClassificationSettings SettingsFor(MailOwnerId owner) => settings;
}
