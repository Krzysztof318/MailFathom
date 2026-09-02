// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Mints predictable links and records what it was asked to mint them for.</summary>
/// <remarks>
/// A substitute could return links, but not show what the use case asked for: the interesting claims are that nothing is
/// minted for a read that did not ask, that a message carrying no file reaches the issuer not at all, and that the links
/// a read publishes line up positionally with the attachments it described. Recording the calls is what makes those
/// assertable.
/// </remarks>
/// <param name="canIssueLinks">Whether this deployment is configured to mint anything.</param>
/// <param name="expiresAt">The expiry every minted link carries.</param>
/// <param name="issueAtMost">
/// A ceiling on how many links come back, which models the key ring being emptied between the guard and the call — the
/// one way the real issuer answers with fewer links than the message has attachments.
/// </param>
internal sealed class RecordingAttachmentDownloadLinkIssuer(
    bool canIssueLinks = true,
    DateTimeOffset? expiresAt = null,
    int? issueAtMost = null)
    : IAttachmentDownloadLinkIssuer
{
    /// <summary>The instant a minted link expires when a test states none.</summary>
    public static readonly DateTimeOffset DefaultExpiry = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Gets what each call asked for, in the order the calls happened.</summary>
    public List<(StoredEmailId StoredEmailId, int AttachmentCount)> Requested { get; } = [];

    /// <inheritdoc />
    public bool CanIssueLinks => canIssueLinks;

    /// <inheritdoc />
    public Task<IReadOnlyList<AttachmentDownloadLink>> IssueAsync(
        StoredEmailId storedEmailId,
        int attachmentCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.Requested.Add((storedEmailId, attachmentCount));

        return Task.FromResult<IReadOnlyList<AttachmentDownloadLink>>(
        [
            .. Enumerable.Range(0, Math.Min(attachmentCount, issueAtMost ?? attachmentCount))
                .Select(position => new AttachmentDownloadLink(
                new Uri($"https://mailfathom.example.test/attachments/{storedEmailId.Value:N}-{position}"),
                expiresAt ?? DefaultExpiry)),
        ]);
    }
}
