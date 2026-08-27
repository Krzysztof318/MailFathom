// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Presentation.Citations;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Covers the form a plan takes on the wire, which is the half of the contract a client actually meets.</summary>
public sealed class PresentationPlanSerializationTests
{
    private static JsonSerializerOptions Contract => PresentationPlanJsonContext.Default.Options;

    /// <summary>Everything in the catalogue survives the trip, which is what a client on the other end depends on.</summary>
    [Fact]
    public void RoundTrip_APlanHoldingOneBlockOfEveryType_ReadsBackAsTheSameTypes()
    {
        // Arrange
        var plan = PresentationPlanExample.Compose();

        // Act
        var json = JsonSerializer.Serialize(plan, Contract);
        var read = JsonSerializer.Deserialize<PresentationPlan>(json, Contract);

        // Assert
        Assert.NotNull(read);
        Assert.Equal(
            [.. plan.Blocks.Select(block => block.Type)],
            [.. read.Blocks.Select(block => block.Type)]);
        Assert.Equal(plan.SchemaVersion, read.SchemaVersion);
        Assert.Equal(plan.Limitations, read.Limitations);
    }

    /// <summary>What a client keys its renderers by: the discriminator and the version beside it.</summary>
    [Fact]
    public void Serialization_ABlock_WritesItsCatalogueIdentityAndVersion()
    {
        // Arrange
        var plan = PresentationPlanExample.Compose();

        // Act
        var written = JsonNode.Parse(JsonSerializer.Serialize(plan, Contract))!.AsObject();
        var first = written["blocks"]!.AsArray()[0]!.AsObject();

        // Assert
        Assert.Equal(PresentationBlockType.AnswerIdentity, (string?)first["type"]);
        Assert.Equal(PresentationBlockType.Answer.Version, (int?)first["version"]);
    }

    /// <summary>The plan's own revision travels with it, because that is what lets a client refuse one block rather than the run.</summary>
    [Fact]
    public void Serialization_APlan_WritesItsSchemaVersion()
    {
        // Act
        var written = JsonNode.Parse(JsonSerializer.Serialize(PresentationPlanExample.Compose(), Contract))!.AsObject();

        // Assert
        Assert.Equal(PresentationPlan.CurrentSchemaVersion, (int?)written["schemaVersion"]);
    }

    /// <summary>An identity is published in the form the client API already names one by, not as the object the value holds.</summary>
    [Fact]
    public void Serialization_ACitation_WritesItsTargetAsTheIdentitiesTheClientApiPublishes()
    {
        // Act
        var written = JsonNode.Parse(JsonSerializer.Serialize(PresentationPlanExample.Compose(), Contract))!.AsObject();
        var fragment = written["citations"]!.AsArray()[1]!.AsObject()["target"]!.AsObject();

        // Assert
        Assert.Equal(FragmentCitationTarget.Kind, (string?)fragment["kind"]);
        Assert.Equal("22222222-2222-2222-2222-222222222222", (string?)fragment["email"]);
        Assert.Equal("33333333-3333-3333-3333-333333333333", (string?)fragment["fragment"]);
    }

    /// <summary>An ordinal would change meaning the first time one of these sets were reordered.</summary>
    [Fact]
    public void Serialization_AValueFromAClosedSet_WritesItsNameRatherThanItsOrdinal()
    {
        // Act
        var written = JsonNode.Parse(JsonSerializer.Serialize(PresentationPlanExample.Compose(), Contract))!.AsObject();
        var answer = written["blocks"]!.AsArray()[0]!.AsObject();
        var table = written["blocks"]!.AsArray()[3]!.AsObject();

        // Assert
        Assert.Equal("High", (string?)answer["confidence"]);
        Assert.Equal("Supported", (string?)answer["evidence"]!["support"]);
        Assert.Equal("RetrievalTruncated", (string?)written["limitations"]!.AsArray()[0]);
        Assert.Equal(FactTableColumn.Party.Identity, (string?)table["columns"]!.AsArray()[0]);
    }

