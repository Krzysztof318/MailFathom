// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Threads;

/// <summary>One message identifier an account already binds to a conversation.</summary>
/// <param name="Identifier">The message identifier, exactly as the mail wrote and this system stored it.</param>
/// <param name="ThreadId">The conversation the identifier belongs to.</param>
/// <param name="ThreadAssembledAt">When that conversation was first assembled, which is what decides a merge.</param>
/// <remarks>
/// The conversation's age travels with the binding rather than being asked for afterwards, because a merge needs it for
/// every conversation the arriving message named and a second lookup would ask the same question again for the ordinary
/// case, where it named exactly one.
/// </remarks>
public sealed record EmailThreadBinding(
    string Identifier,
    EmailThreadId ThreadId,
    DateTimeOffset ThreadAssembledAt);
