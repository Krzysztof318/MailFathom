// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ClientModel.Primitives;
using MailFathom.AI.Providers;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Writes the headers an endpoint declared onto every request sent through it.</summary>
/// <remarks>
/// <para>
/// The shape an OpenAI-compatible gateway asks for when one address fronts several models: a tenant, a project, or a
/// routing key that has to travel beside the bearer credential rather than inside it. The client library writes the
/// credential and nothing else, so this is where a declared header reaches the request.
/// </para>
/// <para>
/// Positioned per call rather than per attempt, because the values are resolved once for the request and do not change
/// between retries of it, and written with <c>Set</c> so a repeated attempt carries one value rather than appending a
/// second. What it may not write is decided before it is built: startup refuses a declaration naming a header the
/// client construction owns, so this never silently replaces an authorization the credential wrote.
/// </para>
/// <para>
/// Built per request and never cached, because the values come from the credential that was resolved for that request
/// and are released with it.
/// </para>
/// </remarks>
internal sealed class DeclaredRequestHeadersPolicy(IReadOnlyList<ProviderEndpointHeader> headers) : PipelinePolicy
{
    /// <inheritdoc />
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        this.Write(message);

        ProcessNext(message, pipeline, currentIndex);
    }

    /// <inheritdoc />
    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        this.Write(message);

        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    private void Write(PipelineMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        foreach (var header in headers)
        {
            message.Request.Headers.Set(header.Name, header.Value);
        }
    }
}
