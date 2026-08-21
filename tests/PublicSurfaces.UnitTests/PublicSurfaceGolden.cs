// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using Xunit;

namespace MailFathom.PublicSurfaces.UnitTests;

/// <summary>Holds a rendered public surface against the file committed beside it, and rewrites that file on request.</summary>
/// <remarks>
/// <para>
/// The comparison is hand-rolled rather than taken from an approval-testing package, because what it has to do is read
/// two strings and report which lines moved. Both surfaces are rendered in a fixed order, so a line present in one
/// rendering and absent from the other is the whole of the difference between them and a set comparison describes it
/// exactly; a package would add a dependency, a licence entry, and a lock-file regeneration for that.
/// </para>
/// <para>
/// The file is read from the source tree rather than from the build output, because the point of it is the diff a pull
/// request shows. A run with <see cref="RegenerationVariable" /> set writes the rendering over it and passes, which is
/// how an intended change to a surface is taken into the record.
/// </para>
/// </remarks>
internal static class PublicSurfaceGolden
{
    /// <summary>The environment variable a run sets to rewrite the golden files instead of asserting against them.</summary>
    internal const string RegenerationVariable = "MAILFATHOM_UPDATE_PUBLIC_SURFACES";

    /// <summary>The command the failure message names, which is the one documented way to regenerate a golden file.</summary>
    private const string RegenerationCommand =
        $"{RegenerationVariable}=1 dotnet test --project tests/PublicSurfaces.UnitTests";

    /// <summary>How many differing lines a failure reports before it says how many it left out.</summary>
    /// <remarks>A surface reordered wholesale would otherwise put thousands of lines into a test result nobody can read to the end.</remarks>
    private const int ReportedLineLimit = 120;

    /// <summary>Asserts a rendered surface matches its golden file, or rewrites the file when the run asked for that.</summary>
    /// <param name="fileName">The golden file's name, which lives beside this suite's sources.</param>
    /// <param name="surfaceName">What the surface is called in the failure message.</param>
    /// <param name="rendered">The surface as this run rendered it.</param>
    public static void AssertMatches(string fileName, string surfaceName, string rendered)
    {
        var path = Path.Combine(GoldenDirectory, fileName);
        var normalized = Normalize(rendered);

        if (Environment.GetEnvironmentVariable(RegenerationVariable) is "1")
        {
            File.WriteAllText(path, normalized);

            return;
        }

        Assert.True(File.Exists(path), $"The golden file '{fileName}' is missing. Regenerate it with: {RegenerationCommand}");

        var committed = Normalize(File.ReadAllText(path));

        if (string.Equals(committed, normalized, StringComparison.Ordinal))
        {
            return;
        }

        Assert.Fail(DescribeDifference(fileName, surfaceName, committed, normalized));
    }

    /// <summary>Gets the directory holding the golden files, which is this project's own source directory.</summary>
    /// <remarks>
    /// Carried as assembly metadata the project file writes. <see cref="System.Runtime.CompilerServices.CallerFilePathAttribute" />
    /// would be the obvious way to reach it and is the wrong one: a deterministic build rewrites source paths to a
    /// root-relative form, so the value that arrives at run time under continuous integration names nothing on disk.
    /// </remarks>
    private static string GoldenDirectory => typeof(PublicSurfaceGolden).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(metadata => metadata.Key == "MailFathom.PublicSurfaceGoldenDirectory")
        .Value!;

    /// <summary>Reduces a rendering to the one form both sides are compared in.</summary>
    /// <remarks>
    /// Line endings are settled here rather than by whatever wrote the file, so a checkout that translated them and a
    /// writer that emitted the platform's own cannot make two identical surfaces compare unequal.
    /// </remarks>
    private static string Normalize(string text) =>
        text.ReplaceLineEndings("\n").TrimEnd('\n') + "\n";

    /// <summary>Describes what moved between the committed surface and the rendered one.</summary>
    private static string DescribeDifference(
        string fileName,
        string surfaceName,
        string committed,
        string rendered)
    {
        var committedLines = committed.Split('\n');
        var renderedLines = rendered.Split('\n');

        var removed = committedLines.Except(renderedLines, StringComparer.Ordinal).ToArray();
        var added = renderedLines.Except(committedLines, StringComparer.Ordinal).ToArray();

        var report = new List<string>
        {
            $"The {surfaceName} no longer matches {fileName}.",
            string.Empty,
            "This is a change to a public surface. Name it in the pull request against the surface it breaks and with",
            "the operator's action, because the release pull request composes CHANGELOG.md from that reading alone.",
            string.Empty,
            $"Take the change into the record with: {RegenerationCommand}",
            string.Empty,
            $"{removed.Length} line(s) gone, {added.Length} line(s) new:",
            string.Empty,
        };

        report.AddRange(Report(removed, '-'));
        report.AddRange(Report(added, '+'));

        if (removed.Length == 0 && added.Length == 0)
        {
            // Every line survives, so what moved is the order they are written in — which is itself part of the record,
            // because both renderings are ordered and a reordering means the ordering rule changed.
            report.Add("No line was added or removed, so the surface was reordered.");
        }

        return string.Join('\n', report);
    }

    /// <summary>Prefixes the differing lines and says how many were left out.</summary>
    private static IEnumerable<string> Report(string[] lines, char marker)
    {
        foreach (var line in lines.Take(ReportedLineLimit))
        {
            yield return $"{marker} {line}";
        }

        if (lines.Length > ReportedLineLimit)
        {
            yield return $"{marker} … and {lines.Length - ReportedLineLimit} more";
        }
    }
}
