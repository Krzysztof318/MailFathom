// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Drafts;

/// <summary>Records that a copy MailFathom appended stopped being one it may still replace or remove.</summary>
/// <param name="Reason">Which fact took the copy out of reach.</param>
/// <param name="ObservedAt">When the attempt that would have touched the copy found it out.</param>
/// <remarks>
/// It is written onto the draft rather than logged and forgotten, because it is what tells an owner why the draft in
/// their mail client stopped following the one this deployment holds. Nothing about it is mail content: a reason and an
/// instant are this system's own account of its own act.
/// </remarks>
public sealed record MailDraftDivergence(MailDraftDivergenceReason Reason, DateTimeOffset ObservedAt);
