// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Secrets.Sources;

/// <summary>Reads environment variables from the running process.</summary>
[RequiresIntegrationCoverage]
internal sealed class ProcessEnvironmentVariableReader : IEnvironmentVariableReader
{
    /// <inheritdoc />
    public string? GetValue(string name) => Environment.GetEnvironmentVariable(name);
}
