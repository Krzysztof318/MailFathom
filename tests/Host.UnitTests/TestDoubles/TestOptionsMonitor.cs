// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Options;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Reports configuration reloads on demand, so a test drives them instead of waiting for a file watcher.</summary>
internal sealed class TestOptionsMonitor<TOptions>(TOptions initialValue) : IOptionsMonitor<TOptions>
{
    private readonly List<Action<TOptions, string?>> listeners = [];

    public TOptions CurrentValue { get; private set; } = initialValue;

    public TOptions Get(string? name) => this.CurrentValue;

    public IDisposable OnChange(Action<TOptions, string?> listener)
    {
        this.listeners.Add(listener);

        return new Subscription(this.listeners, listener);
    }

    /// <summary>Reports a reload the way a configuration provider would.</summary>
    /// <param name="reloadedValue">The newly bound snapshot.</param>
    /// <param name="name">The options name, which is the default one unless a test exercises named options.</param>
    public void ReportReload(TOptions reloadedValue, string? name = null)
    {
        this.CurrentValue = reloadedValue;

        foreach (var listener in this.listeners.ToArray())
        {
            listener(reloadedValue, name);
        }
    }

    private sealed class Subscription(List<Action<TOptions, string?>> listeners, Action<TOptions, string?> listener) : IDisposable
    {
        public void Dispose() => listeners.Remove(listener);
    }
}
