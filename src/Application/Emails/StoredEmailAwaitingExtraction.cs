// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails;

/// <summary>Names one locally stored email whose raw MIME has never been read for metadata and text.</summary>
/// <param name="StoredEmailId">The stable local identity, which is also the backfill's resume position.</param>
/// <param name="OccurrenceId">The remote occurrence identity the extracted metadata is recorded under.</param>
/// <remarks>
/// The occurrence identity travels with the row because extraction is expressed in terms of the message a deployment
/// fetched, not of the row it landed in: the same record shape then describes a message synchronization just read and
/// one the backfill re-reads years later.
/// </remarks>
public sealed record StoredEmailAwaitingExtraction(StoredEmailId StoredEmailId, EmailOccurrenceId OccurrenceId);
