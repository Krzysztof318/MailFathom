// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Common;

/// <summary>Where the documentation for a version MailFathom reports is published.</summary>
/// <remarks>
/// <para>
/// The documentation site holds one directory per version it carries, so where a reader's own pages are is a function
/// of the version in front of them and of nothing else. That is what lets a surface which already knows its version
/// hand the address over without being told it: the container image labels it, the chart's notes print it, the
/// administrative command reports the deployment's, and an MCP session carries it in the handshake. Nothing here is
/// configurable for the same reason — an address an operator could edit could point a reader at pages describing a
/// version other than the one they are running, which is the single thing this exists to prevent.
/// </para>
/// <para>
/// A prerelease resolves to the default branch's directory rather than to one of its own. A nightly is named after the
/// release it will become, which the site does not publish until that release exists, and what a nightly actually
/// carries is whatever <c>main</c> was the night it was built — which is what <c>latest</c> documents. The same
/// reading covers a build nobody released.
/// </para>
/// <para>
/// The address is composed from the numbers a version parsed to rather than from the text it arrived as, because one
/// caller reads that text off a deployment across the network and prints the result. A version this cannot read yields
/// no address at all, and every caller says nothing rather than offering one that goes somewhere.
/// </para>
/// </remarks>
public static class DocumentationAddress
{
    /// <summary>Where the site is served, with the version directories directly beneath it.</summary>
    private const string Site = "https://krzysztof318.github.io/MailFathom/";

    /// <summary>The directory the site publishes the default branch's documentation under.</summary>
    private const string DefaultBranchDirectory = "latest";

    /// <summary>Reports where the documentation for a version is published.</summary>
    /// <param name="version">The semantic version a build reports, which is <see langword="null" /> where it reported none.</param>
    /// <returns>The address of that version's documentation directory, or <see langword="null" /> where the version is not one this can read.</returns>
    /// <remarks>
    /// Build metadata is cut away before anything else, so a version carrying the source revision the SDK stamps after
    /// SemVer's plus sign reads as the version it is a build of.
    /// </remarks>
    public static string? ForVersion(string? version)
    {
        var reported = version?.Trim() ?? string.Empty;

        if (reported.Length == 0)
        {
            return null;
        }

        var buildMetadataStart = reported.IndexOf('+', StringComparison.Ordinal);
        var core = buildMetadataStart < 0 ? reported.AsSpan() : reported.AsSpan(0, buildMetadataStart);

        var prereleaseStart = core.IndexOf('-');

        if (prereleaseStart >= 0)
        {
            return ReleaseNumberOf(core[..prereleaseStart]) is null ? null : Site + DefaultBranchDirectory + "/";
        }

        return ReleaseNumberOf(core) is { } released
            ? $"{Site}v{released.Major}.{released.Minor}.{released.Build}/"
            : null;
    }

    /// <summary>Reads the release number a version names, which is the whole of what an address is composed from.</summary>
    /// <remarks>
    /// Three components exactly, because that is what a release tag carries and what the site names a directory after.
    /// A value naming fewer or more is a version this cannot place on the site rather than one to guess a directory
    /// for.
    /// </remarks>
    private static Version? ReleaseNumberOf(ReadOnlySpan<char> core) =>
        Version.TryParse(core, out var parsed) && parsed is { Build: >= 0, Revision: < 0 } ? parsed : null;
}
