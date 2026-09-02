// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ClientModel.Primitives;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Carries a request to an endpoint that asks for no credential, adding nothing to it.</summary>
/// <remarks>
/// <para>
/// The client library takes either a key or an authentication policy and has no third construction for an endpoint that
/// wants neither, so the shape with no credential is expressed as the policy that authenticates nothing. What that buys
/// over the alternative is exactness: passing a placeholder key would put an <c>Authorization</c> header on every
/// request carrying a value the operator never wrote, and a server that reads the header would then be told something
/// untrue rather than nothing at all.
/// </para>
/// <para>
/// One instance serves every such endpoint, because the policy holds no state and its whole behaviour is to pass
/// control on.
/// </para>
/// </remarks>
internal sealed class UnauthenticatedRequestPolicy : AuthenticationPolicy
{
    private UnauthenticatedRequestPolicy()
    {
    }

    /// <summary>Gets the policy every endpoint declaring no credential is reached through.</summary>
    public static UnauthenticatedRequestPolicy Instance { get; } = new();

    /// <inheritdoc />
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex) =>
        ProcessNext(message, pipeline, currentIndex);

    /// <inheritdoc />
    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex) =>
        ProcessNextAsync(message, pipeline, currentIndex);
}
