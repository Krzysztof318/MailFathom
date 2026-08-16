// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>Names one locally stored email whose raw MIME the re-derivation pass is about to read again.</summary>
/// <param name="StoredEmailId">The stable local identity, which is also the pass's resume position.</param>
/// <param name="OccurrenceId">The remote occurrence identity the re-read metadata is recorded under.</param>
/// <remarks>
/// It is a separate record from the one the extraction backfill walks, because the two describe opposite states of the
/// same row: that backfill reaches a message whose MIME has never been read, and this pass reaches one whose MIME was
/// read by a release that recorded fewer properties than this one does.
/// </remarks>
public sealed record StoredMailAwaitingRederivation(StoredEmailId StoredEmailId, EmailOccurrenceId OccurrenceId);
