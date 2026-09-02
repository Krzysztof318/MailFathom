// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MailFathom.PublicSurfaces.UnitTests;

/// <summary>Renders every tool descriptor the MailFathom registration publishes, in a form two builds compare byte for byte.</summary>
/// <remarks>
/// <para>
/// The descriptors are read from the registration rather than from the tool classes, because the name a client calls, the
/// prose a model reads, and the schema an argument binds against are produced by the registration together — from the
/// attributes, the parameter types, and the serializer options the tools were registered with. A rendering built from
/// the classes would restate that composition instead of recording it.
/// </para>
/// <para>
/// The whole descriptor is serialized rather than the three fields the acceptance names, so nothing published to a
/// client can move without appearing here: a title, an annotation, an output schema, and the descriptor metadata are
/// each part of what a client is told. What makes the result comparable is <see cref="CanonicalJson" /> and the
/// ordering by name applied here, because neither reflection nor the schema generator promises a stable ordering of
/// its own.
/// </para>
/// </remarks>
internal static class McpToolContractSurface
{
    /// <summary>Renders the published tool contract.</summary>
    /// <returns>The canonical JSON form of every registered tool descriptor.</returns>
    public static string Render()
    {
        // The registration alone, with none of the application ports behind it: a descriptor is fixed when the tool is
        // registered, and nothing here calls one. What a particular caller is then listed is decided per request by the
        // filters, which Mcp.UnitTests holds against a composed surface.
        using var provider = new ServiceCollection()
            .AddMailFathomServer()
            .Services
            .BuildServiceProvider();

        var descriptors = new JsonArray(
        [
            .. provider.GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool)
                .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                .Select(tool => JsonSerializer.SerializeToNode(tool, McpJsonUtilities.DefaultOptions)),
        ]);

        return CanonicalJson.Render(descriptors);
    }
}
