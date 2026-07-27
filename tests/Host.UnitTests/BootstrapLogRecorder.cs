// Copyright © 2026 Krzysztof Kasprowicz

using Microsoft.Extensions.Logging;

namespace MailMcp.Host.UnitTests;

/// <summary>Stands in for the bootstrap logging pipeline, capturing every record and counting how often it is released.</summary>
/// <remarks>
/// The startup records are a contract rather than incidental output: an operator diagnoses a host that never began
/// listening from them alone, so their level, their named properties, and the absence of anything else are asserted.
/// Disposal is counted because the bootstrap logger owns this pipeline and is the only thing that will ever close it.
/// </remarks>
internal sealed class BootstrapLogRecorder : ILoggerFactory, ILogger
{
    private readonly List<RecordedLogEntry> entries = [];

    /// <summary>Gets the records written so far, in the order they were written.</summary>
    public IReadOnlyList<RecordedLogEntry> Entries => this.entries;

    /// <summary>Gets the number of times the owner released this pipeline.</summary>
    public int DisposeCount { get; private set; }

    /// <summary>Gets the category the owner created its logger under.</summary>
    public string? CategoryName { get; private set; }

    public ILogger CreateLogger(string categoryName)
    {
        this.CategoryName = categoryName;

        return this;
    }

    public void AddProvider(ILoggerProvider provider)
    {
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

        // The message template itself arrives as a property; keeping it out leaves the named values the record carries.
        var properties = state is IReadOnlyList<KeyValuePair<string, object?>> namedValues
            ? namedValues
                .Where(property => property.Key != "{OriginalFormat}")
                .ToDictionary(property => property.Key, property => property.Value)
            : [];

        this.entries.Add(new RecordedLogEntry(logLevel, formatter(state, exception), exception, properties));
    }

    public void Dispose() => this.DisposeCount++;
}
