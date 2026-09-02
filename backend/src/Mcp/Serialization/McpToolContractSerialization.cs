// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Mcp.Serialization;

/// <summary>Defines how MailFathom tool arguments and results are read and written on the wire.</summary>
/// <remarks>
/// <para>
/// The options are stated once and handed to every tool registration, because they are part of the published contract
/// rather than a formatting preference: the property names a client sends and the enum spellings it reads are generated
/// from them, into the input and output schemas the descriptor advertises as well as into the payloads.
/// </para>
/// <para>
/// Enumerations travel as their names rather than their ordinals. An ordinal would publish a number whose meaning is
/// this assembly's declaration order, which no client can see and which the repository's enum rules keep stable for
/// persistence rather than for a protocol reader.
/// </para>
/// </remarks>
internal static class McpToolContractSerialization
{
    /// <summary>Gets the serializer options every MailFathom tool is registered with.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        // Web defaults supply the camel-cased property names MCP clients expect, and case-insensitive reading, so an
        // argument spelled by a client that pascal-cases its own models still binds.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        // Frozen once the contract is described, with the default resolver populated, so the options a tool registration
        // captured cannot be mutated later by anything that gets hold of them and cannot start differing between the
        // schema that was advertised and the payload that is serialized.
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }
}
