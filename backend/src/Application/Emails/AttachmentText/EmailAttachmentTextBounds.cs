// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>What reading a whole message's attachments, and a whole account run's, may consume.</summary>
/// <remarks>
/// <para>
/// The third and fourth of the four ceilings
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0029-what-an-embedding-is-derived-from-and-whether-attachment-text-joins-it.md">ADR 0029</see>
/// names. <see cref="Extraction.Attachments.AttachmentTextExtractionOptions" /> bounds one attachment; these bound the
/// message it arrived on and the run that met it, because the cost of a message stopped being predictable from its text
/// length the moment attachments were parsed at all — a ten-line covering note with a two-hundred-page report attached
/// is an expensive message, and a mailbox full of them is an expensive run.
/// </para>
/// <para>
/// Both are counted in octets read rather than in characters sent, which is the distinction the record draws between
/// this budget and the embedding one: what a provider is paid for is characters, and what a parser costs is the bytes
/// it was handed. A message that runs out records the reason against the attachments it never opened, so the ceiling is
/// answerable to a user rather than silent; a run that runs out leaves the message it was on untouched, because
/// nothing about that message has been decided and the next run should reach it.
/// </para>
/// <para>
/// <b>The three ceilings are ordered, and configuration refuses a deployment that writes them otherwise:</b> what one
/// attachment may cost is at most what its message may, and what a message may is at most what its run may. The
/// ordering is what stops a run from meeting an attachment it could never afford — such a message would be passed over
/// by every run for ever, having been refused by a budget that starts full each time. With the ordering held, a
/// refusal only ever happens to a run that has already spent, and the next run reaches that message first.
/// </para>
/// </remarks>
/// <param name="IsEnabled">
/// Whether this deployment reads its mail's attachments at all, which is off unless an operator turned it on. It rides
/// with the ceilings rather than arriving as a switch of its own because it is the same answer at zero: what a
/// deployment may read out of an attachment, before any of the numbers below are reached.
/// </param>
/// <param name="MaxAttachmentsPerEmail">How many of one message's attachments are read at all, counted in walk order.</param>
/// <param name="MaxInputOctetsPerEmail">How many octets one message's attachments may be read from together.</param>
/// <param name="MaxInputOctetsPerAccountRun">How many octets one account run may read across every message it reaches.</param>
public sealed record EmailAttachmentTextBounds(
    bool IsEnabled,
    int MaxAttachmentsPerEmail,
    long MaxInputOctetsPerEmail,
    long MaxInputOctetsPerAccountRun)
{
    /// <summary>How many of one message's attachments are read where a deployment declares no ceiling of its own.</summary>
    public const int DefaultMaxAttachmentsPerEmail = 20;

    /// <summary>The octets one message's attachments may be read from where a deployment declares no ceiling of its own.</summary>
    public const long DefaultMaxInputOctetsPerEmail = 64L * 1024 * 1024;

    /// <summary>The octets one account run may read where a deployment declares no ceiling of its own.</summary>
    public const long DefaultMaxInputOctetsPerAccountRun = 4L * 1024 * 1024 * 1024;

    /// <summary>Gets what a deployment that declared nothing is bounded by, which reads no attachment at all.</summary>
    public static EmailAttachmentTextBounds Disabled { get; } = new(
        IsEnabled: false,
        DefaultMaxAttachmentsPerEmail,
        DefaultMaxInputOctetsPerEmail,
        DefaultMaxInputOctetsPerAccountRun);
}
