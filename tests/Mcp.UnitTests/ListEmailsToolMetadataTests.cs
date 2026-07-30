// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Accounts;
using MailMcp.Application.Emails;
using MailMcp.Application.Emails.ListEmails;
using MailMcp.Application.Synchronization;
using MailMcp.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace MailMcp.Mcp.UnitTests;

/// <summary>Covers the descriptor MailMcp advertises for <c>list_emails</c>.</summary>
/// <remarks>
/// The annotations are contract metadata rather than documentation: a client decides whether a tool is safe to call, safe
/// to retry, and confined to local state by reading them before it calls anything. Asserting the advertised descriptor is
/// therefore asserting a promise, and a descriptor that drifts is a broken promise no other test would notice.
/// </remarks>
public sealed class ListEmailsToolMetadataTests
{
    [Fact]
    public void AddMailMcpServer_AdvertisesTheListEmailsToolUnderItsProtocolName()
    {
        // Arrange, Act
        var advertisedTool = AdvertisedListEmailsTool();

        // Assert
        Assert.Equal("list_emails", advertisedTool.Name);
        Assert.Equal("List emails", advertisedTool.Title);
    }

    /// <summary>The four hints the architecture draft requires of every read-only tool.</summary>
    [Fact]
    public void AddMailMcpServer_AdvertisesTheReadOnlyLocalStateAnnotations()
    {
        // Arrange, Act
        var annotations = AdvertisedListEmailsTool().Annotations;

        // Assert
        Assert.NotNull(annotations);
        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    /// <summary>A description is what a model reads to decide whether the tool answers its question at all.</summary>
    [Fact]
    public void AddMailMcpServer_AdvertisesADescriptionStatingTheLocalReadOnlyBoundsOfTheTool()
    {
        // Arrange, Act
        var description = AdvertisedListEmailsTool().Description;

        // Assert
        Assert.NotNull(description);
        Assert.Contains("local", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("100", description, StringComparison.Ordinal);
    }

    [Fact]
    public void AddMailMcpServer_AdvertisesEveryFilterAsAnInputSchemaProperty()
    {
        // Arrange
        string[] expectedProperties =
        [
            "accountIds",
            "folderAliases",
            "senderAddress",
            "recipientAddress",
            "subjectFragment",
            "receivedOnOrAfter",
            "receivedBefore",
            "isRemotelySeen",
            "hasAttachments",
            "direction",
            "pageSize",
            "cursor",
        ];

        // Act
        var advertisedProperties = AdvertisedListEmailsTool()
            .InputSchema
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        // Assert
        Assert.Equal([.. expectedProperties.Order(StringComparer.Ordinal)], [.. advertisedProperties.Order(StringComparer.Ordinal)]);
    }

    /// <summary>An argument nobody can interpret is an argument a model guesses at, so every one carries its own description.</summary>
    [Fact]
    public void AddMailMcpServer_DescribesEveryInputSchemaProperty()
    {
        // Arrange, Act
        var describedProperties = AdvertisedListEmailsTool()
            .InputSchema
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => (property.Name, HasDescription: property.Value.TryGetProperty("description", out var description) && description.GetString()?.Length > 20))
            .ToArray();

        // Assert
        Assert.All(describedProperties, property => Assert.True(property.HasDescription, $"'{property.Name}' carries no usable description."));
    }

    /// <summary>The cancellation token the tool takes is the host's concern and must never become a protocol argument.</summary>
    [Fact]
    public void AddMailMcpServer_DoesNotAdvertiseTheCancellationTokenAsAnArgument()
    {
        // Arrange, Act
        var advertisedProperties = AdvertisedListEmailsTool().InputSchema.GetProperty("properties");

        // Assert
        Assert.False(advertisedProperties.TryGetProperty("cancellationToken", out _));
    }

    /// <summary>An enumeration travels as its name, so a client reads a value rather than this assembly's declaration order.</summary>
    [Fact]
    public void AddMailMcpServer_AdvertisesTheReadingDirectionAsItsNamedValues()
    {
        // Arrange, Act
        var direction = AdvertisedListEmailsTool()
            .InputSchema
            .GetProperty("properties")
            .GetProperty("direction");

        // Assert
        var advertisedValues = direction.GetProperty("enum").EnumerateArray().Select(value => value.ToString()).ToArray();
        Assert.Equal(["newestFirst", "oldestFirst"], advertisedValues);
    }

    [Fact]
    public void AddMailMcpServer_AdvertisesTheResultShapeAsAnOutputSchema()
    {
        // Arrange, Act
        var outputSchema = AdvertisedListEmailsTool().OutputSchema;

        // Assert
        Assert.NotNull(outputSchema);
        var properties = outputSchema.Value.GetProperty("properties");
        Assert.True(properties.TryGetProperty("emails", out _));
        Assert.True(properties.TryGetProperty("nextCursor", out _));
        Assert.True(properties.TryGetProperty("folderFreshness", out _));
    }

    /// <summary>The surface is one tool at this stage, so a second one arriving unnoticed is a change to the published contract.</summary>
    [Fact]
    public void AddMailMcpServer_RegistersOnlyTheListEmailsTool()
    {
        // Arrange, Act
        var registeredTools = RegisteredTools();

        // Assert
        var registeredTool = Assert.Single(registeredTools);
        Assert.Equal(ListEmailsTool.ToolName, registeredTool.ProtocolTool.Name);
    }

    private static Tool AdvertisedListEmailsTool() =>
        RegisteredTools().Single(tool => tool.ProtocolTool.Name == ListEmailsTool.ToolName).ProtocolTool;

    private static IReadOnlyList<McpServerTool> RegisteredTools()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IStoredEmailTimelineReader>(new StubStoredEmailTimelineReader());
        services.AddSingleton<ISynchronizationFreshnessReader>(new StubSynchronizationFreshnessReader());
        services.AddSingleton<IMailAccountCatalog>(new StubMailAccountCatalog("personal"));
        services.AddSingleton<MailboxTimelineReader>();
        services.AddMailMcpServer();

        using var provider = services.BuildServiceProvider();

        return [.. provider.GetServices<McpServerTool>()];
    }
}
