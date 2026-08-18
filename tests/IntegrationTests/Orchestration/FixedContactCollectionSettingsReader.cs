// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts.Collection;
using MailFathom.Domain.Accounts;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Answers with what a test stated the deployment decided about collecting contacts.</summary>
/// <remarks>
/// The deployed reader resolves its answer from the bound configuration section and from every account's own mailbox
/// address, neither of which the suite binds. Stating the settings directly keeps this suite's tests about what
/// collection does with them rather than about how a section is read, which the host's own unit tests establish. The
/// answer does not depend on the account, because the suite configures one.
/// </remarks>
internal sealed class FixedContactCollectionSettingsReader(ContactCollectionSettings settings)
    : IContactCollectionSettingsReader
{
    /// <inheritdoc />
    public ContactCollectionSettings SettingsFor(MailAccountId accountId) => settings;
}
