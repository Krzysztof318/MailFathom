// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Evaluations;

/// <summary>The one switch every evaluation scenario is gated on, and what a requested run cannot proceed without.</summary>
/// <remarks>
/// <para>
/// A scenario calls a model under test and a judge, and both bill somebody's account, so a run nobody asked for costs
/// nothing: the variable is absent on a developer's machine and on every pull-request run, and the
/// <c>AI evaluations</c> workflow is what sets it.
/// </para>
/// <para>
/// Asking for the run without what it needs fails rather than skips, for the reason the provider-contract tests give: a
/// run somebody requested and which then quietly proved nothing is worse than one that never started. The failure names
/// the variable and never its value.
/// </para>
/// </remarks>
internal static class AiEvaluationRun
{
    /// <summary>The variable that turns the evaluation scenarios on. Nothing sets it by default.</summary>
    public const string EnablingVariable = "MAILFATHOM_AI_EVALUATIONS";

    /// <summary>The reason a run nobody asked for reports against each skipped scenario.</summary>
    /// <remarks>xUnit reads it before the condition, so a scenario setting <c>SkipUnless</c> without it fails on every run.</remarks>
    public const string SkipReason =
        $"An evaluation calls real models and spends credit, so it runs only when {EnablingVariable} is set to true.";

    /// <summary>Gets whether a run was explicitly asked for.</summary>
    /// <remarks>A property rather than a field, because xUnit evaluates a skip condition per test and after the runner started.</remarks>
    public static bool Requested =>
        Environment.GetEnvironmentVariable(EnablingVariable) is { Length: > 0 } requested
        && bool.TryParse(requested, out var enabled)
        && enabled;

    /// <summary>Reads a variable a requested run cannot proceed without.</summary>
    /// <param name="variableName">The variable to read.</param>
    /// <returns>Its value.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run was asked for without it.</exception>
    public static string Required(string variableName) =>
        Optional(variableName)
        ?? throw new InvalidOperationException(
            $"The evaluations were turned on through {EnablingVariable} and {variableName} is not set, so this fails "
            + "rather than skipping.");

    /// <summary>Reads a variable a run may leave unset.</summary>
    /// <param name="variableName">The variable to read.</param>
    /// <returns>Its value, or <see langword="null" /> where it is unset or empty.</returns>
    public static string? Optional(string variableName) =>
        Environment.GetEnvironmentVariable(variableName) is { Length: > 0 } value ? value : null;
}
