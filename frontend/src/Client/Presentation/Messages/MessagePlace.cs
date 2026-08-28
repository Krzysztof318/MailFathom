// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Folders;
using MailFathom.Client.Presentation.Workspace;

namespace MailFathom.Client.Presentation.Messages;

/// <summary>Where a message list is drawn from: the account, the folder, or the role the workspace is narrowed to.</summary>
/// <param name="Account">The account in scope, or <see langword="null" /> when every one of them is.</param>
/// <param name="Folder">The folder alias in scope, or <see langword="null" /> when the whole account is.</param>
/// <param name="Role">The special-use role in scope across accounts, or <see langword="null" /> when no role is.</param>
/// <remarks>
/// <para>
/// The scope without what is selected inside it, which is the whole reason it exists. The list writes the selection
/// back into <see cref="IWorkspace.Scope" />, so a list keyed on the scope itself would reload every time somebody
/// clicked a row in it. Keyed on this instead, it reloads exactly when somebody goes somewhere else.
/// </para>
/// <para>
/// It is also what the remembered position is filed under, so returning to a folder returns to where the list was
/// rather than to the top of it.
/// </para>
/// </remarks>
public sealed record MessagePlace(string? Account, string? Folder, string? Role)
{
    /// <summary>Everything the signed-in person can reach, which is where a run that has narrowed nothing draws from.</summary>
    public static MessagePlace Everything { get; } = new(Account: null, Folder: null, Role: null);

    /// <summary>What separates the three parts of <see cref="RememberedAs" />.</summary>
    /// <remarks>
    /// A unit separator, because the two names on either side of it are a mail server's own and this application does
    /// not get to constrain what characters they hold. It is the same character the tree composes a row key with.
    /// </remarks>
    private const char PartSeparator = '\u001F';

    /// <summary>Reads the place out of a workspace scope, leaving whatever is selected inside it behind.</summary>
    /// <param name="scope">The scope in force.</param>
    /// <returns>The place the list is drawn from.</returns>
    public static MessagePlace Of(WorkspaceScope? scope) =>
        scope is null ? Everything : new MessagePlace(scope.Account, scope.Folder, scope.Role);

    /// <summary>Gets what this place is remembered by, which is the three names as one value.</summary>
    /// <remarks>Named for what it is for rather than as a key, because a record here carrying one would be generated an identity MVUX matches list items by — and a place is not a row.</remarks>
    public string RememberedAs => $"{this.Account}{PartSeparator}{this.Folder}{PartSeparator}{this.Role}";

    /// <summary>Gets whether this place is one somebody chose rather than a whole mailbox or every mailbox at once.</summary>
    /// <remarks>
    /// What decides whether junk mail takes part without being asked for. A folder or a role somebody selected is a
    /// place they went to, and leaving its mail out would draw the junk folder as an empty folder; an account and the
    /// unified list are places junk is kept out of until a filter says otherwise, which is how every other read on this
    /// surface behaves.
    /// </remarks>
    public bool IsChosenFolder => this.Folder is not null || this.Role is not null;

    /// <summary>Gets whether a row here names who the message went to rather than who it came from.</summary>
    /// <remarks>
    /// Mail somebody sent is drawn by its recipients, because every row of it came from the same person and a column of
    /// one's own name says nothing. It is answered from the role rather than from the message, because a message does
    /// not know which of its addresses the reader is — and it is answered only where the workspace named a role, since
    /// a folder chosen by its alias reaches this client without one. A sent folder opened by its alias therefore draws
    /// its senders until the scope carries the role of the folder it names.
    /// </remarks>
    public bool ShowsRecipients =>
        this.Role is not null
        && (this.NamesRole(MailFolderRole.Sent)
            || this.NamesRole(MailFolderRole.Drafts)
            || this.NamesRole(MailFolderRole.Outbox));

    private bool NamesRole(MailFolderRole role) =>
        string.Equals(this.Role, role.ToString(), StringComparison.Ordinal);
}
