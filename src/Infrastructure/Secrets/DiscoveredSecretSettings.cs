// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Everything one walk of a bound options graph found about its secret-bearing settings.</summary>
/// <param name="Blocks">Every <see cref="ConfiguredSecret" /> the walk reached, in discovery order.</param>
/// <param name="RawSecretPropertyPaths">
/// The configuration paths of <see cref="string" /> properties whose name announces a secret. Each one bypasses the
/// block shape and every rule that depends on it, so the host refuses to start rather than binding a secret it cannot
/// validate, resolve, or erase.
/// </param>
public sealed record DiscoveredSecretSettings(
    IReadOnlyList<DiscoveredSecret> Blocks,
    IReadOnlyList<string> RawSecretPropertyPaths);
