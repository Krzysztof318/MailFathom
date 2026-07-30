// Copyright © 2026 Krzysztof Kasprowicz

using System.Globalization;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Failures;

namespace MailMcp.Application.EmailContent;

/// <summary>The failure raised when an email exists locally but its stored content cannot be served.</summary>
/// <remarks>
/// <para>
/// It is a failure rather than a result because the fact travels: the reader that discovers it cannot produce the
/// content the caller asked for, and every layer between it and the protocol boundary would only be passing the same
/// impossibility along. The boundary publishes it as one stable code, distinct from the code for an email that does not
/// exist, so a caller can retry a repairable message without concluding that it was never stored.
/// </para>
/// <para>
/// Raising it is never the whole response: a repair request is recorded first, so the defect a reader found is durable
/// before the reader gives up on it.
/// </para>
/// <para>
/// The message names the email identifier the caller supplied and the defect this assembly found. Neither is mail
/// content, and neither reveals anything the caller did not already hold.
/// </para>
/// </remarks>
public sealed class EmailContentUnavailableException : MailMcpException
{
    /// <summary>Initializes the failure for one email whose stored content is unusable.</summary>
    /// <param name="storedEmailId">The email the request named.</param>
    /// <param name="defect">What was found wrong with its stored content.</param>
    public EmailContentUnavailableException(StoredEmailId storedEmailId, EmailContentDefect defect)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            "The locally stored content of email '{0}' cannot be served [{1}].",
            storedEmailId.Value,
            defect))
    {
        this.StoredEmailId = storedEmailId;
        this.Defect = defect;
    }

    /// <summary>Gets the email whose stored content is unusable.</summary>
    public StoredEmailId StoredEmailId { get; }

    /// <summary>Gets what was found wrong with the stored content.</summary>
    public EmailContentDefect Defect { get; }

    /// <inheritdoc />
    public override MailMcpErrorCode ErrorCode => MailMcpErrorCode.EmailContentUnavailable;
}
