// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using CsCheck;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers what every property in the repository is checked through, and what a broken invariant reports.</summary>
/// <remarks>
/// The two claims worth pinning are the ones a property test is worth nothing without: a counterexample arrives shrunk
/// towards the simplest input that still breaks the invariant, and the seed named beside it replays that one case. A
/// property whose failure named neither would report "some input somewhere fails", which is a search rather than a bug
/// report.
/// </remarks>
public sealed class PropertyCheckTests
{
    /// <summary>How many inputs each sample here draws, kept small because these invariants are about the harness.</summary>
    private const int Iterations = 200;

    /// <summary>The greatest input the samples below draw, and the boundary the broken invariant is stated at.</summary>
    private const int GreatestInput = 20;

    private const int Boundary = 10;

    private static readonly Gen<int> Inputs = Gen.Int[0, GreatestInput];

    [Fact]
    public void Holds_AnInvariantEveryInputSatisfies_ChecksTheDeclaredNumberOfInputsFromTheDeclaredDomain()
    {
        // Arrange
        var checkedInputs = new List<int>();

        // Act
        PropertyCheck.Holds(Inputs, checkedInputs.Add, Iterations);

        // Assert
        Assert.Equal(Iterations, checkedInputs.Count);
        Assert.All(checkedInputs, input => Assert.InRange(input, 0, GreatestInput));
    }

    /// <summary>A counterexample is only a bug report once it is the simplest input that still breaks the rule.</summary>
    [Fact]
    public void Holds_AnInvariantSomeInputsBreak_ShrinksTheCounterexampleToTheSimplestInputThatBreaksIt()
    {
        // Act
        var failure = Assert.Throws<CsCheckException>(
            () => PropertyCheck.Holds(Inputs, input => Assert.True(input < Boundary), Iterations));

        // Assert
        Assert.Equal(Boundary.ToString(CultureInfo.InvariantCulture), CounterexampleOf(failure));
    }

    /// <summary>
    /// The seed is what makes a counterexample reproducible rather than observed once, so the replay is asserted rather
    /// than assumed. It reaches the generator directly because <see cref="PropertyCheck" /> deliberately publishes no
    /// seed parameter: a developer replays by setting <c>CsCheck_Seed</c> for the run, and a test must not write a
    /// process-wide variable the rest of the suite would read.
    /// </summary>
    [Fact]
    public void Holds_AnInvariantSomeInputsBreak_NamesASeedThatReplaysTheSameCounterexample()
    {
        // Arrange
        var failure = Assert.Throws<CsCheckException>(
            () => PropertyCheck.Holds(Inputs, input => Assert.True(input < Boundary), Iterations));

        // Act
        var replayed = Assert.Throws<CsCheckException>(
            () => Inputs.Sample(input => Assert.True(input < Boundary), seed: SeedOf(failure), iter: 1, threads: 1));

        // Assert
        Assert.Equal(CounterexampleOf(failure), CounterexampleOf(replayed));
    }

    [Fact]
    public async Task HoldsAsync_AnInvariantEveryInputSatisfies_ChecksTheDeclaredNumberOfInputs()
    {
        // Arrange
        var checkedInputs = new List<int>();

        // Act
        await PropertyCheck.HoldsAsync(
            Inputs,
            input =>
            {
                checkedInputs.Add(input);

                return Task.CompletedTask;
            },
            Iterations);

        // Assert
        Assert.Equal(Iterations, checkedInputs.Count);
    }

    [Fact]
    public async Task HoldsAsync_AnInvariantSomeInputsBreak_ShrinksTheCounterexampleTheSameWay()
    {
        // Act
        var failure = await Assert.ThrowsAsync<CsCheckException>(
            () => PropertyCheck.HoldsAsync(
                Inputs,
                input =>
                {
                    Assert.True(input < Boundary);

                    return Task.CompletedTask;
                },
                Iterations));

        // Assert
        Assert.Equal(Boundary.ToString(CultureInfo.InvariantCulture), CounterexampleOf(failure));
    }

    [Fact]
    public void Holds_NoGeneratorOrNoInvariant_RefusesTheSampleRatherThanRunningNothing()
    {
        // Assert
        Assert.Throws<ArgumentNullException>(() => PropertyCheck.Holds(null!, (int _) => { }, Iterations));
        Assert.Throws<ArgumentNullException>(() => PropertyCheck.Holds(Inputs, null!, Iterations));
        Assert.Throws<ArgumentOutOfRangeException>(() => PropertyCheck.Holds(Inputs, _ => { }, iterations: 0));
    }

    /// <summary>
    /// The arguments are refused before a sample starts, so the failure arrives from the call rather than from a task
    /// nobody awaited. That is what the block bodies below assert: each one discards a task that is never produced.
    /// </summary>
    [Fact]
    public void HoldsAsync_NoGeneratorOrNoInvariant_RefusesTheSampleRatherThanRunningNothing()
    {
        // Assert
        Assert.Throws<ArgumentNullException>(
            () => { _ = PropertyCheck.HoldsAsync(null!, (int _) => Task.CompletedTask, Iterations); });
        Assert.Throws<ArgumentNullException>(
            () => { _ = PropertyCheck.HoldsAsync(Inputs, null!, Iterations); });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => { _ = PropertyCheck.HoldsAsync(Inputs, _ => Task.CompletedTask, iterations: 0); });
    }

    /// <summary>Reads the shrunk input out of a failure, which the generator writes under the seed it names.</summary>
    /// <remarks>
    /// The line after it is what the invariant itself raised, which is why the input is read by position rather than
    /// from the end: a failure reports the input and the reason separately, and both belong in the report.
    /// </remarks>
    private static string CounterexampleOf(CsCheckException failure)
    {
        var lines = failure.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.InRange(lines.Length, 2, int.MaxValue);

        return lines[1];
    }

    /// <summary>Reads the seed out of a failure, which the generator names in double quotes on the first line.</summary>
    private static string SeedOf(CsCheckException failure)
    {
        var named = failure.Message.Split('"');

        Assert.Equal(3, named.Length);

        return named[1];
    }
}
