// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Mcp.Tools.Content;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes what a read produced for one of the emails it was asked about.</summary>
/// <remarks>
/// Exactly one of <see cref="Content" /> and <see cref="Failure" /> is present, and the identifier is present either
/// way. One email this deployment cannot serve therefore costs the caller that email rather than the whole call, which
/// is the reason a read answers per email at all.
/// </remarks>
[Description("What the read produced for one named email: its content, or why there is none. Exactly one of the two is present, and storedEmailId names the email either way.")]
internal sealed record RetrievedEmail
{
    /// <summary>Gets the email this entry answers for, which is the one the call named.</summary>
    [Description("The stable local identifier of the email this entry answers for, which is the one the call named.")]
    public required string StoredEmailId { get; init; }

    /// <summary>Gets the email as a reader receives it, or <see langword="null" /> when it could not be served.</summary>
    [Description("The email as it was read, or null when it could not be served, in which case failure says why.")]
    public RetrievedEmailContent? Content { get; init; }

    /// <summary>Gets why the email could not be served, or <see langword="null" /> when it was.</summary>
    [Description("Why this email carries no content, or null when content is present.")]
    public RetrievedEmailFailure? Failure { get; init; }

    /// <summary>Publishes one outcome a read produced.</summary>
    /// <param name="outcome">The outcome to publish.</param>
    /// <returns>The wire representation of <paramref name="outcome" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="outcome" /> is <see langword="null" />.</exception>
    public static RetrievedEmail From(EmailContentReadOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new RetrievedEmail
        {
            StoredEmailId = outcome.StoredEmailId.ToString(),
            Content = outcome.Content is { } content ? RetrievedEmailContent.From(content) : null,
            Failure = outcome.Failure is { } failure ? RetrievedEmailFailure.From(failure) : null,
        };
    }
}
