// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Answers with what a test stated the deployment decided about classifying mail.</summary>
/// <remarks>
/// The deployed reader resolves its answer from the bound configuration section and from the account's inbox mapping,
/// neither of which the suite binds. Stating the settings directly keeps this suite's tests about what classification
/// does with them rather than about how a section is read, which the host's own unit tests already establish.
/// </remarks>
internal sealed class FixedSpamClassificationSettingsReader(SpamClassificationSettings settings)
    : ISpamClassificationSettingsReader
{
    /// <inheritdoc />
    public SpamClassificationSettings Settings => settings;
}
