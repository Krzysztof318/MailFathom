// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Domain.Emails;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Mints predictable links so the protocol contract can be asserted without key material.</summary>
/// <param name="canIssueLinks">Whether this deployment is configured to mint anything.</param>
internal sealed class StubAttachmentDownloadLinkIssuer(bool canIssueLinks = true) : IAttachmentDownloadLinkIssuer
{
    /// <summary>The instant every minted link expires at.</summary>
    public static readonly DateTimeOffset Expiry = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The address every minted link is composed beneath.</summary>
    public const string AddressPrefix = "https://mailfathom.example.test/attachments/";

    /// <inheritdoc />
    public bool CanIssueLinks => canIssueLinks;

    /// <inheritdoc />
    public Task<IReadOnlyList<AttachmentDownloadLink>> IssueAsync(
        StoredEmailId storedEmailId,
        int attachmentCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<AttachmentDownloadLink>>(
        [
            .. Enumerable.Range(0, attachmentCount).Select(position => new AttachmentDownloadLink(
                new Uri($"{AddressPrefix}{storedEmailId.Value:N}-{position}"),
                Expiry)),
        ]);
    }
}
