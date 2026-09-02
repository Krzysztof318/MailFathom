// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Chat;

/// <summary>What one call consumed, in the units a provider bills by.</summary>
/// <param name="InputTokens">The tokens the conversation sent occupied.</param>
/// <param name="OutputTokens">The tokens the answer occupied.</param>
/// <remarks>
/// Two counts and nothing else. They are the only part of a chat call that is safe to log, meter, and keep — a count
/// describes the size of what was sent without describing any of it — and they are what makes a chat provider's cost
/// visible while it is being spent rather than at the end of a billing period.
/// </remarks>
public sealed record ChatTokenUsage(long InputTokens, long OutputTokens);
