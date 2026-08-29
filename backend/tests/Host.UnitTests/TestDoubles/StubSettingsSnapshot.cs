// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Publishes one fixed snapshot, so a test can exercise a consumer without the reload machinery behind it.</summary>
internal sealed class StubSettingsSnapshot<TSettings>(TSettings current) : ISettingsSnapshot<TSettings>
    where TSettings : class
{
    private ConfigurationReloadToken reloadToken = new();

    public TSettings Current
    {
        get;
        set
        {
            field = value;

            var changed = Interlocked.Exchange(ref this.reloadToken, new ConfigurationReloadToken());
            changed.OnReload();
        }
    } = current;

    public IChangeToken GetReloadToken() => Volatile.Read(ref this.reloadToken);
}
