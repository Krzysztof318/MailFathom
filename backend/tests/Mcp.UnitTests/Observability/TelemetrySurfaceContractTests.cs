// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Mcp.Observability;
using MailFathom.Mcp.Tools;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Observability;

/// <summary>Asserts the redaction contract over what this boundary publishes, which is where a caller's text arrives.</summary>
/// <remarks>
/// <para>
/// The same contract runs over every assembly that publishes a signal, and this is the one where the poison is real
/// rather than representative. A tool call is the only place in this process where text a stranger wrote reaches a
/// publisher directly: the name a caller sends is a string of their choosing, and a dimension taking it would let a
/// client minting names in a loop mint a time series apiece that never goes away.
/// </para>
/// <para>
/// So the drive calls every recording method with a tool name that is a sentinel and asserts that none of it survived.
/// What should come out instead is the one fixed placeholder this surface measures an unpublished name under, and the
/// control below reads it back so the absence above is an absence that would have been visible.
/// </para>
/// </remarks>
[Collection(TelemetrySurfaceCollectionDefinition.Name)]
public sealed class TelemetrySurfaceContractTests
{
    private static readonly McpToolCallTelemetry ToolCalls = new();

    /// <summary>Every publisher this suite drives, which the discovery test holds the assembly against.</summary>
    private static readonly Type[] DrivenPublishers = [typeof(McpToolCallTelemetry)];

    /// <summary>The drive really emits the surface, and the caller's name really reaches the publisher.</summary>
    /// <remarks>
    /// The control for every absence below. It reads back the placeholder the name was reduced to, which is what says
    /// the name arrived and was refused rather than that the drive published nothing.
    /// </remarks>
    [Fact]
    public void EmittedSurface_EveryPublisherDriven_MeasuresTheCallUnderThePlaceholderInstead()
    {
        // Arrange
        using var surface = DriveEveryPublisher();

        // Act

        // Assert
        Assert.Contains("mailfathom.mcp.tool.calls", surface.InstrumentNames);
        Assert.Contains(
            surface.EmittedTags,
            tag => tag.Key == McpToolCallTelemetry.ToolTagName
                && Equals(tag.Value, PublishedTools.UnpublishedToolName));
    }

    /// <summary>Nothing this boundary publishes is named after a message, a person, or a secret.</summary>
    [Fact]
    public void EmittedSurface_EveryPublisherDriven_IsNamedAfterNothingInAMailbox()
    {
        // Arrange
        using var surface = DriveEveryPublisher();

        // Act

        // Assert
        TelemetryRedactionContract.AssertNothingIsNamedAfterMailOrASecret(surface.EmittedNames);
    }

    /// <summary>Every instrument and every dimension sits under the one name an operator filters this process by.</summary>
    [Fact]
    public void EmittedSurface_EveryPublisherDriven_IsNamespacedUnderMailFathom()
    {
        // Arrange
        using var surface = DriveEveryPublisher();

        // Act

        // Assert
        TelemetryRedactionContract.AssertEveryDimensionIsNamespacedUnderMailFathom(surface.InstrumentNames, surface.EmittedTags);
    }

    /// <summary>The tool name a caller sent reached no name, no key, and no dimension value.</summary>
    [Fact]
    public void EmittedSurface_EveryCallMadeUnderACallersOwnName_LetsNoneOfItReachAnExporter()
    {
        // Arrange
        using var surface = DriveEveryPublisher();

        // Act

        // Assert
        TelemetryRedactionContract.AssertNoPoisonedInputEscaped(surface.EmittedNames, surface.EmittedTags);
    }

    /// <summary>A publisher nobody added to the drive fails here rather than going unasserted.</summary>
    [Fact]
    public void EveryPublisherInTheAssembly_WhateverItIsCalled_IsDrivenByThisSuite() =>
        TelemetryRedactionContract.AssertEveryPublisherInTheAssemblyIsDriven(
            typeof(McpToolCallTelemetry).Assembly,
            DrivenPublishers);

    /// <summary>Records every ending a tool call has, each under a name the caller invented.</summary>
    private static EmittedTelemetrySurface DriveEveryPublisher()
    {
        var surface = new EmittedTelemetrySurface();
        var callersOwnName = TelemetryRedactionContract.CallerSuppliedSentinel;
        var duration = TimeSpan.FromMilliseconds(37);

        ToolCalls.RecordCompleted(callersOwnName, isError: false, duration);
        ToolCalls.RecordCompleted(callersOwnName, isError: true, duration);
        ToolCalls.RecordCancelled(callersOwnName, duration);
        ToolCalls.RecordProtocolFailure(callersOwnName, duration);
        ToolCalls.RecordRefused(callersOwnName, duration);
        ToolCalls.RecordUnexpectedFailure(callersOwnName, duration);

        surface.ObserveGauges();

        return surface;
    }
}
