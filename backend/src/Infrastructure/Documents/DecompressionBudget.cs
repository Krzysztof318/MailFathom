// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Counts what one container has inflated to across every part of it that was read.</summary>
/// <remarks>
/// The budget is per extraction rather than per part, because an archive of a thousand individually plausible parts
/// costs their sum. It is not thread-safe and does not need to be: one extraction reads one archive on one thread.
/// </remarks>
internal sealed class DecompressionBudget(long maxOctets, long archiveOctets)
{
    private long consumed;

    /// <summary>Reads back the compressed length a part may honestly claim.</summary>
    /// <param name="declared">The compressed length the archive's own directory declares for the part.</param>
    /// <returns>That length, or the whole archive's length where the declaration exceeds it.</returns>
    /// <remarks>
    /// The declared compressed size is a field the sender wrote rather than a fact about the data, and it is what the
    /// per-part ratio is measured against — so overstating it is how that guard would be widened until no inflation
    /// could reach it. A declaration past the end of the file is refused by the archive reader itself, measured on
    /// .NET 10 on 2026-09-05, so this clamp changes no answer that reader will accept; it is kept because the guard
    /// should not depend on a refusal made somewhere else, and because the arithmetic below multiplies this number.
    /// </remarks>
    public long HonestCompressedLength(long declared) => Math.Min(declared, archiveOctets);

    /// <summary>Records octets the container inflated to.</summary>
    /// <param name="octets">How many were inflated.</param>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when the container passes its total.</exception>
    public void Consume(int octets)
    {
        this.consumed += octets;

        if (this.consumed > maxOctets)
        {
            throw new AttachmentTextExtractionStoppedException(AttachmentTextExtractionOutcome.ContainerBoundExceeded);
        }
    }
}
