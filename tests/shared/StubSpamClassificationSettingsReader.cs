// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;

namespace MailFathom.TestSupport;

/// <summary>Answers with fixed classification settings, for the paths that only read whether the feature is on.</summary>
internal sealed class StubSpamClassificationSettingsReader(SpamClassificationSettings settings)
    : ISpamClassificationSettingsReader
{
    /// <summary>Gets a reader for the deployment that configured nothing, which classifies no mail.</summary>
    public static StubSpamClassificationSettingsReader Disabled { get; } = new(SpamClassificationSettings.Disabled);

    /// <inheritdoc />
    public SpamClassificationSettings Settings => settings;
}
