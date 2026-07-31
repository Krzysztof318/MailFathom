// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Host.Configuration;

namespace MailMcp.Host.UnitTests;

/// <summary>Publishes one fixed snapshot, so a test can exercise a consumer without the reload machinery behind it.</summary>
internal sealed class StubSettingsSnapshot<TSettings>(TSettings current) : ISettingsSnapshot<TSettings>
    where TSettings : class
{
    public TSettings Current { get; set; } = current;
}
