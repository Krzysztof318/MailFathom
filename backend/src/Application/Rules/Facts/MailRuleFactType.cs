// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Facts;

/// <summary>Names the shape of value a fact carries, which is what a condition is type-checked against when it is read.</summary>
/// <remarks>
/// The set is what makes authoring-time type checking possible at all. A condition language with no static type checker
/// would otherwise discover that a subject was compared to a number on live mail, so every fact declares its shape here
/// and the walk that validates an expression judges every comparison, operator, and argument against it.
/// </remarks>
public enum MailRuleFactType
{
    /// <summary>A single string, which may be absent.</summary>
    Text = 0,

    /// <summary>A bounded set of strings, which is tested for membership rather than compared.</summary>
    TextSet = 1,

    /// <summary>A number, which may be absent.</summary>
    Number = 2,

    /// <summary>A boolean, which is never absent.</summary>
    Boolean = 3,

    /// <summary>An instant in time, which may be absent and is always in UTC.</summary>
    Timestamp = 4,
}
