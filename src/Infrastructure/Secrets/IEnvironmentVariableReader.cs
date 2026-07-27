// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Reads one process environment variable.</summary>
/// <remarks>
/// The port exists so scheme adapters are unit-testable without mutating the real environment block. It returns a
/// <see cref="string" /> because the platform API does, which is why environment-sourced material arrives already
/// un-erasable.
/// </remarks>
internal interface IEnvironmentVariableReader
{
    /// <summary>Gets the value of one environment variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The value, or <see langword="null" /> when the variable is unset.</returns>
    string? GetValue(string name);
}
