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
/// <paramref name="ObservedAt" /> records when the state was established and nothing reads it to decide anything yet.
/// The health check reports <paramref name="State" /> alone, so a provider that failed once during a deployment and has
/// not been called since reports exactly what one that failed a moment ago reports. It is carried because the state is
/// otherwise undatable — a reader that wants to tell a stale failure from a current one has no way to derive the moment
/// afterwards — and treating that distinction as already made is the mistake to avoid, not the one to describe.
/// </para>
/// <para>
/// Anything that does come to act on it owes a staleness window, which is a deployment's decision rather than this
/// type's: how long a provider may go unasked before its last failure stops being news.
/// </para>
/// </remarks>
public sealed record AiProviderHealth(AiProviderRole Role, AiProviderHealthState State, DateTimeOffset? ObservedAt);
