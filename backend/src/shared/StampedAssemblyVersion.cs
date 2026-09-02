// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;

namespace MailFathom.Versioning;

/// <summary>The version an assembly was stamped with at build time, split into the parts a reader asks for separately.</summary>
/// <param name="Version">The semantic version, carrying a prerelease identifier when the build had one and no build metadata.</param>
/// <param name="Revision">The short source revision the assembly was built from, or <c>unknown</c> when the build supplied none.</param>
/// <remarks>
/// <para>
/// The single string the build stamps answers two different questions, and reporting it whole answers neither well.
/// <see cref="Version" /> is the compatibility statement, which is what an operator groups deployments by and what an
/// MCP client compares against a tool contract it knows; <see cref="Revision" /> is build provenance, which is what
/// turns a bug report from a deployment the reader did not build into something reproducible, and which is reported in
/// the abbreviated form a nightly identifier and a Git log already use rather than as the whole object name. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md">ADR 0004</see> for where the number comes
/// from.
/// </para>
/// <para>
/// A build inside a Git worktree stamps the revision on its own, because the SDK's source-link support resolves
/// <c>SourceRevisionId</c> from the checkout. A build with no repository beside it — the container build context is the
/// one that occurs here — carries whatever its caller supplied and nothing otherwise, which is a legitimate state
/// rather than a failure. Both parts therefore fall back to <c>unknown</c> rather than to <see langword="null" />:
/// every consumer reports them, and none of them acts on the difference.
/// </para>
/// <para>
/// The type is compiled into each assembly that reports its own version from <c>backend/src/shared/</c>, because each one is
/// stamped separately and has to read its own metadata rather than the entry assembly's.
/// </para>
/// </remarks>
internal sealed record StampedAssemblyVersion(string Version, string Revision)
{
    /// <summary>What either part reads as when the build stamped nothing a reader can use.</summary>
    public const string Unknown = "unknown";

    /// <summary>How many characters of an object name the revision is reported as.</summary>
    /// <remarks>
    /// Seven, because that is what the nightly identifier abbreviates to and what a reader pasting the value into
    /// <c>git show</c> already expects. The whole object name stays on the image's
    /// <c>org.opencontainers.image.revision</c> label and in the assembly's own informational version, so nothing that
    /// needs it has lost it.
    /// </remarks>
    private const int ShortRevisionLength = 7;

    /// <summary>Reads the version an assembly was stamped with.</summary>
    /// <param name="assembly">The assembly whose build-time metadata is read.</param>
    /// <returns>The stamped version, with <see cref="Unknown" /> standing in for whichever part the build omitted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly" /> is <see langword="null" />.</exception>
    public static StampedAssemblyVersion ReadFrom(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return Parse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }

    /// <summary>Splits a stamped informational version into its semantic version and its source revision.</summary>
    /// <param name="informationalVersion">The value the build stamped, which is <see langword="null" /> when it stamped none.</param>
    /// <returns>The two parts, with <see cref="Unknown" /> standing in for whichever one is absent or blank.</returns>
    /// <remarks>
    /// The separator is SemVer's build-metadata plus sign, which is what the SDK appends <c>SourceRevisionId</c> after.
    /// A prerelease identifier stays with the version, where it belongs: <c>0.2.0-nightly.41</c> is a version, and the
    /// commit it was built from is the part after the plus sign.
    /// </remarks>
    internal static StampedAssemblyVersion Parse(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return new StampedAssemblyVersion(Unknown, Unknown);
        }

        var buildMetadataStart = informationalVersion.IndexOf('+', StringComparison.Ordinal);

        if (buildMetadataStart < 0)
        {
            return new StampedAssemblyVersion(informationalVersion.Trim(), Unknown);
        }

        return new StampedAssemblyVersion(
            NonBlankOrUnknown(informationalVersion.AsSpan(0, buildMetadataStart)),
            Abbreviate(NonBlankOrUnknown(informationalVersion.AsSpan(buildMetadataStart + 1))));
    }

    private static string NonBlankOrUnknown(ReadOnlySpan<char> candidate) =>
        candidate.IsWhiteSpace() ? Unknown : candidate.Trim().ToString();

    /// <summary>Shortens an object name to the length a reader expects, leaving anything that is not one alone.</summary>
    /// <remarks>
    /// The test is hexadecimal rather than a length, so a Git object name is abbreviated whichever hash function
    /// produced it while <see cref="Unknown" />, and any other value a build supplied, is reported whole. Truncating
    /// something that is not an object name would produce a value nothing can be looked up by.
    /// </remarks>
    private static string Abbreviate(string revision) =>
        revision.Length > ShortRevisionLength && revision.All(char.IsAsciiHexDigit)
            ? revision[..ShortRevisionLength]
            : revision;
}
