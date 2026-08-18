// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Access;

/// <summary>A written shorthand for every published permission beneath one prefix of the name space.</summary>
/// <remarks>
/// <para>
/// It is what an operator writes in a grant as <c>mailfathom.admin.*</c> rather than as the six names that prefix
/// reaches. The trailing <c>.*</c> is the whole of the syntax: there is no infix matching and no partial segment, so
/// <c>mailfathom.mail.c*</c> names nothing and is left to be refused as the unpublished name it is.
/// </para>
/// <para>
/// A subtree resolves against <see cref="MailFathomPermission.All" /> whenever it is asked rather than being frozen
/// when it is parsed, which is what makes it shorthand for the surface rather than for the names published the day the
/// configuration file was written: a permission added beneath a covered prefix in a later release reaches a grant that
/// already names the subtree.
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
    /// <summary>The value naming every published permission at once, which carries no prefix to sit after.</summary>
    private const string WholeNameSpace = "*";

    /// <summary>What a written subtree ends in, the dot being what keeps the match on whole segments.</summary>
    private const string WildcardSuffix = ".*";

    private readonly string? written;

    private readonly string? coveredNamePrefix;

    private PermissionSubtree(string written, string coveredNamePrefix)
    {
        this.written = written;
        this.coveredNamePrefix = coveredNamePrefix;
    }

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
    /// deployment can grant. A prefix nothing publishes, a prefix belonging to the other protected surface, and the
    /// whole name space each parse here and are refused where a grant is validated, because each needs a different
    /// thing said about it than "that is not a permission".
    /// </remarks>
    public static bool TryParse(string? written, out PermissionSubtree subtree)
    {
        subtree = written switch
        {
            null => default,
            WholeNameSpace => new PermissionSubtree(written, string.Empty),

            // The suffix is dropped rather than the whole wildcard, so the prefix keeps its trailing dot and matching
            // it cannot reach halfway into a segment.
            _ when written.Length > WildcardSuffix.Length && written.EndsWith(WildcardSuffix, StringComparison.Ordinal)
                => new PermissionSubtree(written, written[..^1]),
            _ => default,
        };

        return subtree.IsSpecified;
    }

    /// <summary>Reports every published permission this subtree reaches, reading the set as it stands now.</summary>
    /// <returns>The covered permissions, in the published order, empty when the prefix names nothing this repository publishes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a subtree.</exception>
    public IReadOnlyList<MailFathomPermission> CoveredPermissions()
    {
        var prefix = this.coveredNamePrefix
            ?? throw new InvalidOperationException("The value is the default of the struct and covers no permission.");

        return [.. MailFathomPermission.All.Where(permission => permission.Name.StartsWith(prefix, StringComparison.Ordinal))];
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
}
