// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.UnitTests.Discovery.Presentation;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Streaming;

/// <summary>Covers the stored form a run's events are journalled in, which is written by one build and read by another.</summary>
/// <remarks>
/// <para>
/// The events are rows rather than objects in one process, so this shape is a contract: a run is delivered to a client
/// by a signal saying where to read and a read that goes to the database, and an event that cannot be read back loses
/// the run whatever the execution that composed it did.
/// </para>
/// <para>
/// It is asserted here rather than only where the rows are, because the failure it guards against needs no database to
/// find and is invisible to the compiler: the context is source-generated, source generation reaches no non-public
/// member, and a payload type behind a factory therefore writes perfectly and refuses to read. Running the whole
/// catalogue through it is what makes the next such type a red fast gate rather than a dispatched suite.
/// </para>
/// </remarks>
public sealed class DiscoveryRunEventJsonContextTests
{
    private static readonly MailAnsweringRunSpend Spent = new(3, 12_000, 4_000, 6);

    /// <summary>Every event a run can publish reads back as the document it was written from.</summary>
    [Fact]
    public void Deserialize_EveryEventARunPublishes_ReadsBackTheDocumentItWasWrittenFrom()
    {
        foreach (var written in EveryEvent())
        {
            var payload = JsonSerializer.Serialize(written, DiscoveryRunEventJsonContext.Default.DiscoveryRunEvent);

            var read = JsonSerializer.Deserialize(payload, DiscoveryRunEventJsonContext.Default.DiscoveryRunEvent);

            Assert.NotNull(read);
            Assert.Equal(written.EventName, read.EventName);
            Assert.Equal(
                payload,
                JsonSerializer.Serialize(read, DiscoveryRunEventJsonContext.Default.DiscoveryRunEvent));
        }
    }

    /// <summary>What a run may spend survives the journal, which is what every count it reports is read against.</summary>
    [Fact]
    public void Deserialize_AStartEventStatingWhatARunMaySpend_ReadsTheBoundsBackAsTheyWereWritten()
    {
        var written = new DiscoveryRunStarted { Bounds = MailAnsweringRunBounds.Create(9_000, 3, 40_000) };
        var payload = JsonSerializer.Serialize(
            written,
            DiscoveryRunEventJsonContext.Default.DiscoveryRunEvent);

        var read = Assert.IsType<DiscoveryRunStarted>(
            JsonSerializer.Deserialize(payload, DiscoveryRunEventJsonContext.Default.DiscoveryRunEvent));

        Assert.Equal(9_000, read.Bounds.MaximumRetrievedCharacters);
        Assert.Equal(3, read.Bounds.MaximumProviderCalls);
        Assert.Equal(40_000, read.Bounds.MaximumTokens);
    }

    /// <summary>One of each event, with a block of every catalogued type and every citation the example rests on.</summary>
    private static IEnumerable<DiscoveryRunEvent> EveryEvent()
    {
        yield return new DiscoveryRunStarted
        {
            Bounds = MailAnsweringRunBounds.Create(9_000, 3, 40_000),
            EndpointAlias = "answering",
            PublishedModel = "a-published-model",
        };

        yield return new DiscoveryRetrievalProgressed(new DiscoveryRetrievalProgress(2, 1, 3, 7), Spent);

        foreach (var citation in PresentationPlanExample.Citations())
        {
            yield return new DiscoveryCitationDeclared(citation);
        }

        foreach (var block in PresentationPlanExample.EveryBlock())
        {
            yield return new DiscoveryBlockComposed(block);
        }

        yield return new DiscoveryRunCompleted(
            [PresentationLimitation.RetrievalTruncated],
            PresentationPlanExample.Coverage(),
            Spent);

        yield return new DiscoveryRunFailed(DiscoveryRunFailure.PeriodSpent, Spent)
        {
            RetryAt = PresentationPlanExample.ObservedAt.AddHours(1),
        };
    }
}
