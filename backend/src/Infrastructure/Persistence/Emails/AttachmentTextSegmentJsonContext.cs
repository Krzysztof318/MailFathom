// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>The shape a stored attachment's page boundaries are written and read through, generated rather than discovered.</summary>
/// <remarks>
/// <para>
/// Source-generated for the reason a stored job payload is: this is a document written into the database, and a
/// reflection-based serializer would make what it holds whatever the type happens to carry at the time rather than the
/// one shape stated here. A member added to a segment later is then a deliberate change to what is stored.
/// </para>
/// <para>
/// The kind is written as its name rather than its number, so a row stays readable in an audit query and survives any
/// later reordering of the enumeration — the same choice the search document makes for the source its text came from.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    Converters = [typeof(JsonStringEnumConverter<AttachmentTextSegmentKind>)])]
[JsonSerializable(typeof(IReadOnlyList<AttachmentTextSegment>))]
internal sealed partial class AttachmentTextSegmentJsonContext : JsonSerializerContext;
