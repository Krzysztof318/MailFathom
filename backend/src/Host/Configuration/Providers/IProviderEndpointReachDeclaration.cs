// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Providers;

/// <summary>The part of an AI endpoint declaration that says where the endpoint is and what a request to it presents.</summary>
/// <remarks>
/// <para>
/// Both roles declare these four settings and both are judged by the same rules, because the question they answer is
/// one question: whether a credential this deployment holds would cross a network in the clear. Writing the rules twice
/// is how the two would come to differ, and the direction that matters is the lax one — an embedding endpoint accepting
/// what a chat endpoint refuses is a confidentiality decision nobody took.
/// </para>
/// <para>
/// An interface rather than a shared record the two copy themselves into, so the member names the rules report are the
/// keys an operator actually edits and a rename moves both sides at once.
/// </para>
/// </remarks>
internal interface IProviderEndpointReachDeclaration
{
    /// <summary>Gets the base address requests are sent to, empty for the provider library's own default.</summary>
    string Address { get; }

    /// <summary>Gets the reference to the provider key this endpoint is authenticated with, absent for the other two shapes.</summary>
    ConfiguredSecret? ApiKey { get; }

    /// <summary>Gets the non-interactive Microsoft Entra credential this endpoint is authenticated with, absent for the other two shapes.</summary>
    ProviderEntraCredentialOptions? EntraCredential { get; }

    /// <summary>Gets whether this endpoint is declared to need no credential at all.</summary>
    bool Unauthenticated { get; }
}
