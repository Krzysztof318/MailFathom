// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.AiProviders;

/// <summary>Names what an outbound AI provider is called for.</summary>
/// <remarks>
/// The two are declared, credentialed, called, and reported on separately, and an instance may hold one and not the
/// other. This enumeration is what keeps that separation expressible in one place rather than as two parallel sets of
/// types: the states are read and written per role, so nothing can collapse them into a single "AI is configured" flag.
/// </remarks>
public enum AiProviderRole
{
    /// <summary>The provider that turns a passage into a vector.</summary>
    Embedding = 0,

    /// <summary>The provider that turns a conversation into generated text.</summary>
    Chat = 1,
}
