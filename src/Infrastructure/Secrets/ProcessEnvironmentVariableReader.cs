// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Reads environment variables from the running process.</summary>
// TODO: Remove this exclusion when the planned host integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Real environment access will be covered later by host integration tests.")]
internal sealed class ProcessEnvironmentVariableReader : IEnvironmentVariableReader
{
    /// <inheritdoc />
    public string? GetValue(string name) => Environment.GetEnvironmentVariable(name);
}
