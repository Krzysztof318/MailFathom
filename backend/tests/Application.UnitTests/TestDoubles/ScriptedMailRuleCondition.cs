// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Facts;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>A condition that answers as the test says, raises as the test says, or never answers at all.</summary>
/// <remarks>
/// The three shapes are what the evaluator has to tell apart: a rule that decided, a rule that could not, and a rule
/// that outlasted its own timeout. A condition that never answers is the only way to reach the third without a real
/// clock.
/// </remarks>
internal sealed class ScriptedMailRuleCondition : IMailRuleCondition
{
    private readonly bool matches;
    private readonly Exception? failure;
    private readonly bool neverAnswers;
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ScriptedMailRuleCondition(bool matches, Exception? failure, bool neverAnswers)
    {
        this.matches = matches;
        this.failure = failure;
        this.neverAnswers = neverAnswers;
    }

    /// <summary>Gets how often the condition has been asked about an email.</summary>
    public int EvaluationCount { get; private set; }

    /// <summary>Gets a task that completes once the condition has been entered, so a test can move a clock behind it.</summary>
    public Task Started => this.started.Task;

    /// <inheritdoc />
    public IReadOnlyList<MailRuleFact> ReferencedFacts { get; private init; } = [];

    /// <summary>Creates a condition that answers the same way about every email, naming the facts it reads.</summary>
    /// <remarks>
    /// The named facts are resolved before the answer, because naming a fact and reading it are the same thing for a
    /// real condition: a test about what a rule set costs would prove nothing against a double that declared a fact and
    /// never asked for it.
    /// </remarks>
    public static ScriptedMailRuleCondition Answering(bool matches, params MailRuleFact[] referencedFacts) =>
        new(matches, failure: null, neverAnswers: false) { ReferencedFacts = referencedFacts };

    /// <summary>Creates a condition that raises instead of answering.</summary>
    public static ScriptedMailRuleCondition Raising(Exception failure) => new(matches: false, failure, neverAnswers: false);

    /// <summary>Creates a condition that waits for its own cancellation rather than answering.</summary>
    public static ScriptedMailRuleCondition NeverAnswering() => new(matches: false, failure: null, neverAnswers: true);

    /// <inheritdoc />
    public async Task<bool> EvaluateAsync(MailRuleFacts facts, CancellationToken cancellationToken)
    {
        this.EvaluationCount++;
        this.started.TrySetResult();

        foreach (var fact in this.ReferencedFacts)
        {
            await facts.ResolveAsync(fact, cancellationToken);
        }

        if (this.failure is { } raised)
        {
            throw raised;
        }

        if (this.neverAnswers)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return this.matches;
    }
}