    /// <summary>An address is published as the message wrote it; the comparison form is an internal key.</summary>
    [Fact]
    public void Serialization_AnAddress_WritesTheAddressAloneRatherThanItsComparisonForm()
    {
        // Act
        var written = JsonNode.Parse(JsonSerializer.Serialize(PresentationPlanExample.Compose(), Contract))!.AsObject();
        var person = written["blocks"]!.AsArray()[4]!.AsObject()["entries"]!.AsArray()[0]!.AsObject();

        // Assert
        Assert.Equal("ada@northwind.example", (string?)person["address"]);
    }

    /// <summary>The contract holds for a plan arriving from a producer this deployment does not control, or it holds nowhere.</summary>
    [Fact]
    public void Deserialization_APlanWhoseTextIsMarkup_IsRefused()
    {
        // Arrange
        var written = JsonNode.Parse(JsonSerializer.Serialize(PresentationPlanExample.Compose(), Contract))!.AsObject();
        written["blocks"]!.AsArray()[0]!.AsObject()["text"] = "<Grid><TextBlock Text=\"owned\" /></Grid>";

        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PresentationPlan>(written.ToJsonString(), Contract));
    }

    /// <summary>A block type nothing declares is a service ahead of this build, and it is refused rather than guessed at.</summary>
    [Fact]
    public void Deserialization_ABlockTypeTheCatalogueDoesNotHold_IsRefused()
    {
        // Arrange
        var written = JsonNode.Parse(JsonSerializer.Serialize(PresentationPlanExample.Compose(), Contract))!.AsObject();
        written["blocks"]!.AsArray()[0]!.AsObject()["type"] = "chart";

        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PresentationPlan>(written.ToJsonString(), Contract));
    }

    /// <summary>An address the message could not have written is not one this contract carries.</summary>
    [Theory]
    [InlineData("\"not-an-address\"")]
    [InlineData("7")]
    public void Deserialization_AnAddressThatNamesNoMailbox_IsRefused(string address)
    {
        // Arrange
        var written = JsonNode.Parse(JsonSerializer.Serialize(PresentationPlanExample.Compose(), Contract))!.AsObject();
        written["blocks"]!.AsArray()[4]!.AsObject()["entries"]!.AsArray()[0]!.AsObject()["address"] =
            JsonNode.Parse(address);

        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PresentationPlan>(written.ToJsonString(), Contract));
    }

    /// <summary>The structural rules are the constructor's, and deserialization goes through it.</summary>
    [Fact]
    public void Deserialization_APlanNamingACitationItDoesNotDeclare_IsRefused()
    {
        // Arrange
        var written = JsonNode.Parse(JsonSerializer.Serialize(PresentationPlanExample.Compose(), Contract))!.AsObject();
        written["citations"] = new JsonArray();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<PresentationPlan>(written.ToJsonString(), Contract));
    }

    /// <summary>A revision this build does not implement carries members it would drop, so the block is refused rather than read as this one.</summary>
    [Fact]
    public void Deserialization_ABlockClaimingARevisionThisBuildDoesNotImplement_IsRefused()
    {
        // Arrange
        var written = JsonNode.Parse(JsonSerializer.Serialize(PresentationPlanExample.Compose(), Contract))!.AsObject();
        written["blocks"]!.AsArray()[0]!.AsObject()["version"] = PresentationBlockType.Answer.Version + 1;

        // Act, Assert
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<PresentationPlan>(written.ToJsonString(), Contract));
    }

    /// <summary>The revision this build does implement is the one it wrote, so reading a plan it produced states it rather than omitting it.</summary>
    [Fact]
    public void Deserialization_ABlockClaimingTheRevisionThisBuildImplements_IsRead()
    {
        // Arrange
        var written = JsonSerializer.Serialize(PresentationPlanExample.Compose(), Contract);

        // Act
        var read = JsonSerializer.Deserialize<PresentationPlan>(written, Contract);

        // Assert
        Assert.NotNull(read);
        Assert.Equal(PresentationBlockType.Answer.Version, read.Blocks[0].Version);
    }
}
