// Copyright © 2026 Krzysztof Kasprowicz

using Microsoft.Extensions.Logging;

namespace MailMcp.Host.UnitTests;

/// <summary>One log record captured by <see cref="BootstrapLogRecorder" />, kept whole so a test can assert what it carries and what it omits.</summary>
internal sealed record RecordedLogEntry(
    LogLevel Level,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties);
