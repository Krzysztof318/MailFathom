// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using Xunit;

namespace MailFathom.IntegrationTests.ObjectStorage;

/// <summary>Holds <see cref="S3Surface" /> to what it claims: a list of dependences, each one proved.</summary>
/// <remarks>
/// <para>
/// The list is a compatibility statement somebody will read instead of the adapter, which is exactly why it must not be
/// allowed to become prose that used to be true. Two failures are possible and neither is one a compiler can catch: an
/// entry naming a method that is no longer a test the runner runs, and a test written here that nobody added to the
/// list.
/// </para>
/// <para>
/// Nothing here starts a container or reaches the endpoint. It runs in this project because the types it reflects over
/// live in this assembly and nowhere else.
/// </para>
/// </remarks>
public sealed class S3SurfaceCoverageTests
{
    /// <summary>Every dependence names a method the runner will actually run, so no entry can rest on a test that stopped being one.</summary>
    [Fact]
    public void Dependencies_EachNameATestMethodTheRunnerWillRun()
    {
        Assert.NotEmpty(S3Surface.Dependencies);

        foreach (var dependency in S3Surface.Dependencies)
        {
            var method = dependency.ExercisedBy.GetMethod(
                dependency.TestMethod,
                BindingFlags.Public | BindingFlags.Instance);

            Assert.True(
                method is not null,
                $"'{dependency.Operation}' names {dependency.ExercisedBy.Name}.{dependency.TestMethod}, which does not exist.");
            Assert.True(
                method!.GetCustomAttribute<FactAttribute>() is not null,
                $"'{dependency.Operation}' names {dependency.ExercisedBy.Name}.{dependency.TestMethod}, which is not a test.");
        }
    }

    /// <summary>
    /// Every test in the surface class is named by a dependence, which is the direction that keeps the list complete
    /// rather than merely accurate: a behaviour somebody proved and did not write down is one a reader judging another
    /// S3 implementation would never learn MailFathom relies on.
    /// </summary>
    [Fact]
    public void Dependencies_NameEveryTestTheSurfaceClassHolds()
    {
        var exercised = S3Surface.Dependencies
            .Select(dependency => (dependency.ExercisedBy, dependency.TestMethod))
            .ToHashSet();

        var tests = typeof(OrchestratedS3SurfaceTests)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<FactAttribute>() is not null);

        foreach (var test in tests)
        {
            Assert.True(
                exercised.Contains((typeof(OrchestratedS3SurfaceTests), test.Name)),
                $"{nameof(OrchestratedS3SurfaceTests)}.{test.Name} proves something no entry in {nameof(S3Surface)} names.");
        }
    }

    /// <summary>An operation is named once, so the list reads as a set of dependences rather than as a log of what was tested when.</summary>
    [Fact]
    public void Dependencies_NameEachOperationOnce()
    {
        var operations = S3Surface.Dependencies.Select(static dependency => dependency.Operation).ToList();

        Assert.Equal(operations.Count, operations.Distinct(StringComparer.Ordinal).Count());
    }
}
