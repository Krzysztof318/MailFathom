// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MailFathom.Host.Configuration.Persistence;

/// <summary>Configures what one read of message content may return.</summary>
/// <remarks>
/// <para>
/// Two bounds on body text, because a call names several emails and the count of them is bounded in code while the
/// volume they return is bounded here. Neither replaces the other: without the per-email bound one enormous message
/// would spend a whole call's budget, and without the budget ten messages would each return the per-email bound in full.
/// </para>
/// <para>
/// Attachment content is bounded by the same pair again, in bytes rather than characters. The two pairs are spent
/// independently because a caller asks for text and for files separately, and because base64 makes a byte cost more of
/// a response than a character does.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class EmailContentOptions : IValidatableObject
{
    /// <summary>Gets or sets the maximum number of characters one body representation returns.</summary>
    /// <remarks>
    /// <para>
    /// The range is about what a response can usefully carry rather than about what can be stored. The lower bound
    /// keeps a configured value from making every message look truncated, and the upper bound is where a single body
    /// stops fitting in the context of anything that would read it: a million characters is several times the largest
    /// context an MCP client can be expected to hold, so a body above it would be discarded by the caller rather than
    /// read.
    /// </para>
    /// <para>
    /// It is not the bound that protects this process. A body can only be as large as the raw MIME it was stored
    /// under, which <c>MailSynchronization:MaxRawMimeBytes</c> already limits; this decides how much of it a caller is
    /// handed.
    /// </para>
    /// </remarks>
    [Range(1_000, 1_000_000)]
    public int MaxBodyCharacters { get; set; } = 100_000;

    /// <summary>Gets or sets the maximum number of body characters one call returns across every email it names.</summary>
    /// <remarks>
    /// <para>
    /// It is the volume half of the control on how much mail one protocol call can draw out of a mailbox, and nothing a
    /// request carries can raise it. The budget is spent in the order the emails were named, so a call whose first
    /// emails are large returns less of the later ones and says so on each representation it had to cut.
    /// </para>
    /// <para>
    /// The upper bound is twice the largest per-representation bound, which is what one email asking for both
    /// representations can return; a budget above it would bound nothing that the per-email bound and the count of ten
    /// do not already bound.
    /// </para>
    /// </remarks>
    [Range(2_000, 2_000_000)]
    public int MaxCharactersPerRead { get; set; } = 200_000;

    /// <summary>Gets or sets the greatest number of decoded bytes one attachment returns.</summary>
    /// <remarks>
    /// <para>
    /// A file above it is described exactly as it would be otherwise and comes back with no content, because an
    /// attachment is returned whole or not at all. Zero is therefore the setting for a deployment that wants
    /// attachments described and never handed over, and it is a supported configuration rather than a degenerate one.
    /// </para>
    /// <para>
    /// The upper bound is what one stored message can hold, since <c>MailSynchronization:MaxRawMimeBytes</c> refuses
    /// anything larger and no attachment is larger than the message carrying it. What this decides is how much of it a
    /// caller is handed, in a response where base64 makes it a third larger again.
    /// </para>
    /// </remarks>
    [Range(0, 25 * 1024 * 1024)]
    public int MaxAttachmentBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Gets or sets the greatest number of attachment bytes one call returns across every email it names.</summary>
    /// <remarks>
    /// It is the volume half of the control on how much attachment content one protocol call can draw out of a mailbox,
    /// and nothing a request carries can raise it. The budget is spent in the order the emails were named and, within an
    /// email, in the order its parts were walked; an attachment reached after it is spent says so instead of arriving
    /// truncated.
    /// </remarks>
    [Range(0, 100 * 1024 * 1024)]
    public int MaxAttachmentBytesPerRead { get; set; } = 10 * 1024 * 1024;

    /// <summary>Checks the two rules no range can state on its own.</summary>
    /// <param name="validationContext">The context the options framework validates against.</param>
    /// <returns>The failures for a budget that cannot cover one email, or nothing when the bounds agree.</returns>
    /// <remarks>
    /// A single email asking for both representations may return <see cref="MaxBodyCharacters" /> twice, so a budget
    /// below that would cut a one-email call by a limit that exists for calls naming several — and the truncation it
    /// reported would send a caller to split a call it cannot split further. An attachment budget below
    /// <see cref="MaxAttachmentBytes" /> is the same defect in the other pair: it would withhold a file the
    /// per-attachment bound was set to allow, in every call including one naming a single email. Startup is where both
    /// are caught, because the alternative is a deployment discovering them one read at a time.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this.MaxCharactersPerRead < 2 * (long)this.MaxBodyCharacters)
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "EmailContent:MaxCharactersPerRead must be at least twice EmailContent:MaxBodyCharacters, which is {0}.",
                    2 * (long)this.MaxBodyCharacters),
                [nameof(this.MaxCharactersPerRead)]);
        }

        if (this.MaxAttachmentBytesPerRead < this.MaxAttachmentBytes)
        {
            yield return new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "EmailContent:MaxAttachmentBytesPerRead must be at least EmailContent:MaxAttachmentBytes, which is {0}.",
                    this.MaxAttachmentBytes),
                [nameof(this.MaxAttachmentBytesPerRead)]);
        }
    }
}
