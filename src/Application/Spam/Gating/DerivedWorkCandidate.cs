// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Gating;

/// <summary>Everything an admission is decided from about one occurrence, and nothing else.</summary>
/// <remarks>
/// Four facts, none of them mail. Where the message is and when it was stored come from the occurrence, what was
/// concluded about it comes from its classification, and whether it carries anything a classification could read comes
/// from its content availability — so an admission can be decided without opening the message and can be reported
/// without describing it.
/// </remarks>
/// <param name="AccountId">The account the occurrence belongs to.</param>
/// <param name="FolderAlias">MailFathom's own name for the folder holding it now.</param>
/// <param name="StoredAt">When this occurrence was first recorded locally, which is when its wait for a verdict began.</param>
/// <param name="ContentAvailability">Whether the raw MIME a classification reads is stored, is coming, or never will be.</param>
/// <param name="Verdict">What classification concluded, or <see langword="null" /> when it has reached no conclusion yet.</param>
public sealed record DerivedWorkCandidate(
    MailAccountId AccountId,
    MailFolderAlias FolderAlias,
    DateTimeOffset StoredAt,
    StoredEmailContentAvailability ContentAvailability,
    SpamVerdict? Verdict);
