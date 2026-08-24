// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Xml.Linq;

namespace MailFathom.Client.UnitTests.Strings;

/// <summary>
/// What the client's views ask the string tables for, read from the views themselves.
/// </summary>
/// <remarks>
/// A <c>x:Uid</c> is one end of a name whose other end is a table entry, and nothing holds the two together: the
/// application compiles the views into a resource map a running head resolves against, and a name nothing answers
/// reaches somebody as a control with no words on it rather than as a failure. So the views are read here the way the
/// tables are, from the files the project links beside the test assembly.
/// </remarks>
internal static class AuthoredViews
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Every <c>x:Uid</c> the client's views name, without duplicates.</summary>
    /// <returns>The uid values, in no particular order.</returns>
    public static string[] NamedUids()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Xaml");
        Assert.True(Directory.Exists(root), root);

        var pages = Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories).ToArray();
        Assert.NotEmpty(pages);

        return
        [
            .. pages
                .SelectMany(page => XDocument.Load(page).Descendants())
                .Select(element => element.Attribute(Xaml + "Uid")?.Value)
                .Where(uid => !string.IsNullOrEmpty(uid))
                .Select(uid => uid!)
                .Distinct(StringComparer.Ordinal)
        ];
    }
}
