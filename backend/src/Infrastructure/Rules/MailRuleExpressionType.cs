// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Rules;

/// <summary>The type a part of a parsed condition carries, as the walk that checks the condition works it out.</summary>
/// <remarks>
/// The same shapes the fact surface declares, plus the two a fact can never have.
/// <see cref="Invalid" /> is what a part that has already been reported as wrong carries, so
/// that one mistake produces one message instead of one per enclosing operator, and
/// <see cref="ArgumentList" /> is what a parenthesized list carries in the one position the
/// grammar allows it.
/// </remarks>
internal enum MailRuleExpressionType
{
    /// <summary>A single string.</summary>
    Text = 0,

    /// <summary>A bounded set of strings.</summary>
    TextSet = 1,

    /// <summary>A number.</summary>
    Number = 2,

    /// <summary>A boolean.</summary>
    Boolean = 3,

    /// <summary>An instant in time.</summary>
    Timestamp = 4,

    /// <summary>A part that has already been reported as wrong, which suppresses further messages about it.</summary>
    Invalid = 5,

    /// <summary>A parenthesized list of values, which is only meaningful to the right of a membership test.</summary>
    ArgumentList = 6,
}
