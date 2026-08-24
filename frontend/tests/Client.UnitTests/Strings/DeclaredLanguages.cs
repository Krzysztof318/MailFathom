// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Xml.Linq;

namespace MailFathom.Client.UnitTests.Strings;

/// <summary>
/// What the shipped client declares it is readable in, read from the two places that declare it.
/// </summary>
/// <remarks>
/// A language exists for a reader only where both of them name it: the embedded configuration decides what
/// <c>ILocalizationService</c> offers, and a table under <c>Strings/</c> decides whether that offer has any words
/// behind it. Both are read here rather than in a test, so a test can hold one against the other instead of naming a
/// language itself — a check written against a literal would pass on the day a third language was added to only one
/// of them, which is the failure worth catching.
/// </remarks>
internal static class DeclaredLanguages
{
    private const string CulturesSection = "LocalizationConfiguration";

    /// <summary>The cultures the embedded configuration offers, in the order it names them.</summary>
    /// <returns>The IETF language tags.</returns>
    public static string[] Offered()
    {
        var assembly = typeof(App).Assembly;
        var name = Array.Find(
            assembly.GetManifestResourceNames(),
            resource => resource.EndsWith("appsettings.json", StringComparison.Ordinal));

        Assert.NotNull(name);

        using var settings = assembly.GetManifestResourceStream(name)!;

        // The file is written for a reader rather than for a parser, so it carries comments the JSON grammar does not.
        using var document = JsonDocument.Parse(
            settings,
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        return [.. document.RootElement
            .GetProperty(CulturesSection)
            .GetProperty("Cultures")
            .EnumerateArray()
            .Select(culture => culture.GetString()!)];
    }

    /// <summary>The cultures a string table is authored for, one directory each.</summary>
    /// <remarks>
    /// Read from the files the project links beside the test assembly rather than from a compiled resource map, which
    /// this host has none of. A build that copies rather than mirrors leaves a removed table behind in an incremental
    /// output, so it is a clean build that makes the absence of one visible.
    /// </remarks>
    /// <returns>The IETF language tags.</returns>
    public static string[] Tabled()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Strings");
        Assert.True(Directory.Exists(root), root);

        return
        [
            .. Directory.EnumerateDirectories(root)
                .Where(directory => File.Exists(Path.Combine(directory, "Resources.resw")))
                .Select(Path.GetFileName)
                .Select(culture => culture!)
        ];
    }

    /// <summary>The words one language's table holds, by the key a lookup resolves them through.</summary>
    /// <param name="culture">The IETF language tag naming the table.</param>
    /// <returns>Every authored entry.</returns>
    public static Dictionary<string, string> TableOf(string culture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Strings", culture, "Resources.resw");
        Assert.True(File.Exists(path), path);

        // Everything above the first authored entry is the ResX schema and the four resheaders, neither of which is a
        // `data` element — the sample entries the format's preamble shows are inside an XML comment.
        return XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(
                entry => entry.Attribute("name")!.Value,
                entry => entry.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }
}
