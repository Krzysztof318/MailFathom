// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Records;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>What one user's stored row bound to, and what it left refused.</summary>
/// <param name="Record">The record the user is served from, or <see langword="null" /> when their own document is not one.</param>
/// <param name="HeldBack">Every declaration refused, which is empty when the whole row bound.</param>
/// <remarks>
/// The two travel together because a partly served user is the ordinary answer rather than an exception: a record that
/// bound with one of five mailboxes left out is both a roster entry to publish and a refusal to report, and a caller
/// handed only the first would serve the user while nobody learned which mailbox stopped.
/// </remarks>
internal sealed record ServedUserRecord(
    UserAccountOptions? Record,
    IReadOnlyList<HeldBackRecord> HeldBack);
