// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers the descriptors MailFathom advertises for the pair over one queued send.</summary>
/// <remarks>
/// The two carry the annotation shapes this surface would otherwise never publish together: a plain read, and a write
/// that destroys something without reaching anybody outside this process. Asserting both is what makes the four
/// annotations readable as four separate facts rather than as one safety flag — a client that reads them can tell a
/// send, which is irreversible and open-world, from a withdrawal, which is destructive and closed-world.
/// </remarks>
public sealed class OutgoingEmailToolMetadataTests
{
    [Fact]
    public void AddMailFathomServer_AdvertisesBothToolsUnderTheirProtocolNames()
    {
        // Arrange, Act
        var read = AdvertisedTool(GetOutgoingEmailTool.ToolName);
        var cancel = AdvertisedTool(CancelOutgoingEmailTool.ToolName);

        // Assert
        Assert.Equal("get_outgoing_email", read.Name);
        Assert.Equal("Get outgoing email", read.Title);
        Assert.Equal("cancel_outgoing_email", cancel.Name);
        Assert.Equal("Cancel outgoing email", cancel.Title);
    }

    /// <summary>A read of local state, which is the shape a client may call unattended.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheReadAsReadOnlyIdempotentAndClosedWorld()
    {
        // Arrange, Act
        var annotations = AdvertisedTool(GetOutgoingEmailTool.ToolName).Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    /// <summary>Destructive because it destroys what the caller created, and closed-world because it reaches nobody.</summary>
    /// <remarks>
    /// This is the pair no other tool on this surface publishes, and it is the opposite of a send's on both counts.
    /// Withdrawing reaches no submission server and no recipient — it stops a message from leaving — while destroying
    /// something no further call brings back.
    /// </remarks>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheCancellationAsDestructiveIdempotentAndClosedWorld()
    {
        // Arrange, Act
        var annotations = AdvertisedTool(CancelOutgoingEmailTool.ToolName).Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.False(annotations.ReadOnlyHint);
        Assert.True(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    /// <summary>A model reading the listing must learn that a cancellation cannot reach a message already sent.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesACancellationDescriptionStatingItCannotRecallATransmittedMessage()
    {
        // Arrange, Act
        var description = AdvertisedTool(CancelOutgoingEmailTool.ToolName).Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("CANNOT recall", description, StringComparison.Ordinal);
        Assert.Contains("already been transmitted", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reading back is the alternative to sending again, so the description says so where a model will read it.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesAReadDescriptionOfferingItselfInsteadOfASecondSend()
    {
        // Arrange, Act
        var description = AdvertisedTool(GetOutgoingEmailTool.ToolName).Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("instead of sending again", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no way to list", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The pair is named where a model reads about sending, so it is seen rather than inferred.</summary>
    [Theory]
    [InlineData(SendEmailTool.ToolName)]
    [InlineData(ReplyToEmailTool.ToolName)]
    [InlineData(ForwardEmailTool.ToolName)]
    public void AddMailFathomServer_AdvertisesEverySendingToolNamingTheReadBackAndTheCancellation(string toolName)
    {
        // Arrange, Act
        var description = AdvertisedTool(toolName).Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains(GetOutgoingEmailTool.ToolName, description, StringComparison.Ordinal);
        Assert.Contains(CancelOutgoingEmailTool.ToolName, description, StringComparison.Ordinal);
    }

    /// <summary>Each takes the identifier a send answered with, and nothing that could widen into a listing.</summary>
    [Theory]
    [InlineData(GetOutgoingEmailTool.ToolName)]
    [InlineData(CancelOutgoingEmailTool.ToolName)]
    public void AddMailFathomServer_AdvertisesTheOneRequiredArgumentAndNoOther(string toolName)
    {
        // Arrange, Act
        var inputSchema = AdvertisedTool(toolName).InputSchema;

        // Assert
        var properties = inputSchema.GetProperty("properties");
        Assert.Equal(
            ["outgoingEmailId"],
            properties.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["outgoingEmailId"],
            inputSchema.GetProperty("required").EnumerateArray().Select(entry => entry.GetString()));
    }

    /// <summary>Both answer with the same shape, because both answer the same question about the same record.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheSameAnswerFromBothTools()
    {
        // Arrange, Act
        var read = AdvertisedTool(GetOutgoingEmailTool.ToolName).OutputSchema;
        var cancel = AdvertisedTool(CancelOutgoingEmailTool.ToolName).OutputSchema;

        // Assert
        Assert.NotNull(read);
        Assert.NotNull(cancel);
        Assert.Equal(read.Value.GetRawText(), cancel.Value.GetRawText());
    }

    /// <summary>What a caller may read back is what it supplied, so the answer publishes no part of the message.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesAnAnswerCarryingNothingAboutTheMessageItself()
    {
        // Arrange, Act
        var outputSchema = AdvertisedTool(GetOutgoingEmailTool.ToolName).OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var advertisedSchema = outputSchema.Value.GetRawText();
        Assert.Contains("\"recipients\"", advertisedSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"subject\"", advertisedSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"plainTextBody\"", advertisedSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"htmlBody\"", advertisedSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"rawMime\"", advertisedSchema, StringComparison.Ordinal);
    }

    private static Tool AdvertisedTool(string toolName) => RegisteredMcpToolSurface.AdvertisedTool(toolName);
}
