// Copyright © 2026 Krzysztof Kasprowicz

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MailMcp.TestSupport;

/// <summary>Captures every log record a composed container produced, whichever category wrote it.</summary>
/// <remarks>
/// <para>
/// A test uses this when logging is part of the contract rather than incidental: a component that promises to name a
/// failure without naming the data behind it can only be held to that promise by reading what it actually wrote,
/// including the records written by a library it configured.
/// </para>
/// <para>
/// The provider is compiled into each test project that needs it from <c>tests/shared/</c>, so a helper stays one
/// implementation rather than one per boundary. It records rather than asserts, and records are safe to read from
/// any thread.
/// </para>
/// </remarks>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogRecord> records = new();

    /// <summary>Gets a snapshot of everything logged so far, in the order it was written.</summary>
    internal IReadOnlyCollection<LogRecord> Records => [.. this.records];

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, this.records);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>One captured log record.</summary>
    /// <param name="Category">The logger category that wrote the record.</param>
    /// <param name="Level">The severity the record was written at.</param>
    /// <param name="Message">The formatted message.</param>
    /// <param name="Failure">The exception attached to the record, when one was.</param>
    internal sealed record LogRecord(string Category, LogLevel Level, string Message, Exception? Failure);

    private sealed class RecordingLogger : ILogger
    {
        private readonly string categoryName;
        private readonly ConcurrentQueue<LogRecord> records;

        internal RecordingLogger(string categoryName, ConcurrentQueue<LogRecord> records)
        {
            this.categoryName = categoryName;
            this.records = records;
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

            this.records.Enqueue(new LogRecord(this.categoryName, logLevel, formatter(state, exception), exception));
        }
    }
}
