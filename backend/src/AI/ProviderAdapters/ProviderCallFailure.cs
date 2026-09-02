// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Names what a provider call's transport-level failure was, before any one boundary interprets it.</summary>
/// <remarks>
/// <para>
/// The vocabulary an HTTP status and a client-library exception can be read into, and nothing beyond it. What each
/// member means for the work that asked is the calling boundary's to decide — an embedding call and a chat call publish
/// their own failure enumerations, and both map from this one — so the semantics of a status live in one place while
/// the consequences live where they are acted on.
/// </para>
/// <para>
/// Every member describes the remote party. Nothing this system decided is in the set, which is why a caller's
/// cancellation and a host shutdown are classified as no member at all.
/// </para>
/// </remarks>
internal enum ProviderCallFailure
{
    /// <summary>The provider refused the credential the deployment presented.</summary>
    CredentialRejected = 0,

    /// <summary>The provider refused the request because the deployment is over its allowed rate.</summary>
    RateLimited = 1,

    /// <summary>The request outlived the time allowed for it, whether by this deployment's deadline or by the provider's own.</summary>
    RequestTimedOut = 2,

    /// <summary>The request never reached an answer: the endpoint was unreachable, the connection dropped, or the response was unreadable.</summary>
    TransportFaulted = 3,

    /// <summary>The provider rejected the request itself, for a reason repeating it cannot change.</summary>
    RequestRefused = 4,
}
