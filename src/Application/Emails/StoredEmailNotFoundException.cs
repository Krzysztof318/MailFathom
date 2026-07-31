// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Globalization;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Failures;

namespace MailMcp.Application.Emails;

/// <summary>The failure raised when a request names an email the local mailbox copy holds no row for.</summary>
/// <remarks>
/// <para>
/// One failure covers an identifier that never existed, one whose email was expunged and collected, and one belonging
/// to an account this deployment has stopped serving. A caller that could tell them apart could learn which identifiers
/// exist by asking, and none of the three is anything they can act on differently.
/// </para>
/// <para>
/// The message names the identifier the caller supplied, which is MailMcp's own handle for the email and carries
/// nothing the caller did not already write.
/// </para>
/// </remarks>
public sealed class StoredEmailNotFoundException : MailMcpException
{
    /// <summary>Initializes the failure for one unknown email identifier.</summary>
    /// <param name="storedEmailId">The email identifier the request named.</param>
    public StoredEmailNotFoundException(StoredEmailId storedEmailId)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            "Email '{0}' is not stored in this mailbox copy.",
            storedEmailId.Value)) => this.StoredEmailId = storedEmailId;

    /// <summary>Gets the email identifier the request named.</summary>
    public StoredEmailId StoredEmailId { get; }

    /// <inheritdoc />
    public override MailMcpErrorCode ErrorCode => MailMcpErrorCode.StoredEmailNotFound;
}
