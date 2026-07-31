// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailMcp.Infrastructure.Secrets;

/// <summary>The deployment-wide choice of how secret-bearing configuration values are interpreted.</summary>
/// <param name="Interpretation">The active interpretation mode.</param>
/// <remarks>
/// The mode is supplied at registration rather than read from configuration inside this assembly, so the resolution
/// stack needs no hosting dependency and the rule stays trivially testable in all three directions.
/// </remarks>
public sealed record SecretResolutionOptions(SecretValueInterpretation Interpretation);
