// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Secrets.Sources;

/// <summary>Reads environment variables from the running process.</summary>
/// <remarks>
/// This is the one place the process environment is read because an operator asked for it rather than in spite of the
/// configuration pipeline. An <c>env:</c> secret reference names a variable, so the environment is the source the
/// setting itself selected, and resolving it through <c>IConfiguration</c> would let a value from an
/// <c>appsettings.json</c> file satisfy a reference that promised to read the environment — turning a deployment's
/// choice about where credential material lives into a detail of source precedence.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ProcessEnvironmentVariableReader : IEnvironmentVariableReader
{
    /// <inheritdoc />
    public string? GetValue(string name) => Environment.GetEnvironmentVariable(name);
}
