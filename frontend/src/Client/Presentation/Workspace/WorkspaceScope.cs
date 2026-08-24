// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;

namespace MailFathom.Client.Presentation.Workspace;

/// <summary>
/// What the next question would be asked against: an account, a folder within it, and whatever is selected there.
/// </summary>
/// <remarks>
/// <para>
/// It belongs to the frame rather than to any space, because it is the thing that makes Discover, Mail, and Cases one
/// application: somebody who narrows to a folder in one space and moves to another has not changed what they are
/// asking about, and a scope each space kept for itself would say they had.
/// </para>
/// <para>
/// Nothing here identifies a person or carries mail content. An account and a folder are names the deployment already
/// told this client, and a selection is a list of the identifiers it handed over with them — the classification the
/// root instructions put on mail metadata follows all three, so none of them is logged or written anywhere but memory.
/// </para>
/// </remarks>
public sealed record WorkspaceScope
{
    /// <summary>Everything the signed-in person can reach, which is what a run starts scoped to.</summary>
    public static WorkspaceScope Everything { get; } = new();

    /// <summary>The account in scope, or <see langword="null" /> when every one of them is.</summary>
    public string? Account { get; init; }

    /// <summary>The folder in scope within <see cref="Account" />, or <see langword="null" /> when the whole account is.</summary>
    public string? Folder { get; init; }

    /// <summary>What is selected within the scope above, in the order it was selected.</summary>
    public IImmutableList<string> Selection { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Whether this scope narrows to anything at all.</summary>
    public bool NarrowsAnything => this.Account is not null || this.Selection.Count > 0;

    /// <summary>Holds two scopes equal when they name the same thing, rather than when they are the same object.</summary>
    /// <param name="other">The scope to compare against.</param>
    /// <returns><see langword="true" /> when both name the same account, folder, and selection.</returns>
    /// <remarks>
    /// A record compares its members with the default comparer, and for <see cref="IImmutableList{T}" /> that is
    /// reference equality — so two scopes built from the same selection would be unequal and every write of an
    /// unchanged scope would reach the view as a change. The state this record travels in compares values to decide
    /// whether anything happened, which is why the comparison is stated rather than inherited.
    /// </remarks>
    public bool Equals(WorkspaceScope? other) =>
        other is not null
        && string.Equals(this.Account, other.Account, StringComparison.Ordinal)
        && string.Equals(this.Folder, other.Folder, StringComparison.Ordinal)
        && this.Selection.SequenceEqual(other.Selection, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(this.Account, this.Folder, this.Selection.Count);
}
