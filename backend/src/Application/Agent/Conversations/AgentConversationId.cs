// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Agent.Conversations;

/// <summary>Identifies one Agent conversation for as long as the person whose conversation it is keeps it.</summary>
/// <remarks>
/// A conversation is durable rather than held for the length of a run, so this outlives every process that wrote into
/// it and is what a history entry, a read, and a deletion all address. It is a version 4 UUID because it is handed to a
/// client and presented back — a guessable identifier would let one caller ask to be shown somebody else's
/// conversation, which the owner check beside it refuses but which nothing should be able to attempt cheaply.
/// </remarks>
public readonly record struct AgentConversationId
{
    private AgentConversationId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an identifier for a conversation nothing has started yet.</summary>
    /// <returns>A new identifier, drawn from the platform's generator.</returns>
    public static AgentConversationId New() => new(Guid.NewGuid());

    /// <summary>Creates a conversation identifier from a non-empty UUID, which is how one arrives back off the wire.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated conversation identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static AgentConversationId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An Agent conversation identifier cannot be empty.", nameof(value));
        }

        return new AgentConversationId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
