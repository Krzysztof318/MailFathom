// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Agent.Answering;

/// <summary>What one turn of an Agent conversation may send, in tokens, before its earlier part is compacted.</summary>
/// <remarks>
/// <para>
/// <strong>The deployment's, and one number for the whole application.</strong> It is not per person and not per
/// conversation, and it is not read from the model: a provider's own window is what a call would be refused at, while
/// this is what the deployment has decided to spend per turn — so an operator running a larger model raises it and
/// nothing else changes.
/// </para>
/// <para>
/// It bounds what a turn <em>sends</em>, never what the conversation <em>holds</em>: compaction adds a summary and
/// removes nothing, and what a person reads back is every original message however many compactions it went through.
/// </para>
/// </remarks>
public sealed record AgentContextBudget
{
    /// <summary>What a deployment that states nothing lets one turn send.</summary>
    public const int DefaultTokens = 64_000;

    /// <summary>The least a deployment may state, below which a summary and the question would not fit together.</summary>
    public const int MinimumTokens = 1_000;

    /// <summary>The most a deployment may state.</summary>
    public const int MaximumTokens = 10_000_000;

    /// <summary>Initializes the budget.</summary>
    /// <param name="tokens">What one turn may send, in tokens.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="tokens" /> is outside <see cref="MinimumTokens" /> and <see cref="MaximumTokens" />.</exception>
    public AgentContextBudget(int tokens)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tokens, MinimumTokens);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tokens, MaximumTokens);

        this.Tokens = tokens;
    }

    /// <summary>Gets the budget a deployment that states none receives.</summary>
    public static AgentContextBudget Default { get; } = new(DefaultTokens);

    /// <summary>Gets what one turn may send, in tokens.</summary>
    public int Tokens { get; }
}
