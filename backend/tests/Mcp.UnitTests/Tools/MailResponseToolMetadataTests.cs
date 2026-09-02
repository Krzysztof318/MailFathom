// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers the descriptors MailFathom advertises for <c>reply_to_email</c> and <c>forward_email</c>.</summary>
/// <remarks>
/// Both queue real mail to somebody who is not this mailbox's owner, so the descriptor is most of what makes each one
/// safe: a client decides whether to ask a person from the annotations, and a model decides whether to call at all from
/// the description. They are covered together because what makes them a family is that they take an anchor into stored
/// mail and derive from it — so the assertions that matter are the ones about what is *not* an argument, and those are
/// the same claim twice.
/// </remarks>
public sealed class MailResponseToolMetadataTests
{
    [Theory]
    [InlineData("reply_to_email", "Reply to email")]
    [InlineData("forward_email", "Forward email")]
    public void AddMailFathomServer_AdvertisesEachResponseToolUnderItsProtocolName(string toolName, string title)
    {
        // Arrange, Act
        var advertisedTool = RegisteredMcpToolSurface.AdvertisedTool(toolName);

        // Assert
        Assert.Equal(toolName, advertisedTool.Name);
        Assert.Equal(title, advertisedTool.Title);
    }

    /// <summary>The values are <c>send_email</c>'s, because what these tools do is what it does with an anchor added.</summary>
    /// <remarks>
    /// <c>destructiveHint</c> is <see langword="true" /> for irreversibility rather than destruction, and
    /// <c>idempotentHint</c> is <see langword="true" /> because the key is required rather than optional — the same two
    /// grounds ADR 0013 settled for the tool these follow.
    /// </remarks>
    [Theory]
    [InlineData("reply_to_email")]
    [InlineData("forward_email")]
    public void AddMailFathomServer_AdvertisesTheSameAnnotationsSendEmailCarries(string toolName)
    {
        // Arrange
        var sending = RegisteredMcpToolSurface.AdvertisedTool(SendEmailTool.ToolName).Annotations;

        // Act
        var annotations = RegisteredMcpToolSurface.AdvertisedTool(toolName).Annotations;

        // Assert
        Assert.NotNull(sending);
        Assert.NotNull(annotations);
        Assert.Equal(sending.ReadOnlyHint, annotations.ReadOnlyHint);
        Assert.Equal(sending.DestructiveHint, annotations.DestructiveHint);
        Assert.Equal(sending.IdempotentHint, annotations.IdempotentHint);
        Assert.Equal(sending.OpenWorldHint, annotations.OpenWorldHint);
        Assert.False(annotations.ReadOnlyHint);
        Assert.True(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.True(annotations.OpenWorldHint);
    }

    /// <summary>The description is a safety surface, because it is what a model reads before deciding to call.</summary>
    [Theory]
    [InlineData("reply_to_email")]
    [InlineData("forward_email")]
    public void AddMailFathomServer_AdvertisesADescriptionStatingThatRealMailLeavesAndCannotBeRecalled(string toolName)
    {
        // Arrange, Act
        var description = RegisteredMcpToolSurface.AdvertisedTool(toolName).Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("real email", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CANNOT be recalled", description, StringComparison.Ordinal);
    }

    /// <summary>A caller told the mail is gone will not look for the window in which it is not, so the wording says queued.</summary>
    [Theory]
    [InlineData("reply_to_email")]
    [InlineData("forward_email")]
    public void AddMailFathomServer_AdvertisesADescriptionStatingThatTheCallQueuesRatherThanSends(string toolName)
    {
        // Arrange, Act
        var description = RegisteredMcpToolSurface.AdvertisedTool(toolName).Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("The call itself transmits nothing", description, StringComparison.Ordinal);
        Assert.Contains("the result says queued", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal a caller meets for an email it may not answer is the same in every case, and the description says so
    /// — otherwise a model reads a repeated refusal as a transient failure and tries the next identifier.
    /// </summary>
    [Theory]
    [InlineData("reply_to_email")]
    [InlineData("forward_email")]
    public void AddMailFathomServer_AdvertisesADescriptionStatingThatEveryUnanswerableEmailIsRefusedAlike(
        string toolName)
    {
        // Arrange, Act
        var description = RegisteredMcpToolSurface.AdvertisedTool(toolName).Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("refused the same way in every case", description, StringComparison.Ordinal);
        Assert.Contains("never tells you which", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Who receives a reply is the argument a model cannot be allowed to omit, so the schema requires it and the
    /// description spells both values out.
    /// </summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheReplyAudienceAsARequiredChoiceBetweenTwoNamedValues()
    {
        // Arrange
        var advertised = RegisteredMcpToolSurface.AdvertisedTool(ReplyToEmailTool.ToolName);

        // Act
        var audience = advertised.InputSchema.GetProperty("properties").GetProperty("audience");

        // Assert
        Assert.Contains("audience", RequiredArgumentsOf(advertised), StringComparer.Ordinal);
        Assert.Equal(
            ["senderOnly", "everyone"],
            audience.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.DoesNotContain("default", audience.EnumerateObject().Select(property => property.Name));
    }

    /// <summary>The difference the audience makes is who receives the message, and the descriptor says it in words.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesAnAudienceDescriptionNamingWhoEachValueReaches()
    {
        // Arrange, Act
        var audience = RegisteredMcpToolSurface
            .AdvertisedTool(ReplyToEmailTool.ToolName)
            .InputSchema
            .GetProperty("properties")
            .GetProperty("audience")
            .GetProperty("description")
            .GetString();

        // Assert
        Assert.NotNull(audience);
        Assert.Contains("senderOnly addresses whoever asked for answers", audience, StringComparison.Ordinal);
        Assert.Contains("everyone also addresses everybody", audience, StringComparison.Ordinal);
        Assert.Contains("cannot be corrected", audience, StringComparison.Ordinal);
    }

    /// <summary>What a caller may write and nothing more, which is what keeps the anchor doing the deriving.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheAnchorAndWhatTheCallerWroteAsTheReplyArguments()
    {
        // Arrange
        string[] expectedProperties =
            ["storedEmailId", "audience", "plainTextBody", "htmlBody", "cc", "idempotencyKey"];

        // Act
        var advertisedProperties = ArgumentNamesOf(RegisteredMcpToolSurface.AdvertisedTool(ReplyToEmailTool.ToolName));

        // Assert
        Assert.Equal(
            [.. expectedProperties.Order(StringComparer.Ordinal)],
            [.. advertisedProperties.Order(StringComparer.Ordinal)]);
    }

    /// <summary>A forward addresses nobody of its own, so it takes the three headers a caller fills and the anchor.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheAnchorAndTheRecipientsAsTheForwardArguments()
    {
        // Arrange
        string[] expectedProperties =
            ["storedEmailId", "to", "cc", "bcc", "plainTextBody", "htmlBody", "idempotencyKey"];

        // Act
        var advertisedProperties = ArgumentNamesOf(RegisteredMcpToolSurface.AdvertisedTool(ForwardEmailTool.ToolName));

        // Assert
        Assert.Equal(
            [.. expectedProperties.Order(StringComparer.Ordinal)],
            [.. advertisedProperties.Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Everything derived from the answered email is absent from the contract, which is the guarantee these two tools
    /// exist for: a model that could state a threading header would state a wrong one.
    /// </summary>
    [Theory]
    [InlineData("reply_to_email", "subject")]
    [InlineData("reply_to_email", "inReplyTo")]
    [InlineData("reply_to_email", "references")]
    [InlineData("reply_to_email", "quotedText")]
    [InlineData("reply_to_email", "from")]
    [InlineData("reply_to_email", "account")]
    [InlineData("reply_to_email", "attachments")]
    [InlineData("reply_to_email", "to")]
    [InlineData("reply_to_email", "bcc")]
    [InlineData("forward_email", "subject")]
    [InlineData("forward_email", "inReplyTo")]
    [InlineData("forward_email", "references")]
    [InlineData("forward_email", "quotedText")]
    [InlineData("forward_email", "from")]
    [InlineData("forward_email", "account")]
    [InlineData("forward_email", "attachments")]
    public void AddMailFathomServer_AdvertisesNoArgumentForAnythingTheAnsweredEmailDecides(
        string toolName,
        string absentProperty)
    {
        // Arrange, Act
        var advertisedProperties = RegisteredMcpToolSurface
            .AdvertisedTool(toolName)
            .InputSchema
            .GetProperty("properties");

        // Assert
        Assert.False(advertisedProperties.TryGetProperty(absentProperty, out _));
    }

    /// <summary>The key is required in the schema, which is what makes the idempotent annotation true of the tool.</summary>
    [Theory]
    [InlineData("reply_to_email")]
    [InlineData("forward_email")]
    public void AddMailFathomServer_AdvertisesTheAnchorTheBodyAndTheKeyAmongTheRequiredArguments(string toolName)
    {
        // Arrange, Act
        var requiredProperties = RequiredArgumentsOf(RegisteredMcpToolSurface.AdvertisedTool(toolName));

        // Assert
        Assert.Contains("storedEmailId", requiredProperties, StringComparer.Ordinal);
        Assert.Contains("plainTextBody", requiredProperties, StringComparer.Ordinal);
        Assert.Contains("idempotencyKey", requiredProperties, StringComparer.Ordinal);
    }

    /// <summary>A forward goes only where the caller sends it, so naming somebody is not optional.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheForwardRecipientsAmongTheRequiredArguments()
    {
        // Arrange, Act
        var requiredProperties = RequiredArgumentsOf(RegisteredMcpToolSurface.AdvertisedTool(ForwardEmailTool.ToolName));

        // Assert
        Assert.Contains("to", requiredProperties, StringComparer.Ordinal);
    }

    /// <summary>An argument nobody can interpret is an argument a model guesses at, so every one carries its own description.</summary>
    [Theory]
    [InlineData("reply_to_email")]
    [InlineData("forward_email")]
    public void AddMailFathomServer_DescribesEveryInputSchemaProperty(string toolName)
    {
        // Arrange, Act
        var describedProperties = RegisteredMcpToolSurface
            .AdvertisedTool(toolName)
            .InputSchema
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => (
                property.Name,
                HasDescription: property.Value.TryGetProperty("description", out var description)
                    && description.GetString()?.Length > 20))
            .ToArray();

        // Assert
        Assert.All(
            describedProperties,
            property => Assert.True(property.HasDescription, $"'{property.Name}' carries no usable description."));
    }

    /// <summary>The cancellation token the tool takes is the host's concern and must never become a protocol argument.</summary>
    [Theory]
    [InlineData("reply_to_email")]
    [InlineData("forward_email")]
    public void AddMailFathomServer_DoesNotAdvertiseTheCancellationTokenAsAnArgument(string toolName)
    {
        // Arrange, Act
        var advertisedProperties = RegisteredMcpToolSurface
            .AdvertisedTool(toolName)
            .InputSchema
            .GetProperty("properties");

        // Assert
        Assert.False(advertisedProperties.TryGetProperty("cancellationToken", out _));
    }

    /// <summary>What a caller holds afterwards is the record <c>send_email</c> answers with, in the same shape.</summary>
    [Theory]
    [InlineData("reply_to_email")]
    [InlineData("forward_email")]
    public void AddMailFathomServer_AdvertisesTheSameQueuedRecordSendEmailAnswersWith(string toolName)
    {
        // Arrange
        var sending = RegisteredMcpToolSurface.AdvertisedTool(SendEmailTool.ToolName).OutputSchema;

        // Act
        var outputSchema = RegisteredMcpToolSurface.AdvertisedTool(toolName).OutputSchema;

        // Assert
        Assert.NotNull(sending);
        Assert.NotNull(outputSchema);
        Assert.Equal(sending.Value.GetRawText(), outputSchema.Value.GetRawText());
        Assert.Contains("\"queued\"", outputSchema.Value.GetRawText(), StringComparison.Ordinal);
    }

    private static string[] ArgumentNamesOf(Tool advertisedTool) =>
    [
        .. advertisedTool
            .InputSchema
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name),
    ];

    private static string[] RequiredArgumentsOf(Tool advertisedTool) =>
    [
        .. advertisedTool
            .InputSchema
            .GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty),
    ];
}
