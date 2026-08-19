// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>A written shorthand for every published permission a wildcard pattern reaches within the name space.</summary>
/// <remarks>
/// <para>
/// It is what an operator writes in a grant as <c>mailfathom.admin.*</c> rather than as the six names that prefix
/// reaches, and as <c>mailfathom.*.read</c> rather than as the reading names that sit at two different depths. The
/// syntax is one <c>*</c> occupying a whole dot-separated segment, standing for one or more consecutive segments, at
/// any position and more than once; a trailing wildcard is that rule applied to the last segment rather than a form of
/// its own. There is no partial segment, so <c>mailfathom.mail.c*</c> names nothing and is left to be refused as the
/// unpublished name it is.
/// </para>
/// <para>
/// A subtree resolves against <see cref="MailFathomPermission.All" /> whenever it is asked rather than being frozen
/// when it is parsed, which is what makes it shorthand for the surface rather than for the names published the day the
/// configuration file was written: a permission added where a written pattern reaches in a later release reaches a
/// grant that already names it.
/// </para>
/// <para>
/// What it reaches is the whole published set rather than one protected surface's half, because a pattern with a
/// wildcard before its last segment can name permissions of both — <c>mailfathom.*.read</c> is the worked example. The
/// half an entry may actually grant is the entry's own question and is answered where a grant is validated, which is
/// also where a pattern reaching only the other surface, or reaching everything, is refused.
/// </para>
/// <para>
/// It is only ever read from a deployment's own configuration. A token never carries one — a scope is compared byte
/// for byte at the authorization server, so nothing could mint a pattern — and neither does a published metadata
/// document, which states the resolved names instead.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and names no subtree. It reports itself through
/// <see cref="IsSpecified" /> and refuses to answer for anything else, so a value that never came from
/// <see cref="TryParse" /> cannot be read as one covering everything.
/// </para>
/// </remarks>
public readonly record struct PermissionSubtree
{
    /// <summary>What a written pattern and a published name are both divided into, matching being whole segments.</summary>
    private const char SegmentSeparator = '.';

    /// <summary>The segment standing for one or more segments of a published name.</summary>
    private const string Wildcard = "*";

    private readonly string? written;

    private PermissionSubtree(string written) => this.written = written;

    /// <summary>Gets whether this value names a subtree rather than the unusable struct default.</summary>
    public bool IsSpecified => this.written is not null;

    /// <summary>Gets the value exactly as it was written, which is what a refusal quotes back.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a subtree.</exception>
    public string Written => this.written
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a subtree.");

    /// <summary>Parses a written value that asks for a subtree rather than for one permission.</summary>
    /// <param name="written">The written value.</param>
    /// <param name="subtree">The parsed subtree when the value is written as one; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the value is written as a subtree; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Answering <see langword="true" /> says the operator asked for a subtree and never that the subtree is one this
    /// deployment can grant. A pattern nothing publishes beneath, a pattern belonging to the other protected surface,
    /// and the whole name space each parse here and are refused where a grant is validated, because each needs a
    /// different thing said about it than "that is not a permission".
    /// </remarks>
    public static bool TryParse(string? written, out PermissionSubtree subtree)
    {
        subtree = written is not null && IsWrittenAsAPattern(written)
            ? new PermissionSubtree(written)
            : default;

        return subtree.IsSpecified;
    }

    /// <summary>Reports every published permission this subtree reaches, reading the set as it stands now.</summary>
    /// <returns>The covered permissions, in the published order, empty when the pattern reaches nothing this repository publishes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a subtree.</exception>
    public IReadOnlyList<MailFathomPermission> CoveredPermissions()
    {
        var pattern = (this.written
            ?? throw new InvalidOperationException("The value is the default of the struct and covers no permission."))
            .Split(SegmentSeparator);

        return
        [
            .. MailFathomPermission.All.Where(permission => Covers(pattern, permission.Name.Split(SegmentSeparator))),
        ];
    }

    /// <summary>Reports whether this subtree reaches the whole vocabulary rather than one part of it.</summary>
    /// <returns><see langword="true" /> when every published permission is covered; otherwise <see langword="false" />.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a subtree.</exception>
    /// <remarks>
    /// Such a value spans both protected surfaces, so it can never be shorthand for a subtree of the one being
    /// configured, and what it would resolve to on either is exactly what an entry writing no grant at all already
    /// says. It is read rather than declared, so it stays true of whatever the published set becomes.
    /// </remarks>
    public bool ReachesEveryPublishedPermission() => this.CoveredPermissions().Count == MailFathomPermission.All.Count;

    /// <inheritdoc />
    public override string ToString() => this.written ?? "(unspecified)";

    /// <summary>Reports whether a written value is a pattern at all, which is a question about its shape alone.</summary>
    /// <remarks>
    /// A wildcard is a whole segment or it is not a wildcard: a value writing one inside a segment reaches nothing and
    /// has to fall through to the refusal an unpublished name draws, or an operator who wrote <c>mailfathom.mail.c*</c>
    /// would be told their pattern matched nothing rather than that they had not written one. An empty segment is
    /// refused for the same reason, since <c>.*</c> and <c>mailfathom..read</c> are neither a pattern nor a name.
    /// </remarks>
    private static bool IsWrittenAsAPattern(string written)
    {
        var carriesAWildcard = false;

        foreach (var segment in written.Split(SegmentSeparator))
        {
            if (string.Equals(segment, Wildcard, StringComparison.Ordinal))
            {
                carriesAWildcard = true;

                continue;
            }

            if (segment.Length == 0 || segment.Contains(Wildcard, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return carriesAWildcard;
    }

    /// <summary>Reports whether a pattern's segments reach a published name's segments.</summary>
    /// <remarks>
    /// A wildcard stands for one or more segments rather than for none, which is what keeps <c>mailfathom.mail.*</c>
    /// reaching what sits beneath the prefix and never the prefix itself. Where one is met, every split from one
    /// segment onwards is tried, because a later literal segment decides how many the wildcard may take: both names a
    /// <c>mailfathom.*.read</c> reaches are found that way and neither by taking as much as possible.
    /// </remarks>
    private static bool Covers(ReadOnlySpan<string> pattern, ReadOnlySpan<string> name)
    {
        while (!pattern.IsEmpty && !string.Equals(pattern[0], Wildcard, StringComparison.Ordinal))
        {
            if (name.IsEmpty || !string.Equals(pattern[0], name[0], StringComparison.Ordinal))
            {
                return false;
            }

            pattern = pattern[1..];
            name = name[1..];
        }

        if (pattern.IsEmpty)
        {
            return name.IsEmpty;
        }

        for (var consumed = 1; consumed <= name.Length; consumed++)
        {
            if (Covers(pattern[1..], name[consumed..]))
            {
                return true;
            }
        }

        return false;
    }
}
