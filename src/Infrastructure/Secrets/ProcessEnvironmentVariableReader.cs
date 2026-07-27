// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.CodeCoverage;

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Reads environment variables from the running process.</summary>
[RequiresIntegrationCoverage]
internal sealed class ProcessEnvironmentVariableReader : IEnvironmentVariableReader
{
    /// <inheritdoc />
    public string? GetValue(string name) => Environment.GetEnvironmentVariable(name);
}
