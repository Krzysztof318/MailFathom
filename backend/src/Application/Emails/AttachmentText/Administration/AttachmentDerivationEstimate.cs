// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.AttachmentText.Administration;

/// <summary>Roughly what reading the attachments of the mail already stored would consume.</summary>
/// <param name="OutstandingEmailCount">How many stored messages hold an attachment nothing has read yet.</param>
/// <param name="OutstandingInputOctetCount">The octets those messages' attachments hold, which is what extraction would be handed.</param>
/// <param name="OutstandingAttachmentCount">How many attachments those messages hold, which bounds how many descriptions could be asked for.</param>
/// <remarks>
/// <para>
/// An order of magnitude rather than an invoice, which is the same caveat the message-embedding estimate carries. The
/// octet figure is what the mail server reported for those parts and is therefore an upper bound on what a parser is
/// handed: the per-attachment, per-message, and per-run ceilings each cut it further, and an attachment of a format
/// this deployment does not read is counted here and parsed nowhere.
/// </para>
/// <para>
/// The attachment count bounds descriptions rather than predicting them, and deliberately so: whether a part is a
/// picture is settled by offering it to the extractor first, which cannot be known before the message is opened. An
/// operator reading this is being told the worst case, which is the number worth agreeing to in advance.
/// </para>
/// <para>
/// Counts alone. No file name, media type, subject, or word is describable from any of them.
/// </para>
/// </remarks>
public sealed record AttachmentDerivationEstimate(
    int OutstandingEmailCount,
    long OutstandingInputOctetCount,
    long OutstandingAttachmentCount)
{
    /// <summary>Gets an estimate with nothing left to read, which is what an instance holding no mail reports.</summary>
    public static AttachmentDerivationEstimate Nothing { get; } = new(
        OutstandingEmailCount: 0,
        OutstandingInputOctetCount: 0,
        OutstandingAttachmentCount: 0);

    /// <summary>Gets whether there is nothing left for a reading to reach.</summary>
    public bool IsComplete => this.OutstandingEmailCount == 0;
}
