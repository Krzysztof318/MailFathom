// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.AiProviders;

/// <summary>Reads what the last call to each AI provider established about it.</summary>
/// <remarks>
/// The states are read one role at a time and never aggregated here. Which of them matters is the caller's question: a
/// health check reports each on its own, and a capability gate consults the one provider the capability needs, so an
/// unreachable chat provider never withdraws a search that has nothing to do with it.
/// </remarks>
public interface IAiProviderHealthReader
{
    /// <summary>Reads the state of one provider.</summary>
    /// <param name="role">Which provider to read.</param>
    /// <returns>What the last call to it established, which is <see cref="AiProviderHealthState.Unobserved" /> until one has been made.</returns>
    AiProviderHealth Read(AiProviderRole role);
}
