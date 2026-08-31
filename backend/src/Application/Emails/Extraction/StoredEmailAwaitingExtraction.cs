// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Extraction;

/// <summary>Names one locally stored email whose raw MIME has never been read for metadata and text.</summary>
/// <param name="StoredEmailId">The stable local identity, which is also the backfill's resume position.</param>
/// <param name="OccurrenceId">The remote occurrence identity the extracted metadata is recorded under.</param>
/// <param name="Owner">The owner the message belongs to, whose posture decides what is redacted out of its body.</param>
/// <remarks>
/// <para>
/// The occurrence identity travels with the row because extraction is expressed in terms of the message a deployment
/// fetched, not of the row it landed in: the same record shape then describes a message synchronization just read and
/// one the backfill re-reads years later.
/// </para>
/// <para>
/// The owner travels with it for the reason the walk is one walk over everybody's mail: two rows in one batch can
/// belong to owners with different scanning postures, so the answer has to be a property of the row rather than of the
/// pass.
/// </para>
/// </remarks>
public sealed record StoredEmailAwaitingExtraction(
    StoredEmailId StoredEmailId,
    EmailOccurrenceId OccurrenceId,
    MailOwnerId Owner);
