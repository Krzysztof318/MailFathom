// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Chat;

/// <summary>What a chat provider produced for one conversation.</summary>
/// <param name="Text">The generated text, which is never empty: a call that produced none is a failure rather than an answer.</param>
/// <param name="Stop">Why the model stopped generating.</param>
/// <param name="Usage">What the call consumed, or <see langword="null" /> where the provider reported nothing.</param>
/// <remarks>
/// The text is model output over untrusted input and is treated as untrusted itself: it is encoded for whatever
/// destination presents it, and it never reaches a log, a span, or an exporter. <paramref name="Usage" /> is the part
/// that may, because a count says how much was sent without saying any of it.
/// </remarks>
public sealed record ChatAnswer(string Text, ChatGenerationStop Stop, ChatTokenUsage? Usage);
