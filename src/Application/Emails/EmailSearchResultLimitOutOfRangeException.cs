// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Globalization;
using MailMcp.Domain.Failures;

namespace MailMcp.Application.Emails;

/// <summary>The failure raised when a lexical search asks for more ranked results than the search serves.</summary>
/// <remarks>
/// Refused rather than clamped, for the reason an out-of-range page size is: a clamped window looks exactly like the
/// window that was asked for, so a client that asked for a thousand results and reasoned about the completeness of what
/// came back would silently reason about a fiftieth of it. An absent count is a different input and takes the default.
/// It is its own failure rather than the page-size one because a search returns a window and no cursor continues it, so
/// a caller reading the code learns which control they met.
/// </remarks>
public sealed class EmailSearchResultLimitOutOfRangeException : MailMcpException
{
    /// <summary>Initializes the failure for one rejected result count.</summary>
    /// <param name="requestedResultLimit">The number of results the request asked for.</param>
    /// <param name="maximumResultLimit">The greatest number of results the search serves.</param>
    public EmailSearchResultLimitOutOfRangeException(int requestedResultLimit, int maximumResultLimit)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            "An email search returns between 1 and {0} ranked results, and {1} is outside that range.",
            maximumResultLimit,
            requestedResultLimit))
    {
        this.RequestedResultLimit = requestedResultLimit;
        this.MaximumResultLimit = maximumResultLimit;
    }

    /// <summary>Gets the number of results the request asked for.</summary>
    public int RequestedResultLimit { get; }

    /// <summary>Gets the greatest number of results the search serves.</summary>
    public int MaximumResultLimit { get; }

    /// <inheritdoc />
    public override MailMcpErrorCode ErrorCode => MailMcpErrorCode.EmailSearchResultLimitOutOfRange;
}
