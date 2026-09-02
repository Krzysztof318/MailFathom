// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Logging;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Captures formatted log messages so a test can assert what startup told the operator — and what it did not.</summary>
/// <remarks>
/// <para>
/// Logging is part of the contract here rather than incidental: the host promises to name every setting that resolved
/// to an inline value and to keep secret material out of every line it writes.
/// </para>
/// <para>
/// Writes are serialized because the accounts and folders a supervised synchronization run logs from are genuinely
/// concurrent, and a test that read a torn list would report a fault the code under test does not have.
/// </para>
/// </remarks>
internal sealed class RecordingLogger<TCategory> : ILogger<TCategory>
{
    private readonly Lock recordedMessages = new();
    private readonly List<string> messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (this.recordedMessages)
            {
                return [.. this.messages];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        lock (this.recordedMessages)
        {
            this.messages.Add(formatter(state, exception));
        }
    }
}
