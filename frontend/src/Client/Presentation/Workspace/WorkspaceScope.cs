// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;

namespace MailFathom.Client.Presentation.Workspace;

/// <summary>
/// What the next question would be asked against: an account, a folder within it or a role across all of them, and
/// whatever is selected there, down to a passage somebody selected in one message.
/// </summary>
/// <remarks>
/// <para>
/// It belongs to the frame rather than to any space, because it is the thing that makes Discover, Mail, and Cases one
/// application: somebody who narrows to a folder in one space and moves to another has not changed what they are
/// asking about, and a scope each space kept for itself would say they had.
/// </para>
/// <para>
/// A body selection is mail content and every other member is sensitive mail metadata. They live only in this run's
/// state, are never persisted, logged, or recorded in telemetry, and leave the process only when a later act explicitly
/// asks something against this scope.
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

    /// <summary>The special-use role in scope across every account that has one, or <see langword="null" /> when no role is.</summary>
    /// <remarks>
    /// The one narrowing that is not a place in one mailbox. Somebody with a work account and two personal ones wants
    /// <em>sent</em> to mean sent from any of them as often as they want one account's own sent folder, and a scope
    /// that could only name an account and a folder within it would turn that into three acts. It is the role's own
    /// published name rather than a folder alias, because which folder plays the role differs per account and per
    /// provider — that is the whole reason the deployment reports a role at all.
    /// </remarks>
    public string? Role { get; init; }

    /// <summary>What is selected within the scope above, in the order it was selected.</summary>
    public IImmutableList<string> Selection { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>The passage selected inside the one message in scope, or empty where none is selected.</summary>
    public string BodySelection { get; init; } = string.Empty;

    /// <summary>Whether this scope narrows to anything at all.</summary>
    /// <remarks>
    /// Each of the four is asked about rather than only the account, because nothing here refuses a folder named
    /// without one: the properties above say what a well-formed scope looks like, and a reader that assumed the type
    /// enforced it would answer <see langword="false" /> for a scope that plainly narrows.
    /// </remarks>
    public bool NarrowsAnything =>
        this.Account is not null
        || this.Folder is not null
        || this.Role is not null
        || this.Selection.Count > 0
        || this.BodySelection.Length > 0;

    /// <summary>Whether this scope and another name the same place, whatever either has selected inside it.</summary>
    /// <param name="other">The scope to compare against.</param>
    /// <returns><see langword="true" /> when both name the same account, folder, and role.</returns>
    /// <remarks>
    /// What the tree marks a row selected by. Equality below is the whole value and is what the state compares to
    /// decide whether anything happened; this is the weaker question a row asks — whether the place it stands for is
    /// the place in force — so opening a message inside a folder does not stop the folder reading as the one somebody
    /// is in.
    /// </remarks>
    public bool NamesSamePlaceAs(WorkspaceScope? other) =>
        other is not null
        && string.Equals(this.Account, other.Account, StringComparison.Ordinal)
        && string.Equals(this.Folder, other.Folder, StringComparison.Ordinal)
        && string.Equals(this.Role, other.Role, StringComparison.Ordinal);

    /// <summary>Holds two scopes equal when they name the same thing, rather than when they are the same object.</summary>
    /// <param name="other">The scope to compare against.</param>
    /// <returns><see langword="true" /> when both name the same account, folder, role, and selection.</returns>
    /// <remarks>
    /// A record compares its members with the default comparer, and for <see cref="IImmutableList{T}" /> that is
    /// reference equality — so two scopes built from the same selection would be unequal and every write of an
    /// unchanged scope would reach the view as a change. The state this record travels in compares values to decide
    /// whether anything happened, which is why the comparison is stated rather than inherited.
    /// </remarks>
    public bool Equals(WorkspaceScope? other) =>
        this.NamesSamePlaceAs(other)
        && this.Selection.SequenceEqual(other!.Selection, StringComparer.Ordinal)
        && string.Equals(this.BodySelection, other.BodySelection, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(this.Account, this.Folder, this.Role, this.Selection.Count, this.BodySelection);
}
