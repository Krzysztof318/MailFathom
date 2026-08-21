// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery;

/// <summary>What one refusal of an authored message is published as: a code to look up and a sentence to read.</summary>
/// <param name="Code">The stable identity a boundary reports and an operator looks up.</param>
/// <param name="Message">The client-safe sentence, which names a field, a bound, or a count and never a value.</param>
/// <remarks>
/// It exists because a message somebody authored can be refused on the way to two different endings — a send that is
/// never queued and a draft that is never stored — and every reason it can be refused for is the same in both. Deciding
/// the code and the sentence once means the two endings cannot start disagreeing about what an author did wrong.
/// </remarks>
internal sealed record PublishedMailRefusal(MailFathomErrorCode Code, string Message);
