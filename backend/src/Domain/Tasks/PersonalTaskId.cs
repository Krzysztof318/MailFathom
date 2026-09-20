// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Tasks;

/// <summary>Identifies one task, independently of whatever produced it.</summary>
/// <remarks>
/// It is an identity of its own rather than the identity of the message a task may cite, because most tasks cite no
/// message and two tasks derived from one message are two things a person owes.
/// </remarks>
public readonly record struct PersonalTaskId
{
    private PersonalTaskId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Gets whether this identifier names a task.</summary>
    /// <remarks>
    /// Being a struct, <see langword="default" /> is reachable and addresses nothing; the private constructor is what
    /// keeps every other value validated, and this is what reports the one it cannot reach.
    /// </remarks>
    public bool IsSpecified => this.Value != Guid.Empty;

    /// <summary>Creates a task identifier from a non-empty UUID.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated task identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static PersonalTaskId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A task identifier cannot be empty.", nameof(value));
        }

        return new PersonalTaskId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
