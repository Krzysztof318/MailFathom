// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.Json;
using MailFathom.Cli.Diagnostics;

namespace MailFathom.Cli.UnitTests;

/// <summary>Reads a written line of the invocation log back as the record it was serialized from.</summary>
/// <remarks>Through the command's own contract rather than a second one written here, so a test asserting a field is asserting what an operator's <c>jq</c> would find rather than what this file believes was written.</remarks>
internal static class CliInvocationLogLine
{
    /// <summary>Reads one line of the log.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The record it holds.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the line holds no record.</exception>
    internal static CliInvocationEntry Read(this string line) =>
        JsonSerializer.Deserialize(
            Encoding.UTF8.GetBytes(line),
            CliInvocationLogJsonContext.Default.CliInvocationEntry)
        ?? throw new InvalidOperationException("The line held no record.");
}
