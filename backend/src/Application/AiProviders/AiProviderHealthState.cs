// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.AiProviders;

/// <summary>Says what the last call to one AI provider established about it.</summary>
/// <remarks>
/// <para>
/// The set is written for the operator's next move rather than for the failure's shape. Waiting is the answer to one
/// member, editing configuration or rotating a credential is the answer to another, and the third says only that
/// nothing has been asked yet. The finer classification each adapter produces stays in its own failure enumeration,
/// which is what the resilience pipeline reads.
/// </para>
/// <para>
/// It is observed rather than probed. Nothing here calls a provider to find out how it is: a paid call made to answer a
/// health check would spend an operator's money on every scrape, and the answer would be about a request nobody asked
/// for. What this reports is the outcome of the last real call, which is the only evidence available for free.
/// </para>
/// </remarks>
public enum AiProviderHealthState
{
    /// <summary>No call has been made yet, so nothing is known.</summary>
    /// <remarks>The state of a freshly started instance, and of a configured provider whose work has not arrived yet. It is not a failure and must never read as one.</remarks>
    Unobserved = 0,

    /// <summary>The last call produced an answer.</summary>
    Serving = 1,

    /// <summary>The last call failed for a reason that may pass on its own.</summary>
    /// <remarks>A rate limit, a timeout, an unreachable endpoint. The work belongs to a later attempt and nobody has to do anything for that attempt to differ.</remarks>
    Unavailable = 2,

    /// <summary>The last call failed for a reason no later attempt changes.</summary>
    /// <remarks>A refused credential, a rejected request, an answer of the wrong shape. Somebody has to rotate a secret or correct a declaration, and until they do every further call buys the same answer.</remarks>
    Misconfigured = 3,
}
