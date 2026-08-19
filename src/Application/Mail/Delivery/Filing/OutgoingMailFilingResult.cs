// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>Reports what one filing attempt did, in the terms a pass tallies and a metric is broken down by.</summary>
/// <param name="OutgoingEmailId">The send whose copy was being filed.</param>
/// <param name="Filing">The place the copy was going into, or the unspecified default where the attempt reached none.</param>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="Failure">The code recorded against the record, or <see langword="null" /> when the attempt recorded none.</param>
/// <remarks>
/// <para>
/// Nothing here is mail. The identifier is MailFathom's own, the filing is one of a closed set of words this system
/// chose, and the failure is a code — no subject, no address, and no folder path.
/// </para>
/// <para>
/// The filing may be the unspecified struct default, which is not a missing value but the answer itself: a failure
/// that ended before any place was chosen says nothing about which place, and naming one anyway would put a word into
/// a log line and a metric dimension that the failure never established. Read the place through
/// <see cref="FilingName" /> rather than through <see cref="OutgoingMailFiling.Name" />, which refuses the default.
/// </para>
/// </remarks>
public sealed record OutgoingMailFilingResult(
    OutgoingEmailId OutgoingEmailId,
    OutgoingMailFiling Filing,
    OutgoingMailFilingOutcome Outcome,
    MailFathomErrorCode? Failure)
{
    /// <summary>The word a log line and a metric dimension carry for an attempt that reached no place at all.</summary>
    public const string UndeterminedFilingName = "undetermined";

    /// <summary>Gets the place this attempt was about, as the one name every reader of this result records it by.</summary>
    public string FilingName => this.Filing.IsSpecified ? this.Filing.Name : UndeterminedFilingName;
}
