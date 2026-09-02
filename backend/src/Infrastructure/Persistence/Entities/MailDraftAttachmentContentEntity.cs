// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The octets of one file staged against a draft.</summary>
/// <remarks>
/// <para>
/// A table of its own for the reason every payload here has one: a listing of what a draft carries must never load a
/// single file's bytes, and the description beside this row is what a listing reads.
/// </para>
/// <para>
/// Deliberately, the octets are held in this column rather than through <c>IEmailContentStore</c>, whose ceiling is that a
/// staged file never reaches the object backend a deployment may have configured for its mail. It is the smaller thing
/// that is also the more accurate one here — a staged file is written in the same transaction as its row, so it needs
/// neither the placement window that machinery exists for nor the move and release sweeps built on it, and it is
/// bounded by <c>MaxAttachmentBytes</c> and lives only until the draft is sent or given up. A deployment that wants
/// staged files in object storage is a fifth <c>EmailContentKind</c> and the adapters that go with it.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailDraftAttachmentContentEntity
{
    public Guid MailDraftAttachmentId { get; set; }

    public required byte[] Content { get; set; }

    public required MailDraftAttachmentEntity Attachment { get; set; }
}
