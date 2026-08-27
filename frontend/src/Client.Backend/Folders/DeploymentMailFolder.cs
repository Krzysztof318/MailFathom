// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Folders;

/// <summary>One folder of one account, as a screen drawing a tree needs it.</summary>
/// <param name="Alias">MailFathom's own name for the folder, which is what every other route on this surface names it by.</param>
/// <param name="Role">The role the folder plays for its account as the deployment named it, or <see langword="null" /> where configuration labelled it with none.</param>
/// <param name="Path">The folder's place on its mail server, outermost level first, and empty where nothing has bound the alias to a remote folder yet.</param>
/// <param name="StoredEmailCount">How many of the folder's emails this deployment holds and would serve.</param>
/// <param name="UnreadEmailCount">How many of those the mail server last reported without <c>\Seen</c>.</param>
/// <param name="SynchronizationState">The standing as the deployment names it, kept as the word that arrived so an unknown one is readable rather than lost.</param>
/// <param name="LastSynchronizedAt">When the folder last durably took anything in, or <see langword="null" /> where it never has.</param>
/// <param name="Behind">Whether the folder's last attempt ended with mail it had not yet taken in.</param>
/// <remarks>
/// <para>
/// The path is levels rather than one string, so a tree is built without knowing that mail servers have a hierarchy
/// delimiter or which character this one chose. The last level is what a person recognizes as the folder's name; the
/// alias above it is MailFathom's own name and is what a later request names the folder with.
/// </para>
/// <para>
/// The counts are of the local copy and are meaningless without the three freshness fields under them, which is why
/// all six travel together. A folder still being backfilled holds fewer than the mail server does, and one whose last
/// attempt failed holds what it held before that attempt.
/// </para>
/// <para>
/// Every remote folder name here is this owner's own and is put in front of that owner alone. It carries the same
/// classification the rest of this client's mail data does: it reaches no log, no telemetry, and no local store.
/// </para>
/// </remarks>
public sealed record DeploymentMailFolder(
    string Alias,
    string? Role,
    IReadOnlyList<string> Path,
    int StoredEmailCount,
    int UnreadEmailCount,
    string SynchronizationState,
    DateTimeOffset? LastSynchronizedAt,
    bool Behind)
{
    /// <summary>Gets where this folder's copy stands, as this client reads the word the deployment sent.</summary>
    public MailSynchronizationStanding Standing => MailSynchronizationStandings.Read(this.SynchronizationState);

    /// <summary>Gets the part this folder plays for its account, as this client reads the role the deployment sent.</summary>
    /// <remarks>
    /// A document naming no role reads as <see cref="MailFolderRole.None" /> and one naming a role this build does not
    /// know reads as <see cref="MailFolderRole.Unrecognized" />, because the two lead a tree to different places: an
    /// ordinary folder belongs where its path puts it, and a folder whose role cannot be interpreted is one this client
    /// must not claim to have placed.
    /// </remarks>
    public MailFolderRole SpecialUse => this.Role switch
    {
        null => MailFolderRole.None,
        "Inbox" => MailFolderRole.Inbox,
        "Drafts" => MailFolderRole.Drafts,
        "Sent" => MailFolderRole.Sent,
        "Outbox" => MailFolderRole.Outbox,
        "Archive" => MailFolderRole.Archive,
        "Junk" => MailFolderRole.Junk,
        "Trash" => MailFolderRole.Trash,
        "All" => MailFolderRole.All,
        "Flagged" => MailFolderRole.Flagged,
        "Important" => MailFolderRole.Important,
        _ => MailFolderRole.Unrecognized,
    };

    /// <summary>Gets the folder's place on its mail server, reading a document that named none as a folder nothing has bound yet.</summary>
    public IReadOnlyList<string> HierarchyLevels => this.Path ?? [];
}
