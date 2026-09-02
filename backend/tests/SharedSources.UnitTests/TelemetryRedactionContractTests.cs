// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.TestSupport;
using Xunit;
using Xunit.Sdk;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the rules the whole telemetry surface is held against, in both directions.</summary>
/// <remarks>
/// Every assertion this file is about reports a defect as an empty collection being non-empty, which is a shape that
/// passes just as quietly when the rule matches nothing at all. So each rule is driven twice: once over a surface that
/// obeys it, and once over one that breaks it in the way the rule exists to catch. The sample types the discovery
/// checks read are declared here rather than taken from a production boundary, so what they prove is the rule rather
/// than today's arrangement of somebody else's assembly.
/// </remarks>
public sealed class TelemetryRedactionContractTests
{
    /// <summary>A name that says nothing about a mailbox passes, whichever of the three forms it takes.</summary>
    [Fact]
    public void AssertNothingIsNamedAfterMailOrASecret_OrdinaryNames_Passes() =>
        TelemetryRedactionContract.AssertNothingIsNamedAfterMailOrASecret(
            ["synchronize_account", "mailfathom.mail.sync.run.duration", "mailfathom.answering.period.tokens"]);

    /// <summary>A name that is about a message, a person, or a secret fails, whole segment by whole segment.</summary>
    [Theory]
    [InlineData("mailfathom.mail.subject")]
    [InlineData("mailfathom.mail.sender.address")]
    [InlineData("read_email_body")]
    [InlineData("mailfathom.mcp.tool.query")]
    [InlineData("mailfathom.answering.prompt")]
    [InlineData("mailfathom.mail.occurrence.uid")]
    [InlineData("mailfathom.provider.token")]
    [InlineData("mailfathom.mail.folder.path")]
    public void AssertNothingIsNamedAfterMailOrASecret_ANameAboutAMailbox_Fails(string name) =>
        Assert.Throws<EmptyException>(() =>
            TelemetryRedactionContract.AssertNothingIsNamedAfterMailOrASecret([name]));

    /// <summary>A word inside another word is not that word, which is what keeps two legitimate names legitimate.</summary>
    [Theory]
    [InlineData("mailfathom.answering.tokens")]
    [InlineData("mailfathom.mail.content.stored_total")]
    public void AssertNothingIsNamedAfterMailOrASecret_AForbiddenWordInsideAnotherWord_Passes(string name) =>
        TelemetryRedactionContract.AssertNothingIsNamedAfterMailOrASecret([name]);

    /// <summary>A dimension minted from a value, or written outside the one namespace, fails.</summary>
    [Theory]
    [InlineData("mailfathom.mail.Account")]
    [InlineData("mail.sync.outcome")]
    [InlineData("mailfathom.mail.folder-alias")]
    public void AssertEveryDimensionIsNamespacedUnderMailFathom_ANameOutsideTheShape_Fails(string name) =>
        Assert.Throws<EmptyException>(() =>
            TelemetryRedactionContract.AssertEveryDimensionIsNamespacedUnderMailFathom([name], []));

    /// <summary>A dimension in the one namespace passes, whether it arrives as an instrument or as a tag key.</summary>
    [Fact]
    public void AssertEveryDimensionIsNamespacedUnderMailFathom_TheNamesInUse_Passes() =>
        TelemetryRedactionContract.AssertEveryDimensionIsNamespacedUnderMailFathom(
            ["mailfathom.jobs.queue.depth"],
            [new KeyValuePair<string, object?>("mailfathom.job.type", "classify_email_spam")]);

    /// <summary>A span named after what it saw rather than after what it did fails.</summary>
    [Theory]
    [InlineData("Synchronize Account")]
    [InlineData("synchronize/INBOX")]
    public void AssertEverySpanIsNamedAfterItsOperation_ANameOutsideTheShape_Fails(string name) =>
        Assert.Throws<EmptyException>(() =>
            TelemetryRedactionContract.AssertEverySpanIsNamedAfterItsOperation([name]));

    /// <summary>The hyphenated form a mutation span takes passes, because it is the published name of the mutation.</summary>
    [Fact]
    public void AssertEverySpanIsNamedAfterItsOperation_AMutationsOwnName_Passes() =>
        TelemetryRedactionContract.AssertEverySpanIsNamedAfterItsOperation(["set-seen", "synchronize_account"]);

    /// <summary>An alias reaching a dimension named for one is the case the contract allows.</summary>
    [Fact]
    public void AssertNoPoisonedInputEscaped_AnAliasOnADimensionNamedForOne_Passes() =>
        TelemetryRedactionContract.AssertNoPoisonedInputEscaped(
            ["mailfathom.mail.account"],
            [
                new KeyValuePair<string, object?>(
                    "mailfathom.mail.account",
                    TelemetryRedactionContract.ConfiguredAliasSentinel),
            ]);

