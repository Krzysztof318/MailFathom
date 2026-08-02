// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Secrets;

/// <summary>One secret-bearing setting found in a bound options graph, with the configuration path that reached it.</summary>
/// <param name="ConfigurationPath">The colon-separated path an operator edits, for example <c>MailSynchronization:Accounts:0:Secrets:Password</c>.</param>
/// <param name="Secret">The bound block. Discovery never reads its value, so nothing in the walk can reach a diagnostic.</param>
public sealed record DiscoveredSecret(string ConfigurationPath, ConfiguredSecret Secret);
