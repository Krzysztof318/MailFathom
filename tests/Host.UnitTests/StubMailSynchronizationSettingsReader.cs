// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;

namespace MailMcp.Host.UnitTests;

/// <summary>Publishes one fixed snapshot, so a test can exercise a consumer without the reload machinery behind it.</summary>
internal sealed class StubMailSynchronizationSettingsReader(MailSynchronizationOptions current) : IMailSynchronizationSettingsReader
{
    public MailSynchronizationOptions Current { get; set; } = current;
}
