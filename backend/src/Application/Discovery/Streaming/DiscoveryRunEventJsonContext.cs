// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>The one shape a run's event is written into its journal row as, with the readers and writers generated at compile time.</summary>
/// <remarks>
/// <para>
/// A run's events are rows now rather than objects in one process, so the payload column is a stored form that outlives
/// the process that wrote it — and a rolling upgrade has one build writing it while another reads it. That makes it a
/// contract rather than an implementation detail, which is why it is declared here instead of being left to whatever
/// options the store happened to have: a property renamed on one side and not the other would be a block that reads
/// back empty rather than a compilation failure.
/// </para>
/// <para>
/// Source-generated for the reason
/// <see cref="PresentationPlanJsonContext" /> is, in the same mode and with the same three converters, so a block or a
/// citation is written here exactly as it is written there. The mode is metadata because the block catalogue, the
/// citation targets, and the events themselves are all polymorphic and the fast path carries no discriminator.
/// </para>
/// <para>
/// It is not what a client is handed. The endpoint serializes the events it read with the surface's own options, so
/// this shape is between the run and its rows and changing it changes no wire form.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [
        typeof(StoredEmailIdJsonConverter),
        typeof(EmailChunkIdJsonConverter),
        typeof(EmailAddressJsonConverter),
    ])]
[JsonSerializable(typeof(DiscoveryRunEvent))]
public sealed partial class DiscoveryRunEventJsonContext : JsonSerializerContext;
