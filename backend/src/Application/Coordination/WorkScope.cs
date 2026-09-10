// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Coordination;

/// <summary>Names one unit of work that must not run twice, in the words an operator reads it back by.</summary>
/// <remarks>
/// <para>
/// The scope is keyed exactly as the work's own progress is keyed, which is what stops the two from ever disagreeing
/// about what one unit of the work is: where progress is one row for the deployment the scope is deployment-wide, where
/// it is one row per account the scope is the account, and where an operator names the scope they name the lease with
/// it. Nothing here decides that — the caller composes the text from the key its position row already carries.
/// </para>
/// <para>
/// It is composed of MailFathom's own names and identifiers — an account alias, a user identity, the name of a walk —
/// and never of a subject, an address, or anything else out of a message. An operator reads this text when they ask who
/// holds what, so a digest would be shorter and would tell them nothing; mail content in it would make the lease table
/// a second uncontrolled copy of personal data.
/// </para>
/// </remarks>
public sealed record WorkScope
{
    /// <summary>The greatest length a scope may have, which bounds the key column it is stored in.</summary>
    public const int MaximumLength = 256;

    private WorkScope(string value) => this.Value = value;

    /// <summary>Gets the text one unit of work is held under.</summary>
    public string Value { get; }

    /// <summary>Creates a scope from the key the work's own progress is recorded under.</summary>
    /// <param name="value">The composed name of one unit of work.</param>
    /// <returns>A validated scope.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank, carries a control character, or is longer than <see cref="MaximumLength" />.</exception>
    public static WorkScope Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmedValue = value.Trim();

        if (trimmedValue.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"A work scope may be at most {MaximumLength} characters long.",
                nameof(value));
        }

        // A control character would make the scope unreadable in the query an operator asks who holds what with, and it
        // is never part of a name an operator wrote or an identity MailFathom generated.
        if (trimmedValue.Any(char.IsControl))
        {
            throw new ArgumentException("A work scope cannot contain a control character.", nameof(value));
        }

        return new WorkScope(trimmedValue);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
