// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One file an author staged against a draft, without the octets it is made of.</summary>
/// <remarks>
/// A row of its own rather than a part of the draft's composed message, because a file is uploaded once and belongs to
/// the draft: composing it into every revision from here is what keeps editing a subject from re-sending a file the
/// author already handed over. The octets sit in the one-to-one table beside it, so listing what a draft carries never
/// loads any of them.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailDraftAttachmentEntity
{
    public Guid Id { get; set; }

    public Guid MailDraftId { get; set; }

    /// <summary>Gets or sets the name the recipient will see the file under.</summary>
    /// <remarks>
    /// Untrusted input that ends up in a header, exactly as a subject does. Nothing here judges it: what refuses a
    /// name a header cannot carry is the composition, which is the one place every authored field is checked.
    /// </remarks>
    public required string FileName { get; set; }

    /// <summary>Gets or sets the media type the author declared the file as, which is recorded rather than sniffed.</summary>
    public required string MediaType { get; set; }

    /// <summary>Gets or sets how many octets were stored for the file.</summary>
    /// <remarks>
    /// Kept beside the description for the reason the draft keeps its own MIME length: a listing says how large the
    /// file is, and reading the <c>bytea</c> to learn that would load the file to answer a question about its size.
    /// </remarks>
    public long ByteLength { get; set; }

    /// <summary>Gets or sets when the upload was taken in, which is the order a composition attaches the files in.</summary>
    public DateTimeOffset StagedAt { get; set; }

    /// <summary>Gets or sets the octets of the file, loaded only where a message is about to be composed.</summary>
    public MailDraftAttachmentContentEntity? Content { get; set; }

    public required MailDraftEntity MailDraft { get; set; }
}
