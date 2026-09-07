// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Egress;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>Scans the sentences a message's marks carry before they leave the deployment.</summary>
/// <remarks>
/// Shared by every read that publishes a derivation, so a mark cannot be scanned one way where a list draws it and
/// another way where a conversation does. A mark's aspect, provenance, date, and evidence are MailFathom's own values
/// and passage identifiers, so nothing about them is somebody's mail; what is, is the reading and the reason, and both
/// are scanned exactly as the preview beside them is.
/// </remarks>
internal static class GuardedEmailEnrichment
{
    /// <summary>Scans one message's marks, dropping any the scanner withheld.</summary>
    /// <param name="egressGuard">The guard the surrounding read already opened its scan on.</param>
    /// <param name="egressPoint">The point this read publishes at, which the scan was begun under.</param>
    /// <param name="enrichment">What was derived about the message, or <see langword="null" /> where nothing has been.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The derivation with its readings scanned, or <see langword="null" /> where none was given.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="egressGuard" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A mark a scanner withheld entirely is dropped rather than published as an empty sentence. It is the same decision
    /// the scanner already made about the text the mark was derived from, carried one step further — and it leaves a
    /// derivation that settled with something to say indistinguishable from one that settled with nothing, which is the
    /// answer withholding is supposed to give.
    /// </remarks>
    internal static async Task<EmailEnrichment?> ScanAsync(
        SensitiveContentEgressGuard egressGuard,
        SensitiveContentEgressPoint egressPoint,
        EmailEnrichment? enrichment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(egressGuard);

        if (enrichment is null || enrichment.Marks.Count is 0)
        {
            return enrichment;
        }

        var guarded = new List<EmailEnrichmentMark>(enrichment.Marks.Count);

        foreach (var mark in enrichment.Marks)
        {
            var text = await egressGuard.GuardAsync(egressPoint, mark.Text, cancellationToken);
            var reason = await egressGuard.GuardAsync(egressPoint, mark.Reason, cancellationToken);

            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(reason))
            {
                continue;
            }

            guarded.Add(EmailEnrichmentMark.Create(
                mark.Aspect,
                text,
                reason,
                mark.Evidence,
                mark.Provenance,
                mark.DueAt));
        }

        return enrichment with { Marks = guarded };
    }
}
