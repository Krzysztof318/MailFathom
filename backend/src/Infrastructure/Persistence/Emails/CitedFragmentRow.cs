// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>One passage as the query projects it, before it becomes the application's own reading of one.</summary>
/// <remarks>
/// A projection of its own rather than the application type directly, because the domain identity is created outside
/// the query: <see cref="EmailChunkId.Create" /> is a method the provider cannot translate, and putting it in the
/// selector would either fail to translate or pull the table into this process to run it.
/// </remarks>
/// <param name="Id">The passage's identifier.</param>
/// <param name="Ordinal">Its position in its message, counted from zero in reading order.</param>
/// <param name="StartOffset">Where it begins in the extracted text it was cut from.</param>
/// <param name="Text">The passage itself.</param>
internal sealed record CitedFragmentRow(Guid Id, int Ordinal, int StartOffset, string Text);
