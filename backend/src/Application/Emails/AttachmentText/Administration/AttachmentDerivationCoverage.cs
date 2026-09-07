// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.AttachmentText.Administration;

/// <summary>How much of a mailbox's attachment and image content has been read, how much waits, and what was skipped.</summary>
/// <param name="EmailsWithAttachmentCount">How many stored messages a reading could reach at all, which is what the rest is measured against.</param>
/// <param name="ReadEmailCount">How many of those have had their attachments read and stamped.</param>
/// <param name="Outstanding">What is left, and roughly what reading it would consume.</param>
/// <param name="DocumentTextAttachmentCount">How many attachments yielded a document's own words.</param>
/// <param name="DescribedImageCount">How many attachments yielded a description of a picture.</param>
/// <param name="IndexedCharacterCount">The characters those document readings put into the lexical index.</param>
/// <param name="Skips">One entry per recorded reason an attachment yielded nothing, largest first.</param>
/// <remarks>
/// <para>
/// Reported apart from the message coverage beside it rather than folded into it, because the two answer different
/// questions and are paid for in different units: a message is embedded once its own text has a vector, and its
/// attachments may still be entirely unread. An operator seeing one figure would read a mailbox as complete while every
/// contract in it was still waiting.
/// </para>
/// <para>
/// <see cref="IndexedCharacterCount" /> is storage rather than provider spend, which is why it sits here beside what
/// extraction consumed rather than inside a ceiling that prices a call. It grows when a document's words are written to
/// the lexical index and it is what an operator watches to see the database growing; a description contributes none of
/// it, because
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// keeps a model's account of a picture out of the lexical index.
/// </para>
/// <para>
/// Counts and one aggregate per reason. Nothing here names a message, a file, or a word.
/// </para>
/// </remarks>
public sealed record AttachmentDerivationCoverage(
    int EmailsWithAttachmentCount,
    int ReadEmailCount,
    AttachmentDerivationEstimate Outstanding,
    long DocumentTextAttachmentCount,
    long DescribedImageCount,
    long IndexedCharacterCount,
    IReadOnlyList<AttachmentSkipCount> Skips)
{
    /// <summary>Gets the coverage of an instance holding no mail with attachments, which is also what one that reads none reports.</summary>
    public static AttachmentDerivationCoverage Nothing { get; } = new(
        EmailsWithAttachmentCount: 0,
        ReadEmailCount: 0,
        AttachmentDerivationEstimate.Nothing,
        DocumentTextAttachmentCount: 0,
        DescribedImageCount: 0,
        IndexedCharacterCount: 0,
        Skips: []);

    /// <summary>Gets how many attachments were skipped for a recorded reason.</summary>
    public long SkippedAttachmentCount => this.Skips.Sum(skip => skip.AttachmentCount);

    /// <summary>Gets whether every message a reading could reach has been read.</summary>
    /// <remarks>
    /// A mailbox may be complete and still have skipped a great deal, which is exactly the pair of facts this value
    /// exists to keep apart: nothing is outstanding, and <see cref="Skips" /> says what yielded nothing.
    /// </remarks>
    public bool IsComplete => this.Outstanding.IsComplete;
}
