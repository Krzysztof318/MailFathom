// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;

namespace MailFathom.Host.UnitTests;

/// <summary>Publishes one fixed snapshot, so a test can exercise a consumer without the reload machinery behind it.</summary>
internal sealed class StubSettingsSnapshot<TSettings>(TSettings current) : ISettingsSnapshot<TSettings>
    where TSettings : class
{
    public TSettings Current { get; set; } = current;
}
