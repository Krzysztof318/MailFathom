// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Globalization;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Emails;

/// <summary>The failure raised when a mailbox query asks for a page size outside the range the query serves.</summary>
/// <remarks>
/// An out-of-range page size is refused rather than clamped to the maximum, because the two answers differ in what the
/// caller learns: a clamped page looks like the page that was asked for, so a client walking a mailbox in pages of a
/// thousand would silently receive a hundredth of what it planned for and would have no reason to look. An absent page
/// size is a different input and takes the default instead.
/// </remarks>
public sealed class MailboxQueryPageSizeOutOfRangeException : MailFathomException
{
    /// <summary>Initializes the failure for one rejected page size.</summary>
    /// <param name="requestedPageSize">The page size the request asked for.</param>
    /// <param name="maximumPageSize">The greatest page size the query serves.</param>
    public MailboxQueryPageSizeOutOfRangeException(int requestedPageSize, int maximumPageSize)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            "A mailbox query page size must be between 1 and {0}, and {1} is outside that range.",
            maximumPageSize,
            requestedPageSize))
    {
        this.RequestedPageSize = requestedPageSize;
        this.MaximumPageSize = maximumPageSize;
    }

    /// <summary>Gets the page size the request asked for.</summary>
    public int RequestedPageSize { get; }

    /// <summary>Gets the greatest page size the query serves.</summary>
    public int MaximumPageSize { get; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailboxQueryPageSizeOutOfRange;
}
