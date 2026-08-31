// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Extraction;

/// <summary>Covers the seam at which every derived copy of a message's body is redacted.</summary>
public sealed class RedactingEmailMimeReaderTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero));

    /// <summary>The placeholder is what everything cut, embedded, and retrieved from this text will carry.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ABodyCarryingACredential_YieldsTextWithThePlaceholderInIt()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);
        var reader = new RedactingEmailMimeReader(
            ReaderYielding($"the key is {Marker}", $"quoted history\nthe key is {Marker}"),
            derivation.Guard);

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the key is [redacted:CloudKey]", extraction.Metadata?.Text.TrimmedText);
        Assert.Equal("quoted history\nthe key is [redacted:CloudKey]", extraction.Metadata?.Text.OriginalText);
    }

    /// <summary>A credential left in the untrimmed reading is a credential in the derived store, so both are scanned.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ACredentialOnlyTheUntrimmedReadingKept_IsStillReplaced()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);
        var reader = new RedactingEmailMimeReader(
            ReaderYielding("nothing to see", $"an older turn wrote {Marker}\nnothing to see"),
            derivation.Guard);

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("nothing to see", extraction.Metadata?.Text.TrimmedText);
        Assert.Equal("an older turn wrote [redacted:CloudKey]\nnothing to see", extraction.Metadata?.Text.OriginalText);
    }

    /// <summary>The ordinary message quotes nothing, and paying two scans for its one text would double what this costs.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ABodyTrimmingLeftUnchanged_ScansItOnce()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);
        var reader = new RedactingEmailMimeReader(
            ReaderYielding($"the key is {Marker}", $"the key is {Marker}"),
            derivation.Guard);

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the key is [redacted:CloudKey]", extraction.Metadata?.Text.TrimmedText);
        Assert.Equal("the key is [redacted:CloudKey]", extraction.Metadata?.Text.OriginalText);
        Assert.Single(derivation.Scanner.ScannedTexts);
    }

    /// <summary>Redaction changes what the words are, never which part of the message they were read from.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ALossyReadingOfAnHtmlBody_StaysMarkedAsOne()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);
        var reader = new RedactingEmailMimeReader(
            ReaderYielding(
                ExtractedEmailText.DerivedFromHtmlBody($"original {Marker}", $"trimmed {Marker}")),
            derivation.Guard);

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ExtractedEmailTextSource.DerivedFromHtmlBodyPart, extraction.Metadata?.Text.Source);
        Assert.True(extraction.Metadata?.Text.IsDerivedFromHtml);
    }

    /// <summary>The envelope is what a listing filters on and what a reply is addressed to, so it is left alone.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ABodyCarryingACredential_LeavesTheEnvelopeMetadataAsItWasRead()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);
        var reader = new RedactingEmailMimeReader(
            ReaderYielding($"the key is {Marker}", $"the key is {Marker}"),
            derivation.Guard);

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Subject", extraction.Metadata?.Subject);
    }

    /// <summary>A message with no words to scan must cost no scan at all.</summary>
    [Theory]
    [InlineData(ExtractedEmailTextSource.NoTextualBodyPart)]
    [InlineData(ExtractedEmailTextSource.EncryptedBody)]
    public async Task ReadMetadataAsync_AMessageThatYieldedNoWords_ReachesNoScanner(ExtractedEmailTextSource source)
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);
        var text = source == ExtractedEmailTextSource.EncryptedBody
            ? ExtractedEmailText.EncryptedBody
            : ExtractedEmailText.NoTextualBody;
        var reader = new RedactingEmailMimeReader(ReaderYielding(text), derivation.Guard);

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(text, extraction.Metadata?.Text);
        Assert.Empty(derivation.Scanner.ScannedTexts);
    }

    /// <summary>Content nobody could parse carries no text either, and the failure has to reach the caller unchanged.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ContentNoReaderCouldParse_IsCarriedThroughAsTheFailureItIs()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);
        var inner = Substitute.For<IEmailMimeReader>();
        inner.ReadMetadataAsync(Arg.Any<RemoteEmailContent>(), Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailMimeExtractionResult.MalformedContent()));
        var reader = new RedactingEmailMimeReader(inner, derivation.Guard);

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailMimeExtractionOutcome.MalformedContent, extraction.Outcome);
        Assert.Empty(derivation.Scanner.ScannedTexts);
    }

    /// <summary>A derived write that fell back to the unredacted reading would put the credential in the index.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ADetectorThatCannotAnswer_RefusesTheDerivationRatherThanStoringTheText()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Unavailable(this.timeProvider);
        var reader = new RedactingEmailMimeReader(
            ReaderYielding("whatever the message said", "whatever the message said"),
            derivation.Guard);

        // Act
        var refusal = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Secrets, refusal.Scanner);
        Assert.DoesNotContain("whatever the message said", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>With both switches off nothing wraps this port at all, and the reading has to be the one it was.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ADeploymentThatScansNothing_YieldsTheTextExactlyAsItWasExtracted()
    {
        // Arrange
        var guard = ScanningSensitiveContentDerivation.Inactive();
        var extracted = ExtractedEmailText.FromPlainTextBody($"original {Marker}", $"trimmed {Marker}");
        var reader = new RedactingEmailMimeReader(ReaderYielding(extracted), guard);

        // Act
        var extraction = await reader.ReadMetadataAsync(Content(), SyntheticMailOwner.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"original {Marker}", extraction.Metadata?.Text.OriginalText);
        Assert.Equal($"trimmed {Marker}", extraction.Metadata?.Text.TrimmedText);
    }

    /// <summary>
    /// The stamp travels on the reading rather than being resolved where the reading is written, because a batch is
    /// read outside any transaction and commits afterwards: a stamp taken at the write would record a posture the text
    /// beside it never went through, and a row stamped stricter than what produced it is a row nothing revisits.
    /// </summary>
    [Fact]
    public async Task ReadMetadataAsync_ABodyRedactedForAnOwner_CarriesThePostureItWasRedactedUnder()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);
        var reader = new RedactingEmailMimeReader(
            ReaderYielding($"before {Marker} after", $"before {Marker} after"),
            derivation.Guard);

        // Act
        var extraction = await reader.ReadMetadataAsync(
            Content(),
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            derivation.Guard.StampFor(SyntheticMailOwner.Deployment),
            extraction.Metadata!.RedactedUnder);
    }

    /// <summary>A message with no body to redact is still derived under a posture, and a row left unstamped would be outstanding for ever.</summary>
    [Fact]
    public async Task ReadMetadataAsync_AMessageWithNoTextToRedact_IsStampedJustTheSame()
    {
        // Arrange
        using var derivation = ScanningSensitiveContentDerivation.Finding(Marker, this.timeProvider);
        var reader = new RedactingEmailMimeReader(
            ReaderYielding(ExtractedEmailText.NoTextualBody),
            derivation.Guard);

        // Act
        var extraction = await reader.ReadMetadataAsync(
            Content(),
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            derivation.Guard.StampFor(SyntheticMailOwner.Deployment),
            extraction.Metadata!.RedactedUnder);
    }

    /// <summary>Nothing scanned it, so the row says so: that is what separates "written under no scanner" from "written under this one".</summary>
    [Fact]
    public async Task ReadMetadataAsync_ADeploymentThatScansNothing_CarriesNoPosture()
    {
        // Arrange
        var guard = ScanningSensitiveContentDerivation.Inactive();
        var reader = new RedactingEmailMimeReader(ReaderYielding("body", "body"), guard);

        // Act
        var extraction = await reader.ReadMetadataAsync(
            Content(),
            SyntheticMailOwner.Deployment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(extraction.Metadata!.RedactedUnder);
    }

    private static IEmailMimeReader ReaderYielding(string trimmedText, string originalText) =>
        ReaderYielding(ExtractedEmailText.FromPlainTextBody(originalText, trimmedText));

    private static IEmailMimeReader ReaderYielding(ExtractedEmailText text)
    {
        var reader = Substitute.For<IEmailMimeReader>();

        reader.ReadMetadataAsync(Arg.Any<RemoteEmailContent>(), Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(EmailMimeExtractionResult.Extracted(new ExtractedEmailMetadata(
                call.Arg<RemoteEmailContent>()!.OccurrenceId,
                Subject: "Subject",
                SentAt: null,
                ReceivedAt: null,
                Participants: [],
                EmailThreadReferences.None,
                EmailAttachmentSummary.None,
                text,
                SenderAuthentication.NotEstablished()))));

        return reader;
    }

    private static RemoteEmailContent Content() => new(
        EmailOccurrenceId.Create(
            MailAccountId.Create("primary"),
            new MailFolderResolutionId(MailFolderAlias.Create("inbox"), MailFolderResolutionGeneration.First),
            ImapUidValidity.Create(5),
            ImapUid.Create(11)),
        new byte[] { 1, 2, 3 });
}
