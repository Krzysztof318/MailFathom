// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.AiProviders;

/// <summary>What the last call to one AI provider established, and when.</summary>
/// <param name="Role">Which provider this describes.</param>
/// <param name="State">What the last call established about it.</param>
/// <param name="ObservedAt">When that call ended, or <see langword="null" /> while nothing has been observed.</param>
/// <remarks>
/// <para>
/// <paramref name="ObservedAt" /> records when the state was established, and what reads it is whatever has to decide
/// whether calling the provider again now would buy anything: the semantic read path for the embedding role, and the
/// answering capability for the chat one. The health check reads <paramref name="State" /> alone, so a provider that
/// failed once during a deployment and has not been called since probes exactly as one that failed a moment ago does.
/// </para>
/// <para>
/// A reader acting on the moment owes a staleness window of its own, because how long a provider may go unasked before
/// its last failure stops being news depends on what else calls it. Absence of a moment is treated as old rather than
/// fresh by both readers today, so an unstamped state can never be what withholds a capability indefinitely.
/// </para>
/// </remarks>
public sealed record AiProviderHealth(AiProviderRole Role, AiProviderHealthState State, DateTimeOffset? ObservedAt);
