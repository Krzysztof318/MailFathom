// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Folders;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class LocalMailFolderEntity
{
    public Guid Id { get; set; }

    public required string MailboxAccountId { get; set; }

    public Guid? ParentId { get; set; }

    public required string Name { get; set; }

    /// <summary>Gets or sets the form sibling names are compared in, which the uniqueness among live siblings is written over.</summary>
    public required string NameKey { get; set; }

    public MailFolderSpecialUse? Role { get; set; }

    public string? SourceFolderAlias { get; set; }

    /// <summary>Gets or sets when the folder was erased, or <see langword="null" /> while it is live.</summary>
    /// <remarks>
    /// An erased row stays while its mail is erased in passes, so a message is never left pointing at nothing. Once no
    /// mail remains, a row carrying no source alias goes; one carrying a source alias stays, renamed to that alias,
    /// because it is what says arrivals from that source go to the inbox rather than recreating the folder.
    /// </remarks>
    public DateTimeOffset? ErasedAt { get; set; }
}
