// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>Which of the four things a derived statement says about where a conversation stands.</summary>
/// <remarks>
/// <para>
/// The four read differently and are drawn apart from one another, which is why they are an aspect rather than one
/// list of sentences: somebody scanning for what they still owe should not have to find it among what was settled.
/// </para>
/// <para>
/// A fifth is not added lightly. Each of these is something a reader acts on differently — an agreement is relied on,
/// an open question is answered, a commitment is kept, and a difference between two versions of a document is checked
/// — and an aspect nobody acts on differently is a sentence that belongs in one of the four.
/// </para>
/// </remarks>
public enum ThreadStateAspect
{
    /// <summary>Something the conversation settled.</summary>
    Agreement = 0,

    /// <summary>Something the conversation raised and has not settled.</summary>
    OpenQuestion = 1,

    /// <summary>Something somebody undertook to do, which may name who owes it and when.</summary>
    Commitment = 2,

    /// <summary>How one version of a document the conversation exchanged differs from the version before it.</summary>
    VersionDifference = 3,
}
