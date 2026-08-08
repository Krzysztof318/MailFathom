// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.IntegrationTests.ProviderAdapters;

/// <summary>The one switch every test that calls a real AI provider is gated on, and the reason a run nobody asked for reports.</summary>
/// <remarks>
/// <para>
/// One switch rather than one per provider. Calling an AI provider is what costs money here, and which half of the AI
/// boundary spends it — an embedding of a mail body, an answer generated from one — is a distinction the operator
/// turning the tests on has no reason to make: they are asking for the provider-contract tests, and every one of them
/// bills the same account. A second switch would mean a provider added later runs free by default until somebody
/// remembers it exists.
/// </para>
/// <para>
/// A paid call is never the default, in the running service and in verification alike. This variable is absent on a
/// developer's machine and on an ordinary pipeline run, which is what makes the tests reading it skip and cost nothing;
/// the `Integration tests` workflow turns it on through an input that defaults to off and supplies the credentials with
/// it. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>.
/// </para>
/// <para>
/// Asking for the run without what it needs fails rather than skips, and that asymmetry is the point: a run somebody
/// explicitly requested and which then quietly proved nothing is worse than one that never started.
/// </para>
/// </remarks>
internal static class AiProviderContractRun
{
    /// <summary>The variable that turns the provider-contract tests on. Nothing sets it by default.</summary>
    public const string EnablingVariable = "MAILFATHOM_AI_CONTRACT_TESTS";

    /// <summary>The reason a run nobody asked for reports against each skipped test.</summary>
    /// <remarks>
    /// xUnit requires this message beside a conditional skip and reads it before it reads the condition, so a test
    /// carrying <c>SkipUnless</c> without <c>Skip</c> fails on every run rather than skipping on the ones that asked
    /// for nothing.
    /// </remarks>
    public const string SkipReason =
        $"A provider-contract run calls a real AI provider and spends credit, so it happens only when {EnablingVariable} is set to true.";

    /// <summary>Gets whether a run was explicitly asked for.</summary>
    /// <remarks>
    /// Read as a property rather than at class initialization, because xUnit evaluates the skip condition per test and
    /// a static read at load time would fix the answer before the runner had set anything.
    /// </remarks>
    public static bool Requested =>
        Environment.GetEnvironmentVariable(EnablingVariable) is { Length: > 0 } requested
        && bool.TryParse(requested, out var enabled)
        && enabled;

    /// <summary>Reads a variable a requested run cannot proceed without.</summary>
    /// <param name="variableName">The variable to read.</param>
    /// <returns>Its value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the run was asked for without it.</exception>
    public static string Required(string variableName) =>
        Environment.GetEnvironmentVariable(variableName) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"The provider-contract tests were turned on through {EnablingVariable} and {variableName} is not set. "
                + "A run that was asked for and then proved nothing is worse than one that never started, so this fails "
                + "rather than skipping.");
}
