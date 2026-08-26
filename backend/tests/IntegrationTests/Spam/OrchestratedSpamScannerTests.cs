// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Sockets;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Scanning;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Spam;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Spam;

/// <summary>Proves the spam adapter against the daemon image a deployment actually pulls.</summary>
/// <remarks>
/// <para>
/// Everything about this scanner that a substitute can settle is settled in the unit suite, where it is cheaper: how a
/// malformed reply is handled, what a timeout produces, how a symbol list becomes a result, and what the bounds refuse.
/// What no substitute settles is the claim this class makes — that a real <c>spamd</c>, on the pinned image, answers the
/// request the adapter builds in the shape the adapter parses, names itself in a way the corpus identity can read, and
/// scores what SpamAssassin is designed to score. A scripted daemon answering a payload somebody hand-wrote proves the
/// parser handles the payload somebody hand-wrote.
/// </para>
/// <para>
/// <b>Every fixture here is fabricated.</b> The spam side is GTUBE, the string SpamAssassin publishes precisely so that
/// a positive verdict can be proven without real spam, and the ordinary side is invented correspondence in the reserved
/// <c>.test</c> domain. No message any person sent enters this repository.
/// </para>
/// <para>
/// The class joins the shared-infrastructure collection because its last test writes to the orchestrated database. The
/// daemon itself is shared with nothing else in the suite, and a failure is reached through an address nothing answers
/// at rather than by stopping the container: stopping a resource this class did not start would break every test behind
/// it, and the code path is identical either way.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedSpamScannerTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The alias this class owns, so its rows are not disturbed by another's.</summary>
    private const string FolderAlias = "spam-scan";

    private const uint ScoredUid = 71;

    /// <summary>
    /// The string SpamAssassin publishes for exactly this purpose: a corpus that is working scores it far above any
    /// threshold, and it carries nothing anybody wrote.
    /// </summary>
    private const string GeneralTestForUnsolicitedBulkEmail =
        "XJS*C4JDBQADN1.NSBN3*2IDNEN*GTUBE-STANDARD-ANTI-UBE-TEST-EMAIL*C.34X";

    /// <summary>The rule the corpus fires on that string, which is what makes the symbol list assertable by name.</summary>
    private const string GeneralTestRuleName = "GTUBE";

    /// <summary>Ordinary correspondence, invented, of the kind a mailbox is full of.</summary>
    private const string OrdinaryCorrespondence =
        "Thanks for the notes from Tuesday. I have folded them into the draft and will send the revision on Friday.";

    /// <summary>The daemon scores what SpamAssassin is designed to score, and says which of its rules did it.</summary>
    /// <remarks>
    /// Three claims in one exchange because they are one answer read three ways, and separating them would spend three
    /// scans to assert three fields of the same reply: the numbers cross the port as an assessment, the symbol list
    /// crosses it as rule names, and the daemon's own release crosses it as the corpus every signal will carry.
    /// </remarks>
    [Fact]
    public async Task ScanAsync_TheStandardTestMessage_IsScoredSpamWithTheRuleThatFiredAndTheCorpusThatFiredIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var composition = this.Compose();
        var scanner = composition.GetRequiredService<ISpamScanner>();

        // Act
        var scan = await scanner.ScanAsync(
            MessageOf(GeneralTestForUnsolicitedBulkEmail),
            cancellationToken);

        // Assert
        Assert.Equal(SpamScanOutcome.Scored, scan.Outcome);
        Assert.NotNull(scan.Assessment);
        Assert.True(
            scan.Assessment.ClearsThreshold,
            $"The daemon scored the standard test message {scan.Assessment.Score} against a threshold of {scan.Assessment.Threshold}, which is not spam.");
        Assert.Contains(GeneralTestRuleName, scan.FiredRules);

        // The corpus names the release the daemon is rather than the protocol it speaks, which is the whole reason the
        // adapter asks it to rewrite a message once instead of pinging it.
        Assert.StartsWith("spamassassin.", scan.CorpusRevision, StringComparison.Ordinal);
    }

    /// <summary>Ordinary correspondence is scored and is not spam, which is what makes the test above mean anything.</summary>
    /// <remarks>
    /// The control for the assertion above. Without it a scanner that called everything spam — or one whose threshold
    /// comparison was inverted — would pass the positive case and be reported as working.
    /// </remarks>
    [Fact]
    public async Task ScanAsync_OrdinaryCorrespondence_IsScoredAndDoesNotClearTheThreshold()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var composition = this.Compose();
        var scanner = composition.GetRequiredService<ISpamScanner>();

        // Act
        var scan = await scanner.ScanAsync(MessageOf(OrdinaryCorrespondence), cancellationToken);

        // Assert
        Assert.Equal(SpamScanOutcome.Scored, scan.Outcome);
        Assert.NotNull(scan.Assessment);
        Assert.False(
            scan.Assessment.ClearsThreshold,
            $"The daemon scored invented correspondence {scan.Assessment.Score} against a threshold of {scan.Assessment.Threshold}, which is spam.");
    }

    /// <summary>A message past the configured size is never sent, and the daemon is not what decides that.</summary>
    /// <remarks>
    /// Proven against the real daemon rather than a substitute because the claim is about which of the two answers: an
    /// adapter that had sent it would get a score back, and the outcome would be <c>Scored</c> instead.
    /// </remarks>
    [Fact]
    public async Task ScanAsync_AMessagePastTheConfiguredSize_IsNotSentAtAll()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var composition = this.Compose(maximumMessageBytes: 32_000);
        var scanner = composition.GetRequiredService<ISpamScanner>();

        // Act
        var scan = await scanner.ScanAsync(
            MessageOf(new string('a', 40_000)),
            cancellationToken);

        // Assert
        Assert.Equal(SpamScanOutcome.ContentTooLarge, scan.Outcome);
        Assert.Null(scan.Assessment);
        Assert.Empty(scan.FiredRules);
    }

    /// <summary>
    /// A daemon that is not there stops the host and leaves a scan unable to score, which are the two halves of one
    /// contract: startup refuses, and a scan that reaches nothing reports that it scored nothing rather than that the
    /// message was clean.
    /// </summary>
    [Fact]
    public async Task AgainstADaemonNothingAnswersAt_StartupRefusesAndAScanScoresNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var composition = Compose(UnreachableDaemon());

        // Act
        var startupFailure = await Assert.ThrowsAsync<SpamScannerUnavailableException>(() =>
            composition.GetRequiredService<ISpamScannerProbe>().VerifyAvailableAsync(cancellationToken));
        var scan = await composition.GetRequiredService<ISpamScanner>().ScanAsync(
            MessageOf(GeneralTestForUnsolicitedBulkEmail),
            cancellationToken);

        // Assert
        Assert.Equal(MailFathomErrorCode.SpamScannerUnavailable, startupFailure.ErrorCode);
        Assert.Equal(SpamScanOutcome.Unavailable, scan.Outcome);
    }

    /// <summary>The rules the daemon fired are what a stored classification carries, one signal each, with the corpus on them.</summary>
    /// <remarks>
    /// The end of the path this feature exists for, and the one claim here that needs the database as well as the
    /// daemon: a scan produces rule names, the classifier turns each into a signal, and the store writes them under the
    /// occurrence. Everything between is unit-covered; what only real infrastructure settles is that the names survive
    /// the round trip through a column and come back readable.
    /// </remarks>
    [Fact]
    public async Task ClassifyAsync_AStoredMessageTheDaemonScoresSpam_RecordsTheRulesItFired()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            spamClassification: SpamClassificationSettings.Create(
                isEnabled: true,
                usesScanner: true,
                [MailFolderAlias.Create(FolderAlias)]),
            spamScanner: this.ProfileOf(orchestration.SpamScanner));
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, ScoredUid);
        var storedEmailId = await StoreAsync(
            services,
            occurrenceId,
            MessageOf(GeneralTestForUnsolicitedBulkEmail).RawMime,
            cancellationToken);

        // Act
        var result = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<EmailSpamClassifier>().ClassifyAsync(
                storedEmailId,
                SpamClassificationMode.Reclassify,
                token),
            cancellationToken);

        // Assert
        var classification = result.Classification;

        Assert.NotNull(classification);
        Assert.Equal(SpamVerdict.Spam, classification.Verdict);
        Assert.Equal(SpamClassificationStage.Scanner, classification.DecidedBy);

        var storedBack = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<IEmailSpamClassificationStore>()
                .FindAsync(storedEmailId, token),
            cancellationToken);

        Assert.NotNull(storedBack);

        var scannerSignals = storedBack.Signals
            .Where(signal => signal.Kind is SpamSignalKind.ScannerRule)
            .ToArray();

        Assert.Contains(scannerSignals, signal => signal.Name == GeneralTestRuleName);
        Assert.All(
            scannerSignals,
            signal =>
            {
                Assert.Equal(SpamSignalSource.ScannerCorpus, signal.Provenance.Source);
                Assert.Equal(storedBack.CorpusRevision, signal.Provenance.Origin);
            });
    }

    /// <summary>Builds a whole synthetic message around one body, so what the daemon reads is a message rather than a fragment.</summary>
    /// <remarks>
    /// The recorded length and digest are computed over the same bytes, because the content store's own read would
    /// report a defect otherwise and this class is not about that path.
    /// </remarks>
    private static StoredEmailContent MessageOf(string body)
    {
        var rawMime = Encoding.ASCII.GetBytes(string.Join(
            "\r\n",
            "From: sender@mailfathom.test",
            "To: owner@mailfathom.test",
            "Subject: Notes from Tuesday",
            "Date: Tue, 04 May 2026 08:30:00 +0000",
            "Message-ID: <spam-scan@mailfathom.test>",
            string.Empty,
            body,
            string.Empty));

        return new StoredEmailContent(
            rawMime,
            rawMime.LongLength,
            System.Security.Cryptography.SHA256.HashData(rawMime));
    }

    /// <summary>Reserves a loopback port and releases it, so the address is one nothing is listening on.</summary>
    /// <remarks>
    /// Asking the operating system for a free port and giving it back is how a port known to refuse a connection is
    /// obtained; a number written here would be one some other process on the machine might hold.
    /// </remarks>
    private static Uri UnreachableDaemon()
    {
        using var reservation = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reservation.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        return new Uri($"tcp://127.0.0.1:{((IPEndPoint)reservation.LocalEndPoint!).Port}", UriKind.Absolute);
    }

    private static ServiceProvider Compose(Uri daemon, int maximumMessageBytes = 512_000)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(SpamAssassinScannerProfile.Create(
            daemon.Host,
            daemon.Port,
            TimeSpan.FromSeconds(30),
            maximumMessageBytes,
            maximumConcurrentScans: 5));

        // The real registration rather than a scanner composed here, so what these tests exercise is the composition a
        // deployment gets: one conversation shared by the scanner and the probe, and the bounds read off the profile.
        services.AddSpamAssassinScanning();

        return services.BuildServiceProvider();
    }

    private static async Task<StoredEmailId> StoreAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrenceId,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        StoredEmailId? storedEmailId = null;

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                storedEmailId = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session, SyntheticMailAccount.Owner,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, "Notes from Tuesday", rawMime.Length),
                    extractedMetadata: null,
                    StoredEmailContentAvailability.Available,
                    token);

                await scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                    session,
                    storedEmailId.Value,
                    occurrenceId,
                    PlacedEmailContent.InDatabase(rawMime),
                    token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailId!.Value;
    }

    private ServiceProvider Compose(int maximumMessageBytes = 512_000) =>
        Compose(orchestration.SpamScanner, maximumMessageBytes);

    private SpamAssassinScannerProfile ProfileOf(Uri daemon) => SpamAssassinScannerProfile.Create(
        daemon.Host,
        daemon.Port,
        TimeSpan.FromSeconds(30),
        maximumMessageBytes: 512_000,
        maximumConcurrentScans: 5);
}
