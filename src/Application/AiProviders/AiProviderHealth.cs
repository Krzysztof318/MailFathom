// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.AiProviders;

/// <summary>What the last call to one AI provider established, and when.</summary>
/// <param name="Role">Which provider this describes.</param>
/// <param name="State">What the last call established about it.</param>
/// <param name="ObservedAt">When that call ended, or <see langword="null" /> while nothing has been observed.</param>
/// <remarks>
/// The timestamp is what separates a provider that is failing now from one that failed once during a deployment and has
/// not been asked since. Without it a state read hours after the fact reads as current, which is how an operator ends
/// up chasing an outage that ended before they were paged.
/// </remarks>
public sealed record AiProviderHealth(AiProviderRole Role, AiProviderHealthState State, DateTimeOffset? ObservedAt);
