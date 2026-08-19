// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>Reports what one filing attempt did, in the terms a pass tallies and a metric is broken down by.</summary>
/// <param name="OutgoingEmailId">The send whose copy was being filed.</param>
/// <param name="Filing">The place the copy was going into.</param>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="Failure">The code recorded against the record, or <see langword="null" /> when the attempt recorded none.</param>
/// <remarks>
/// Nothing here is mail. The identifier is MailFathom's own, the filing is one of a closed set of words this system
/// chose, and the failure is a code — no subject, no address, and no folder path.
/// </remarks>
public sealed record OutgoingMailFilingResult(
    OutgoingEmailId OutgoingEmailId,
    OutgoingMailFiling Filing,
    OutgoingMailFilingOutcome Outcome,
    MailFathomErrorCode? Failure);
