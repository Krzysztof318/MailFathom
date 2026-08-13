// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Observability;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Observability;

/// <summary>Covers the instruments every tool call is published on, and the bound on what may dimension them.</summary>
public sealed class McpToolCallTelemetryTests
{
    private const string CallsInstrumentName = "mailfathom.mcp.tool.calls";

    private const string DurationInstrumentName = "mailfathom.mcp.tool.call.duration";

    private const string ToolTagName = "mailfathom.mcp.tool";

    private const string OutcomeTagName = "mailfathom.mcp.tool.outcome";

    /// <summary>One call is one count and one duration, under the tool it named and the outcome it reached.</summary>
    [Fact]
    public void RecordCompleted_CallThatAnswered_CountsItAndRecordsItsDurationInSeconds()
    {
        // Arrange
        var telemetry = new McpToolCallTelemetry();
        using var recorded = new RecordedMailFathomMeasurements();

        // Act
        telemetry.RecordCompleted(SearchEmailsTool.ToolName, isError: false, TimeSpan.FromMilliseconds(1500));

        // Assert
        var call = Assert.Single(Measurements(recorded, CallsInstrumentName, SearchEmailsTool.ToolName));
        Assert.Equal(1, call.Value);
        Assert.Equal("succeeded", call.Tags[OutcomeTagName]);

        var duration = Assert.Single(
            Measurements(recorded, DurationInstrumentName, SearchEmailsTool.ToolName),
            measurement => measurement.Value == 1.5);

        Assert.Equal("succeeded", duration.Tags[OutcomeTagName]);
    }

    /// <summary>
    /// A tool's own error result is neither a success nor a failure of the surface, and a dashboard that could not tell
    /// the three apart would read a deployment answering every call with a refusal as a healthy one.
    /// </summary>
    [Theory]
    [InlineData("tool_error")]
    [InlineData("cancelled")]
    [InlineData("protocol_error")]
    [InlineData("refused")]
    [InlineData("failed")]
    public void Record_EachWayACallCanEnd_PublishesItsOwnOutcome(string expectedOutcome)
    {
        // Arrange
        var telemetry = new McpToolCallTelemetry();
        using var recorded = new RecordedMailFathomMeasurements();
        var duration = TimeSpan.FromMilliseconds(20);

        // Act
        switch (expectedOutcome)
        {
            case "tool_error":
                telemetry.RecordCompleted(AskMailTool.ToolName, isError: true, duration);
                break;
            case "cancelled":
                telemetry.RecordCancelled(AskMailTool.ToolName, duration);
                break;
            case "protocol_error":
                telemetry.RecordProtocolFailure(AskMailTool.ToolName, duration);
                break;
            case "refused":
                telemetry.RecordRefused(AskMailTool.ToolName, duration);
                break;
            default:
                telemetry.RecordUnexpectedFailure(AskMailTool.ToolName, duration);
                break;
        }

        // Assert
        Assert.Contains(
            Measurements(recorded, CallsInstrumentName, AskMailTool.ToolName),
            measurement => Equals(measurement.Tags[OutcomeTagName], expectedOutcome));
    }

    /// <summary>
    /// The tool dimension is what a caller could otherwise choose, so anything this surface does not publish is measured
    /// under one fixed name however plausible it looks.
    /// </summary>
    [Theory]
    [InlineData("list_email")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("victim@example.test")]
    public void Record_ToolNameThisSurfaceDoesNotPublish_MeasuresItUnderOnePlaceholder(string? requestedToolName)
    {
        // Arrange
        var telemetry = new McpToolCallTelemetry();
        using var recorded = new RecordedMailFathomMeasurements();

        // Act
        telemetry.RecordUnexpectedFailure(requestedToolName, TimeSpan.FromMilliseconds(5));

        // Assert
        var published = recorded.Read(CallsInstrumentName)
            .Select(measurement => measurement.Tags[ToolTagName])
            .ToArray();

        Assert.Contains("(unpublished)", published);
        Assert.DoesNotContain(requestedToolName, published);
    }

    /// <summary>Every published tool is measured under its own name, so a per-tool regression is visible as one.</summary>
    [Fact]
    public void Record_EveryPublishedTool_MeasuresItUnderTheNameItIsAdvertisedAs()
    {
        // Arrange
        var telemetry = new McpToolCallTelemetry();
        using var recorded = new RecordedMailFathomMeasurements();
        string[] publishedTools =
        [
            ListAccountsTool.ToolName,
            ListEmailsTool.ToolName,
            GetEmailContentTool.ToolName,
            SearchEmailsTool.ToolName,
            AskMailTool.ToolName,
        ];

        // Act
        foreach (var toolName in publishedTools)
        {
            telemetry.RecordCompleted(toolName, isError: false, TimeSpan.FromMilliseconds(1));
        }

        // Assert
        Assert.All(
            publishedTools,
            toolName => Assert.NotEmpty(Measurements(recorded, CallsInstrumentName, toolName)));
    }

    /// <summary>The two dimensions are the whole of what a call may say about itself; a third would carry caller text.</summary>
    [Fact]
    public void Record_AnyCall_PublishesNothingBeyondTheToolAndTheOutcome()
    {
        // Arrange
        var telemetry = new McpToolCallTelemetry();
        using var recorded = new RecordedMailFathomMeasurements();

        // Act
        telemetry.RecordRefused(ListEmailsTool.ToolName, TimeSpan.FromMilliseconds(7));

        // Assert
        Assert.All(
            Measurements(recorded, DurationInstrumentName, ListEmailsTool.ToolName),
            measurement => Assert.Equal([ToolTagName, OutcomeTagName], measurement.Tags.Keys));
    }

    private static IReadOnlyList<RecordedMeasurement> Measurements(
        RecordedMailFathomMeasurements recorded,
        string instrumentName,
        string toolName) =>
    [
        .. recorded.Read(instrumentName)
            .Where(measurement => Equals(measurement.Tags[ToolTagName], toolName)),
    ];
}
