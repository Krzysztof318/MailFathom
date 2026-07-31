// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MailFathom.TestSupport;

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
    private const string OriginalFormatPropertyName = "{OriginalFormat}";

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
    /// <param name="Properties">
    /// The named values the record carries, without the message template itself. A test that has to show a component
    /// published exactly one set of facts asserts over this rather than searching the formatted text, because a
    /// substring search proves that a value is absent from one rendering, not that it was never attached.
    /// </param>
    internal sealed record LogRecord(
        string Category,
        LogLevel Level,
        string Message,
        Exception? Failure,
        IReadOnlyDictionary<string, object?> Properties);

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

            // The message template arrives as a property of its own; dropping it leaves the values the record carries.
            var properties = state is IReadOnlyList<KeyValuePair<string, object?>> namedValues
                ? namedValues
                    .Where(property => property.Key != OriginalFormatPropertyName)
                    .ToDictionary(property => property.Key, property => property.Value)
                : [];

            this.records.Enqueue(
                new LogRecord(this.categoryName, logLevel, formatter(state, exception), exception, properties));
        }
    }
}
