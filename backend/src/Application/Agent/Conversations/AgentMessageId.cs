// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Agent.Conversations;

/// <summary>Identifies one message inside a conversation, and so the answer a run is composing into.</summary>
/// <remarks>
/// It is what every part of an answer names as it is written — a status line, a block, a proposal, and the ending all
/// carry it — so a conversation that holds two answers never has to infer which one a part belongs to from where it
/// sits in the order.
/// </remarks>
public readonly record struct AgentMessageId
{
    private AgentMessageId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Creates an identifier for a message nothing has written yet.</summary>
    /// <returns>A new identifier, drawn from the platform's generator.</returns>
    public static AgentMessageId New() => new(Guid.NewGuid());

    /// <summary>Creates a message identifier from a non-empty UUID, which is how one arrives back off the wire.</summary>
    /// <param name="value">The UUID to wrap.</param>
    /// <returns>A validated message identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static AgentMessageId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An Agent message identifier cannot be empty.", nameof(value));
        }

        return new AgentMessageId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString();
}
