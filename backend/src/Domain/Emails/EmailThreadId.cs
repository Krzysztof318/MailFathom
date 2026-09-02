// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>Identifies one conversation the stored mail of a single account was assembled into.</summary>
/// <remarks>
/// <para>
/// A local identity rather than a message identifier, for the reason <see cref="StoredEmailId" /> is one: a conversation
/// is a relation this deployment established between rows it holds, and no header names it. Two accounts that both hold
/// the same exchange therefore hold two threads, because a thread is owned by the account whose mail it assembles.
/// </para>
/// <para>
/// The identity outlives a merge. When a later message proves two assembled threads were always one, the surviving
/// thread keeps its own identifier and the merged one keeps a row naming the survivor, so an identifier a tool published
/// before the merge still resolves to the conversation it named.
/// </para>
/// </remarks>
public readonly record struct EmailThreadId
{
    private EmailThreadId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a thread identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated thread identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static EmailThreadId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An email thread identifier cannot be empty.", nameof(value));
        }

        return new EmailThreadId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
