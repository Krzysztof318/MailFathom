// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>Carries one bounded page of a person's tasks and the boundary the next page continues from.</summary>
/// <param name="Tasks">The tasks, soonest due first, with the undated ones last.</param>
/// <param name="NextCursor">The cursor a caller presents for the following page, or <see langword="null" /> when this page reached the end of the list.</param>
/// <remarks>
/// The cursor is the encoded form rather than the <see cref="PersonalTaskCursor" /> the store walks by, because what a
/// page hands back is what a caller presents again: the position the store takes carries no fingerprint and no version,
/// and issuing it raw would be handing a client a boundary it could compose one of for itself.
/// <para>
/// The absent cursor is the end of the walk rather than a page that happened to be short: a page is only ever short
/// because the list held nothing more, so a caller stops when the cursor stops instead of comparing the count against
/// the size it asked for.
/// </para>
/// </remarks>
public sealed record PersonalTaskPage(IReadOnlyList<PersonalTask> Tasks, string? NextCursor);
