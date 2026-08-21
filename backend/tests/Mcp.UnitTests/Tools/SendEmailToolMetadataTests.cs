// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers the descriptor MailFathom advertises for <c>send_email</c>.</summary>
/// <remarks>
/// It is the one tool whose effect reaches somebody who is not this mailbox's owner and cannot be recalled, so the
/// descriptor is most of what makes the tool safe: a client decides whether to ask a person from the annotations, and a
/// model decides whether to call at all from the description. Both are asserted here because both are the published
/// contract rather than prose.
/// </remarks>
public sealed class SendEmailToolMetadataTests
{
    [Fact]
    public void AddMailFathomServer_AdvertisesTheSendEmailToolUnderItsProtocolName()
    {
        // Arrange, Act
        var advertisedTool = AdvertisedSendEmailTool();

        // Assert
        Assert.Equal("send_email", advertisedTool.Name);
        Assert.Equal("Send email", advertisedTool.Title);
    }

    /// <summary>A writing tool that reaches a server this deployment does not own, is safe to retry, and cannot be undone.</summary>
    /// <remarks>
    /// <para>
    /// <c>destructiveHint</c> is <see langword="true" /> for irreversibility rather than for destruction, which is the
    /// second ground ADR 0013 adds beside the one <c>set_mail_flags</c> established. Sending is literally additive and
    /// a literal reading would give <see langword="false" />, which would place this tool in the same class as
    /// <c>create_contact</c> — one call to undo.
    /// </para>
    /// <para>
    /// <c>idempotentHint</c> is <see langword="true" /> because the idempotency key is required, which is the one
    /// condition that record permits the value under. It would be a lie about the tool if the key were optional, since
    /// an annotation describes the tool as it may be called rather than as a careful caller would call it.
    /// </para>
    /// </remarks>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheWritingIrreversibleIdempotentOpenWorldAnnotations()
    {
        // Arrange, Act
        var annotations = AdvertisedSendEmailTool().Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.False(annotations.ReadOnlyHint);
        Assert.True(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.True(annotations.OpenWorldHint);
    }

    /// <summary>The description is a safety surface, because it is what a model reads before deciding to call.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingThatRealMailLeavesAndCannotBeRecalled()
    {
        // Arrange, Act
        var description = AdvertisedSendEmailTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("real email", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be recalled", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A caller told the mail is gone will not look for the window in which it is not, so the wording says queued.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingThatTheCallQueuesRatherThanSends()
    {
        // Arrange, Act
        var description = AdvertisedSendEmailTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("The call itself transmits nothing", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("the result says queued", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What the tool will not do is advertised rather than discovered, because discovering it costs a wrong send.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingWhatTheToolWillNotDo()
    {
        // Arrange, Act
        var description = AdvertisedSendEmailTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("will not attach files", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("will not reply to or forward", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("will not schedule a send for later", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("will not send to a mailing list", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The retry contract is the one thing a model has to get right, so the description states both halves of it.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingWhatTheIdempotencyKeyDecides()
    {
        // Arrange, Act
        var description = AdvertisedSendEmailTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("idempotencyKey is required", description, StringComparison.Ordinal);
        Assert.Contains("a new value is a new message", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The sending address belongs to the account's configuration, and the descriptor says so where a caller would look for it.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesADescriptionStatingThatTheSendingAddressIsNotAnArgument()
    {
        // Arrange, Act
        var description = AdvertisedSendEmailTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("The From address is not an argument", description, StringComparison.Ordinal);
    }

    /// <summary>The fields of a message and nothing else, which is what keeps a sending address out of the contract.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheFieldsOfAMessageAsInputSchemaProperties()
    {
        // Arrange
        string[] expectedProperties =
            ["account", "to", "cc", "bcc", "subject", "plainTextBody", "htmlBody", "idempotencyKey"];

        // Act
        var advertisedProperties = AdvertisedSendEmailTool()
            .InputSchema
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        // Assert
        Assert.Equal(
            [.. expectedProperties.Order(StringComparer.Ordinal)],
            [.. advertisedProperties.Order(StringComparer.Ordinal)]);
    }

    /// <summary>No argument names who the message is from, which is the guarantee the whole sending path is built around.</summary>
    [Theory]
    [InlineData("from")]
    [InlineData("fromAddress")]
    [InlineData("sender")]
    [InlineData("replyTo")]
    [InlineData("attachments")]
    public void AddMailFathomServer_AdvertisesNoArgumentNamingASenderOrAnAttachment(string absentProperty)
    {
        // Arrange, Act
        var advertisedProperties = AdvertisedSendEmailTool().InputSchema.GetProperty("properties");

        // Assert
        Assert.False(advertisedProperties.TryGetProperty(absentProperty, out _));
    }

    /// <summary>An argument nobody can interpret is an argument a model guesses at, so every one carries its own description.</summary>
    [Fact]
    public void AddMailFathomServer_DescribesEveryInputSchemaProperty()
    {
        // Arrange, Act
        var describedProperties = AdvertisedSendEmailTool()
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

    /// <summary>The key is required in the schema, which is what makes the idempotent annotation true of the tool rather than of a careful caller.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheIdempotencyKeyAmongTheRequiredArguments()
    {
        // Arrange, Act
        var requiredProperties = AdvertisedSendEmailTool()
            .InputSchema
            .GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();

        // Assert
        Assert.Equal(
            ["account", "idempotencyKey", "plainTextBody", "subject", "to"],
            [.. requiredProperties.Order(StringComparer.Ordinal)]);
    }

    /// <summary>Copying somebody is optional, and a client reads that from the schema rather than from the prose.</summary>
    [Theory]
    [InlineData("cc")]
    [InlineData("bcc")]
    public void AddMailFathomServer_AdvertisesTheCopiedHeadersAsOptionalListsOfAddresses(string header)
    {
        // Arrange, Act
        var advertised = AdvertisedSendEmailTool().InputSchema.GetProperty("properties").GetProperty(header);

        // Assert
        Assert.Equal(["array", "null"], TypesOf(advertised));
        Assert.Contains("string", TypesOf(advertised.GetProperty("items")), StringComparer.Ordinal);
    }

    /// <summary>The cancellation token the tool takes is the host's concern and must never become a protocol argument.</summary>
    [Fact]
    public void AddMailFathomServer_DoesNotAdvertiseTheCancellationTokenAsAnArgument()
    {
        // Arrange, Act
        var advertisedProperties = AdvertisedSendEmailTool().InputSchema.GetProperty("properties");

        // Assert
        Assert.False(advertisedProperties.TryGetProperty("cancellationToken", out _));
    }

    /// <summary>What a caller holds afterwards is the record, so its identity and its state are part of the contract.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheQueuedRecordAsAnOutputSchema()
    {
        // Arrange, Act
        var outputSchema = AdvertisedSendEmailTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var advertisedSchema = outputSchema.Value.GetRawText();
        Assert.Contains("outgoingEmailId", advertisedSchema, StringComparison.Ordinal);
        Assert.Contains("recipientCount", advertisedSchema, StringComparison.Ordinal);
        Assert.Contains("queuedAt", advertisedSchema, StringComparison.Ordinal);
    }

    /// <summary>The states are a closed set, and <c>queued</c> among them is what stops a caller reporting a delivery.</summary>
    [Fact]
    public void AddMailFathomServer_AdvertisesTheQueuedStateUnderItsPublishedSpelling()
    {
        // Arrange, Act
        var outputSchema = AdvertisedSendEmailTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var advertisedSchema = outputSchema.Value.GetRawText();
        Assert.Contains("\"queued\"", advertisedSchema, StringComparison.Ordinal);
        Assert.Contains("\"sending\"", advertisedSchema, StringComparison.Ordinal);
        Assert.Contains("\"cancelled\"", advertisedSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"recorded\"", advertisedSchema, StringComparison.Ordinal);
    }

    /// <summary>Reads the types a property admits, which an argument a caller may omit states as a list rather than as one name.</summary>
    private static string[] TypesOf(JsonElement property)
    {
        var type = property.GetProperty("type");

        return type.ValueKind is JsonValueKind.Array
            ? [.. type.EnumerateArray().Select(value => value.GetString() ?? string.Empty)]
            : [type.GetString() ?? string.Empty];
    }

    private static Tool AdvertisedSendEmailTool() =>
        RegisteredMcpToolSurface.AdvertisedTool(SendEmailTool.ToolName);
}
