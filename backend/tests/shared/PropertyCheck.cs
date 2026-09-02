// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using CsCheck;

namespace MailFathom.TestSupport;

/// <summary>Runs one stated invariant over generated inputs, under the settings every property here is checked with.</summary>
/// <remarks>
/// <para>
/// Shared because the settings are the contract rather than a preference. A property that ran on a different thread
/// count or a different iteration budget in each suite would make "this invariant holds" mean something different per
/// project, and the two decisions below are the ones that would drift first.
/// </para>
/// <para>
/// <b>One thread.</b> The suite already runs its collections in parallel, so a property fanning out across every
/// logical processor would oversubscribe the machine and change the timing of every test running beside it. What the
/// fan-out buys is wall-clock on a long sample, and these samples are hundreds of iterations of pure functions.
/// </para>
/// <para>
/// <b>The seed is the repro.</b> Generation is seeded per iteration and the seed of the failing iteration is named in
/// the failure, after the case has been shrunk towards the simplest input that still breaks the invariant. Re-running
/// with <c>CsCheck_Seed</c> set to it reproduces that one case, so a counterexample arrives as a two-line repro rather
/// than as a search. Nothing here pins the seed of the run: a property that generated the same inputs on every run
/// would be a fixed corpus of examples wearing a generator's clothes.
/// </para>
/// </remarks>
internal static class PropertyCheck
{
    /// <summary>How many threads a sample runs on, and why the number is one.</summary>
    private const int SingleThread = 1;

    /// <summary>Checks that an invariant holds for every generated input, shrinking the first that breaks it.</summary>
    /// <typeparam name="TInput">What the invariant is stated over.</typeparam>
    /// <param name="inputs">Generates the inputs the invariant is claimed for.</param>
    /// <param name="invariant">Asserts the invariant, raising an exception for an input that breaks it.</param>
    /// <param name="iterations">How many inputs to draw.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="iterations" /> is not positive.</exception>
    internal static void Holds<TInput>(Gen<TInput> inputs, Action<TInput> invariant, int iterations)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(invariant);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);

        inputs.Sample(invariant, iter: iterations, threads: SingleThread);
    }

    /// <summary>Checks that an invariant of an asynchronous operation holds for every generated input.</summary>
    /// <typeparam name="TInput">What the invariant is stated over.</typeparam>
    /// <param name="inputs">Generates the inputs the invariant is claimed for.</param>
    /// <param name="invariant">Asserts the invariant, raising an exception for an input that breaks it.</param>
    /// <param name="iterations">How many inputs to draw.</param>
    /// <returns>A task that completes when every input has been drawn and checked.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="iterations" /> is not positive.</exception>
    internal static Task HoldsAsync<TInput>(Gen<TInput> inputs, Func<TInput, Task> invariant, int iterations)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(invariant);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);

        return inputs.SampleAsync(invariant, iter: iterations, threads: SingleThread);
    }
}
