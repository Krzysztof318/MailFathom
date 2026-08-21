// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Chunking;

/// <summary>One stored message the arrival pipeline has carried as far as the cut, and no further.</summary>
/// <param name="StoredEmailId">The message whose passages are still to be cut.</param>
/// <param name="Admission">Why the classification gate lets this message be derived from, which is what the pass reports as it cuts.</param>
/// <remarks>
/// The admission is carried rather than asked for again, because the query that selected the message already decided it.
/// Nothing else travels: a subject, a participant, and a body are mail content, and a pass reports counts.
/// </remarks>
public sealed record StoredEmailAwaitingChunking(
    StoredEmailId StoredEmailId,
    DerivedWorkAdmission Admission = DerivedWorkAdmission.Admitted);
