// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Calendar.Import;

/// <summary>How many entries of an offered file were skipped for one reason.</summary>
/// <remarks>
/// A count rather than the entries themselves, which is the whole of what a person deciding whether to confirm an
/// import needs: that eight of eighty entries recur is a fact about the file, while which eight is a list of somebody
/// else's appointments this deployment has no reason to read back.
/// </remarks>
/// <param name="Reason">Why these entries became no event.</param>
/// <param name="Count">How many of them there were, which is never zero.</param>
public readonly record struct CalendarImportSkipTally(CalendarImportSkipReason Reason, int Count);
