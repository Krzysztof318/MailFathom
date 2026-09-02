// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>The one serialization path a presentation plan travels, with the readers and writers generated at compile time.</summary>
/// <remarks>
/// <para>
/// Source-generated rather than reflection-based, because the client at the other end publishes trimmed: a
/// reflection-based reader is removed by the trimmer rather than reported, so the failure would arrive as a screen that
/// works in a debug build and throws in the published one. Declaring the contract's own path here means both ends read
/// and write one shape, and that this file is where a type joining the contract has to be named.
/// </para>
/// <para>
/// The mode is metadata rather than the default, because the block catalogue and the citation targets are polymorphic
/// and the fast path does not carry a type discriminator. Enums are written as their names for the same reason the two
/// closed enumerations publish an identity: an ordinal means nothing outside this assembly and changes meaning the
/// first time a set is reordered.
/// </para>
/// <para>
/// The three converters are registered here rather than on the values they convert. A stored email, a passage, and an
/// address are identities this contract publishes in a particular form; how they are written elsewhere is not this
/// contract's decision to take on their behalf.
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
[JsonSerializable(typeof(PresentationPlan))]
public sealed partial class PresentationPlanJsonContext : JsonSerializerContext;
