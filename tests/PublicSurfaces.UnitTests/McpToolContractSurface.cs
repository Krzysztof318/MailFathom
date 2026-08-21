// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Encodings.Web;
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
/// each part of what a client is told. What makes the result comparable is the canonical form below — object keys in
/// ordinal order and the tools themselves in ordinal order by name — because neither reflection nor the schema
/// generator promises a stable ordering of its own.
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
                .Select(tool => Canonical(JsonSerializer.SerializeToNode(tool, McpJsonUtilities.DefaultOptions))),
        ]);

        return descriptors.ToJsonString(RenderingOptions);
    }

    /// <summary>How the record is written: indented so a diff is line by line, and unescaped so the prose stays readable.</summary>
    /// <remarks>
    /// The relaxed encoder is safe here for the reason it is unsafe on a response: this string is written to a file the
    /// repository holds and is never served to a client or embedded in a document. Escaping every apostrophe would make
    /// a description unreadable in the one place it exists to be read.
    /// </remarks>
    private static JsonSerializerOptions RenderingOptions { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Rewrites a node with every object's keys in ordinal order, so two renderings differ only where the contract does.</summary>
    /// <remarks>
    /// Array order is left alone. A schema's <c>required</c> list and an enumeration's values are sequences the
    /// generator produced from declaration order, which is stable, and sorting them would hide a reordering that a
    /// client reading positionally would see.
    /// </remarks>
    private static JsonNode? Canonical(JsonNode? node) => node switch
    {
        JsonObject properties => new JsonObject(properties
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(property.Key, Canonical(property.Value?.DeepClone())))),
        JsonArray items => new JsonArray([.. items.Select(item => Canonical(item?.DeepClone()))]),
        _ => node?.DeepClone(),
    };
}
