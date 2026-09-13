// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Folders.Local;

/// <summary>What one bounded pass over the mail of an account's erased folders removed.</summary>
/// <param name="ErasedEmailCount">How many stored messages the pass erased.</param>
/// <param name="EmailsRemain">Whether messages in erased folders are still stored, so another pass is owed.</param>
public sealed record LocalMailFolderMailErasure(int ErasedEmailCount, bool EmailsRemain);
