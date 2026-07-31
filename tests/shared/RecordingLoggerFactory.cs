// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging;

namespace MailMcp.TestSupport;

/// <summary>A logging pipeline a test can hand to an owner and then prove was released.</summary>
/// <remarks>
/// Capture itself belongs to <see cref="RecordingLoggerProvider" />, which this type delegates to rather than
/// reimplementing. What it adds is the one thing an <see cref="ILoggerProvider" /> cannot express: a
/// factory whose disposal is observable. Ownership of a logging pipeline is a contract wherever a type builds one
/// outside the container, and a test can only hold it to that contract by watching the factory it handed over.
/// </remarks>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly RecordingLoggerProvider provider = new();

    /// <summary>Gets a snapshot of everything logged so far, in the order it was written.</summary>
    public IReadOnlyCollection<RecordingLoggerProvider.LogRecord> Records => this.provider.Records;

    /// <summary>Gets the number of times the owner released this pipeline.</summary>
    public int DisposeCount { get; private set; }

    /// <summary>Gets the category the owner created its logger under.</summary>
    public string? CategoryName { get; private set; }

    public ILogger CreateLogger(string categoryName)
    {
        this.CategoryName = categoryName;

        return this.provider.CreateLogger(categoryName);
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
        this.DisposeCount++;

        // The captured records outlive this call, so an assertion made after the owner released the pipeline still
        // reads what was written to it.
        this.provider.Dispose();
    }
}
