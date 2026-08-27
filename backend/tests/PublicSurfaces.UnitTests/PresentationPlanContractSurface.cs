// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.PublicSurfaces.UnitTests;

/// <summary>Renders the presentation plan contract — its versions and the JSON every part of it takes — in a form two builds compare byte for byte.</summary>
/// <remarks>
/// <para>
/// The plan is a published contract with a life of its own. A client is updated separately from the deployment that
/// serves it, so it meets plans written by builds it has never seen, and what it can do about that rests entirely on
/// the two numbers this file records beside the shapes: the plan's schema version, and the version of each block type
/// in the catalogue. A block whose shape moved without its version moving is the one change that breaks a client
/// silently, and nothing else in the repository would have shown it.
/// </para>
/// <para>
/// The shapes are exported from the contract's own source-generated serializer rather than described a second time, so
/// this records what a client actually receives. A value with a converter of its own exports as an unconstrained
/// schema — the exporter cannot see through a converter — which is why the two closed enumerations are written out
/// beside it: their identities are part of the contract exactly as a property name is.
/// </para>
/// <para>
/// It is not in the OpenAPI document, because that document is generated from the endpoints the host maps and this
/// contract has no endpoint yet. The route that streams a run is what puts it there; until then this file is where a
/// change to the contract is read.
/// </para>
/// </remarks>
internal static class PresentationPlanContractSurface
{
    /// <summary>Renders the published presentation plan contract.</summary>
    /// <returns>The canonical JSON form of the contract.</returns>
    public static string Render() => CanonicalJson.Render(new JsonObject
    {
        ["schemaVersion"] = PresentationPlan.CurrentSchemaVersion,
        ["blockCatalogue"] = RenderBlockCatalogue(),
        ["factTableColumns"] = RenderFactTableColumns(),
        ["bounds"] = RenderBounds(),
        ["schema"] = PresentationPlanJsonContext.Default.Options.GetJsonSchemaAsNode(typeof(PresentationPlan)),
    });

    private static JsonObject RenderBlockCatalogue() => new(PresentationBlockType.All
        .OrderBy(blockType => blockType.Identity, StringComparer.Ordinal)
        .Select(blockType => KeyValuePair.Create<string, JsonNode?>(blockType.Identity, blockType.Version)));

    private static JsonObject RenderFactTableColumns() => new(FactTableColumn.All
        .OrderBy(column => column.Identity, StringComparer.Ordinal)
        .Select(column => KeyValuePair.Create<string, JsonNode?>(column.Identity, column.ValueKind.ToString())));

    /// <summary>Renders the counts a plan refuses to exceed, which a producer on either side has to hold to.</summary>
    /// <remarks>
    /// A bound is part of the contract rather than an implementation detail: a producer composing more rows than the
    /// contract admits has its plan refused, so lowering one is a break exactly as removing a property is.
    /// </remarks>
    private static JsonObject RenderBounds() => new()
    {
        ["planBlocks"] = PresentationPlan.MaxBlocks,
        ["planCitations"] = PresentationPlan.MaxCitations,
        ["blockCitations"] = PresentationEvidence.MaxCitations,
        ["textLength"] = PresentationText.MaxLength,
        ["citationIdentifierLength"] = PresentationCitationId.MaxLength,
        ["addressOctets"] = EmailAddressJsonConverter.MaxOctets,
        ["evidenceListEntries"] = EvidenceListBlock.MaxEntries,
        ["timelineEntries"] = TimelineBlock.MaxEntries,
        ["factTableColumns"] = FactTableBlock.MaxColumns,
        ["factTableRows"] = FactTableBlock.MaxRows,
        ["peopleEntries"] = PeopleBlock.MaxEntries,
        ["threadStateParticipants"] = ThreadStateBlock.MaxParticipants,
        ["threadStateStatements"] = ThreadStateBlock.MaxStatements,
        ["attachmentGalleryEntries"] = AttachmentGalleryBlock.MaxEntries,
        ["draftRecipients"] = DraftBlock.MaxRecipients,
    };
}
