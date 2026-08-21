// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Embeddings.Backfill;

/// <summary>One stored message the backfill walk found something outstanding for.</summary>
/// <param name="StoredEmailId">The message to bring up to date.</param>
/// <param name="RequiresChunking">Whether the message has extracted text and no passages at all, so passages have to be cut before anything can be embedded.</param>
/// <param name="Admission">Why the classification gate lets this message be derived from, which is what the sweep reports when it cuts passages the gate was holding.</param>
/// <remarks>
/// Both facts are carried rather than re-derived, because the query that selected the message already answered them and
/// a second question per message would be a second round trip for something the first one knew. Nothing else about the
/// message travels: its subject, its participants, and its text are mail content, and a walk reports counts.
/// </remarks>
public sealed record StoredEmailAwaitingEmbedding(
    StoredEmailId StoredEmailId,
    bool RequiresChunking,
    DerivedWorkAdmission Admission = DerivedWorkAdmission.Admitted);