    /// <summary>An alias reaching any other dimension fails, because only the ones named for it may carry one.</summary>
    [Fact]
    public void AssertNoPoisonedInputEscaped_AnAliasOnAnyOtherDimension_Fails() =>
        Assert.Throws<EmptyException>(() =>
            TelemetryRedactionContract.AssertNoPoisonedInputEscaped(
                ["mailfathom.jobs.attempts"],
                [
                    new KeyValuePair<string, object?>(
                        "mailfathom.job.type",
                        TelemetryRedactionContract.ConfiguredAliasSentinel),
                ]));

    /// <summary>A caller's text and anything read out of a message reach no dimension at all.</summary>
    [Theory]
    [InlineData(TelemetryRedactionContract.CallerSuppliedSentinel)]
    [InlineData(TelemetryRedactionContract.MailDerivedSentinel)]
    public void AssertNoPoisonedInputEscaped_ForbiddenTextOnAPermittedDimension_Fails(string poison) =>
        Assert.Throws<EmptyException>(() =>
            TelemetryRedactionContract.AssertNoPoisonedInputEscaped(
                ["mailfathom.mail.account"],
                [new KeyValuePair<string, object?>("mailfathom.mail.account", poison)]));

    /// <summary>A poisoned string that became part of a name fails wherever the name came from.</summary>
    [Fact]
    public void AssertNoPoisonedInputEscaped_APoisonedName_Fails() =>
        Assert.Throws<EmptyException>(() =>
            TelemetryRedactionContract.AssertNoPoisonedInputEscaped(
                [$"mailfathom.{TelemetryRedactionContract.MailDerivedSentinel}"],
                []));

    /// <summary>A type holding an instrument is a publisher, and a suite that did not drive it is told so.</summary>
    [Fact]
    public void AssertEveryPublisherInTheAssemblyIsDriven_APublisherNobodyDrove_Fails() =>
        Assert.Throws<EmptyException>(() =>
            TelemetryRedactionContract.AssertEveryPublisherInTheAssemblyIsDriven(
                typeof(SamplePublisher).Assembly,
                []));

    /// <summary>A suite naming every publisher in the assembly passes.</summary>
    [Fact]
    public void AssertEveryPublisherInTheAssemblyIsDriven_EveryPublisherNamed_Passes()
    {
        // Arrange — the sample is a publisher by holding an instrument, so it is built and driven like one.
        new SamplePublisher().Record();

        // Act

        // Assert
        TelemetryRedactionContract.AssertEveryPublisherInTheAssemblyIsDriven(
            typeof(SamplePublisher).Assembly,
            [typeof(SamplePublisher), typeof(SampleSpanPublisher)]);
    }

    /// <summary>The declared names are read off the two field-name conventions and nothing else.</summary>
    [Fact]
    public void DeclaredTelemetryNamesIn_ThisAssembly_ReadsTheTagAndSpanConstantsAlone()
    {
        // Arrange
        var declared = TelemetryRedactionContract.DeclaredTelemetryNamesIn(typeof(SamplePublisher).Assembly);

        // Act

        // Assert
        Assert.Contains(("mailfathom.sample.outcome", false), declared);
        Assert.Contains(("run_sample", true), declared);
        Assert.DoesNotContain(declared, name => name.Value == SampleSpanPublisher.NotATelemetryName);
    }

    /// <summary>A declaration that breaks the contract is caught without anything having to run.</summary>
    [Fact]
    public void AssertEveryDeclaredNameObeysTheContract_ThisAssembly_FailsOnTheDeliberatelyWrongOne() =>
        Assert.Throws<EmptyException>(() =>
            TelemetryRedactionContract.AssertEveryDeclaredNameObeysTheContract(typeof(SamplePublisher).Assembly));

    /// <summary>A publisher found by the instrument it holds rather than by where it lives.</summary>
    private sealed class SamplePublisher
    {
        internal const string OutcomeTagName = "mailfathom.sample.outcome";

        private readonly Counter<long> samples =
            new Meter("SharedSources.SamplePublisher").CreateCounter<long>("mailfathom.sample.count");

        internal void Record() => this.samples.Add(1);
    }

    /// <summary>A publisher found by the span constant it declares, and the one wrong name this assembly carries.</summary>
    /// <remarks>
    /// <see cref="SubjectTagName" /> is deliberately named after a message so the declared-name assertion has something
    /// to fail on. It is a constant in a test double and reaches no meter and no activity source, so nothing publishes
    /// it — which is the point: the check reads declarations, and this proves it reads them.
    /// </remarks>
    private static class SampleSpanPublisher
    {
        internal const string RunSpanName = "run_sample";

        internal const string SubjectTagName = "mailfathom.sample.subject";

        internal const string NotATelemetryName = "mailfathom.sample-hash.v1";
    }
}
