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
internal sealed class DecompressionBudget(long maxOctets)
{
    private long consumed;

    /// <summary>Records octets the container inflated to.</summary>
    /// <param name="octets">How many were inflated.</param>
    /// <exception cref="AttachmentTextExtractionBoundException">Thrown when the container passes its total.</exception>
    public void Consume(int octets)
    {
        this.consumed += octets;

        if (this.consumed > maxOctets)
        {
            throw new AttachmentTextExtractionBoundException(AttachmentTextExtractionOutcome.ContainerBoundExceeded);
        }
    }
}
