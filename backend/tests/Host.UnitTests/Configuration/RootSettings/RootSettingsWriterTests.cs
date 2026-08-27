// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Configuration;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.RootSettings;

/// <summary>
/// Covers what a configuration write does to the deployment. Every assertion reads the row and the published layer
/// rather than the writer's own answer alone, because the contract is about what a later reader sees: a refused write
/// leaves both exactly as they were, and a committed one moves both together.
/// </summary>
public sealed class RootSettingsWriterTests
{
    private static readonly DateTimeOffset AnyInstant = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A change the composed configuration binds is committed, and the version it produced is reported.</summary>
    [Fact]
    public async Task WriteAsync_AChangeTheConfigurationBinds_Commits()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act
        var result = await deployment.WriteAsync(ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3"));

        // Assert
        Assert.True(result.IsCommitted);
        Assert.Equal(5, result.Version);
        Assert.Equal(5, deployment.Row.Version);
        Assert.Equal(1, deployment.Row.AcceptedCommits);
    }

    /// <summary>
    /// The reload token rises after the commit, so everything bound to the layer observes the version the deployment
    /// actually holds rather than a candidate a failed commit would have taken back.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ACommittedChange_RepublishesTheVersionItCommitted()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act
        await deployment.WriteAsync(ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3"));

        // Assert
        Assert.Equal(5, deployment.Layer.Version);
        Assert.Equal("3", deployment.PublishedValueOf("MailboxSearch:SnippetsPerEmail"));
    }

    /// <summary>
    /// A caller giving up between the commit and the republish leaves the process reading the version the database
    /// holds, because a commit that is already durable is not one the caller can take back by cancelling.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ACallerCancellingAfterTheCommit_StillRepublishesTheCommittedVersion()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);
        using var abandoned = new CancellationTokenSource();

        deployment.Row.WhenCommitted = abandoned.Cancel;

        // Act
        var result = await deployment.WriteAsync(abandoned.Token, ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3"));

        // Assert
        Assert.True(result.IsCommitted);
        Assert.Equal(5, deployment.Layer.Version);
        Assert.Equal("3", deployment.PublishedValueOf("MailboxSearch:SnippetsPerEmail"));
    }

    /// <summary>A setting the write did not name is still carried, because the persisted document is sparse.</summary>
    [Fact]
    public async Task WriteAsync_AChangeBesideAPersistedSetting_LeavesTheOtherSettingCarried()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted(
            """{ "Deployment": { "PublicBaseAddress": "https://mail.example/" } }""",
            version: 1);

        // Act
        await deployment.WriteAsync(ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3"));

        // Assert
        deployment.Layer.TryGet("Deployment:PublicBaseAddress", out var untouched);
        Assert.Equal("https://mail.example/", untouched);
    }

    /// <summary>A removal stops the document carrying the setting, so the source beneath the layer supplies it again.</summary>
    [Fact]
    public async Task WriteAsync_ARemoval_StopsTheLayerCarryingTheSetting()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted(
            """{ "MailboxSearch": { "SnippetsPerEmail": "3" } }""",
            version: 1);

        // Act
        var result = await deployment.WriteAsync(ConfigurationEdit.Removing("MailboxSearch:SnippetsPerEmail"));

        // Assert
        Assert.True(result.IsCommitted);
        deployment.Layer.TryGet("MailboxSearch:SnippetsPerEmail", out var published);
        Assert.Null(published);
    }

    /// <summary>
    /// A candidate is judged as the configuration it would produce rather than as a document, so a setting an operator
    /// override supplies is what the validators see. The persisted value here would refuse on its own.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ASettingAnOverrideBeats_IsJudgedAgainstTheOverride()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted(
            "{}",
            version: 1,
            operatorOverride: """{ "MailboxSearch": { "SnippetsPerEmail": "3" } }""");

        // Act
        var result = await deployment.WriteAsync(
            ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "-1"));

        // Assert
        Assert.True(result.IsCommitted);
    }

    /// <summary>An unknown property is refused, and the deployment keeps the document and the version it had.</summary>
    [Fact]
    public async Task WriteAsync_AnUnknownProperty_IsRefusedAndChangesNothing()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act
        var result = await deployment.WriteAsync(ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmails", "3"));

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, result.Refusal);
        Assert.Equal(4, result.Version);
        Assert.Equal("{}", deployment.Row.Json);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
        Assert.Equal(4, deployment.Layer.Version);
    }

    /// <summary>A segment that is not the array position it was written as is refused by the same binding.</summary>
    [Fact]
    public async Task WriteAsync_AMalformedArrayIndex_IsRefusedAndChangesNothing()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act
        var result = await deployment.WriteAsync(
            ConfigurationEdit.SetTo("MailSynchronization:Accounts:second:Alias", "work"));

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, result.Refusal);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>A value a validator refuses is refused, and every refused setting travels back with it.</summary>
    [Fact]
    public async Task WriteAsync_AValueAValidatorRefuses_IsRefusedAndSaysWhat()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act
        var result = await deployment.WriteAsync(ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "-1"));

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, result.Refusal);
        Assert.NotEmpty(result.RefusalMessages);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>A write composed over a version the document has already passed is refused, naming the version in force.</summary>
    [Fact]
    public async Task WriteAsync_AVersionTheDocumentHasPassed_IsRefusedAndChangesNothing()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 6);

        // Act
        var result = await deployment.WriteAsync(
            expectedVersion: 4,
            ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3"));

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, result.Refusal);
        Assert.Equal(6, result.Version);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
        Assert.Contains(result.RefusalMessages, message => message.Contains("version 6", StringComparison.Ordinal));
    }

    /// <summary>
    /// Two administrators editing at once: the first write stands, and the second is refused rather than composed over
    /// it, so neither change disappears without anybody being told.
    /// </summary>
    [Fact]
    public async Task WriteAsync_TwoWritersOverOneVersion_RefusesTheSecondAndKeepsTheFirst()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act
        var first = await deployment.WriteAsync(
            expectedVersion: 4,
            ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3"));
        var second = await deployment.WriteAsync(
            expectedVersion: 4,
            ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "5"));

        // Assert
        Assert.True(first.IsCommitted);
        Assert.False(second.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, second.Refusal);
        Assert.Equal(5, deployment.Row.Version);
        Assert.Equal(1, deployment.Row.AcceptedCommits);
        deployment.Layer.TryGet("MailboxSearch:SnippetsPerEmail", out var published);
        Assert.Equal("3", published);
    }

    /// <summary>
    /// The window the version guard exists for, which the two cases above never open: the row is read, the candidate is
    /// composed and judged over what it said, and a competitor commits before the statement is issued. The refusal
    /// names the version now in force rather than the one after the version read, because the winner may have
    /// committed more than once while this candidate was being judged — so the number the operator recomposes over is
    /// read from the row rather than counted from the one this write started at.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ARowMovedBetweenTheReadAndTheCommit_IsRefusedNamingTheVersionNowInForce()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);
        var competitorHasCommitted = false;

        deployment.Row.WhenRead = () =>
        {
            if (competitorHasCommitted)
            {
                return;
            }

            competitorHasCommitted = true;
            deployment.Row.CommitFromElsewhere("""{ "MailboxSearch": { "SnippetsPerEmail": "9" } }""");
            deployment.Row.CommitFromElsewhere("""{ "MailboxSearch": { "SnippetsPerEmail": "11" } }""");
        };

        // Act
        var result = await deployment.WriteAsync(
            expectedVersion: 4,
            ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3"));

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, result.Refusal);
        Assert.Equal(6, result.Version);
        Assert.Equal(6, deployment.Row.Version);
        Assert.Contains(result.RefusalMessages, message => message.Contains("version 6", StringComparison.Ordinal));
        Assert.Null(deployment.PublishedValueOf("MailboxSearch:SnippetsPerEmail"));
    }

    /// <summary>
    /// A row somebody edited directly in the database, which the operator page says is possible. Every case above is
    /// answered before the row is read, by the path the caller named; this one is answered by the composition, because
    /// the offending setting is one the *row* carries and the write named an unrelated path. Without the refusal an
    /// ordinary write would leave the port as an exception rather than as a refusal naming the setting.
    /// </summary>
    /// <param name="rowSomebodyEdited">A persisted document carrying a setting the layer may not carry.</param>
    /// <param name="named">The declared setting the refusal has to name for the operator to find it in the row.</param>
    [Theory]
    [InlineData("""{ "Persistence": { "Password": { "SecretReference": "file:/run/secrets/db" } } }""", "Persistence:Password")]
    [InlineData("""{ "Accounts": { "0": { "DisplayName": "owner" } } }""", "Accounts")]
    public async Task WriteAsync_ARowAlreadyCarryingASettingTheLayerMayNotCarry_IsRefusedRatherThanRaised(
        string rowSomebodyEdited,
        string named)
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        deployment.Row.CommitFromElsewhere(rowSomebodyEdited);

        // Act
        var result = await deployment.WriteAsync(ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3"));

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationPathNotWritable, result.Refusal);
        Assert.Equal(1, deployment.Row.AcceptedCommits);
        Assert.Contains(result.RefusalMessages, message => message.Contains(named, StringComparison.Ordinal));
    }

    /// <summary>
    /// A section a registration refuses before any validator runs — a resilience section naming no dependency class —
    /// leaves the write as a refusal rather than as an exception. It is the same operator's mistake as a value a
    /// validator turns away, and a start meets it at the same point.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ASectionARegistrationRefuses_IsRefusedRatherThanRaised()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act
        var result = await deployment.WriteAsync(ConfigurationEdit.SetTo("Resilience:EmailDelivry:MaxAttempts", "3"));

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, result.Refusal);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
        Assert.Contains(result.RefusalMessages, message => message.Contains("EmailDelivry", StringComparison.Ordinal));
    }

    /// <summary>A setting MailFathom reads before the layer exists is persisted nowhere, so a write to it is refused.</summary>
    [Theory]
    [InlineData("Persistence:Password:SecretReference")]
    [InlineData("Secrets:Interpretation")]
    [InlineData("ConfigurationSources:Directory")]
    public async Task WriteAsync_ABootstrapSetting_IsRefusedBeforeAnythingIsRead(string path)
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act
        var result = await deployment.WriteAsync(ConfigurationEdit.SetTo(path, "file:/run/secrets/whatever"));

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationPathNotWritable, result.Refusal);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>A write addressed at a section containing a bootstrap setting is refused too, for what it would carry.</summary>
    [Fact]
    public async Task WriteAsync_ASectionContainingABootstrapSetting_IsRefused()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act
        var result = await deployment.WriteAsync(ConfigurationEdit.SetTo("Persistence", "anything"));

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationPathNotWritable, result.Refusal);
    }

    /// <summary>A setting another store owns is refused rather than written into the root document.</summary>
    [Fact]
    public async Task WriteAsync_ASettingAnotherStoreOwns_IsRefused()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act
        var result = await deployment.WriteAsync(ConfigurationEdit.SetTo("Accounts:0:DisplayName", "owner"));

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationPathNotWritable, result.Refusal);
        Assert.Contains(result.RefusalMessages, message => message.Contains("owner-accounts", StringComparison.Ordinal));
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>
    /// Secret material is refused where a reference belongs, and the refusal repeats neither the value nor its length.
    /// The inline scheme is material as much as a bare password is, which is why both are refused.
    /// </summary>
    [Theory]
    [InlineData("hunter2")]
    [InlineData("plaintext:hunter2")]
    // A colon makes a value parse as a reference and does not make it one: the scheme it names is minted from whatever
    // stood before the colon, and no adapter serves it. Admitted, it would be written verbatim into the column every
    // later read composes from and would then fail to resolve at the next use.
    [InlineData("hunter2:hunter2")]
    public async Task WriteAsync_SecretMaterial_IsRefusedWithoutRepeatingIt(string material)
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act
        var result = await deployment.WriteAsync(
            ConfigurationEdit.SetTo("MailSynchronization:Accounts:0:Secrets:Password:SecretReference", material));

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationSecretMaterialRefused, result.Refusal);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
        Assert.DoesNotContain(result.RefusalMessages, message => message.Contains("hunter2", StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.RefusalMessages,
            message => message.Contains(material.Length.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    /// <summary>
    /// A document past what the layer composes settings from is refused as a result rather than raised as an argument
    /// failure: every change in it is within the bounds a caller can honour, and what is left for the administrator to
    /// do is persist less.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ChangesComposingADocumentPastTheCeiling_AreRefused()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);
        var edits = Enumerable.Range(0, 200)
            .Select(position => ConfigurationEdit.SetTo(
                $"Deployment:Padding:{position}",
                new string('x', ConfigurationEdit.MaximumValueLength)))
            .ToArray();

        // Act
        var result = await deployment.WriteAsync(edits);

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Equal(MailFathomErrorCode.ConfigurationDocumentTooLarge, result.Refusal);
        Assert.Equal(4, result.Version);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>A reference naming where the material is kept is what the document may carry, so it reaches validation.</summary>
    [Fact]
    public async Task WriteAsync_ASecretReference_IsNotRefusedAsMaterial()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act
        var result = await deployment.WriteAsync(ConfigurationEdit.SetTo(
            "MailSynchronization:Accounts:0:Secrets:Password:SecretReference",
            "file:/run/secrets/mailbox-password"));

        // Assert
        Assert.NotEqual(MailFathomErrorCode.ConfigurationSecretMaterialRefused, result.Refusal);
    }

    /// <summary>
    /// A refusal is recorded as a count rather than as the sentences it produced. The change is one whose refusal
    /// provably quotes what the caller wrote — the path, which the refusal has to name for the caller to know which of
    /// its changes was turned away — so the assertion is about redaction rather than about a message that happened to
    /// carry nothing of the caller's. The caller already holds what it sent; a log an operator reads does not.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ARefusedChange_RecordsHowManySettingsRatherThanWhatWasWritten()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);
        const string Path = "Persistence:Password:SecretReference";

        // Act
        var result = await deployment.WriteAsync(ConfigurationEdit.SetTo(Path, "file:/run/secrets/whatever"));

        // Assert
        Assert.False(result.IsCommitted);
        Assert.Contains(result.RefusalMessages, message => message.Contains(Path, StringComparison.Ordinal));
        Assert.Contains(deployment.WriterRecords, message => message.Contains("was refused", StringComparison.Ordinal));
        Assert.DoesNotContain(deployment.WriterRecords, message => message.Contains(Path, StringComparison.Ordinal));
    }

    /// <summary>A write states at least one change, and states no more than the boundary accepts.</summary>
    [Fact]
    public async Task WriteAsync_NoChanges_IsRefusedAsACallersMistake()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => deployment.Writer.WriteAsync(
            [],
            expectedVersion: 4,
            TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// More changes than the boundary accepts is the other half of that bound, and it is refused before anything is
    /// read: the ceiling is what keeps one request from composing an unbounded document, so it holds whatever the
    /// changes themselves would have produced.
    /// </summary>
    [Fact]
    public async Task WriteAsync_MoreChangesThanTheBoundaryAccepts_IsRefusedAsACallersMistake()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);
        var edits = Enumerable.Range(0, IConfigurationWriter.MaximumEdits + 1)
            .Select(position => ConfigurationEdit.SetTo($"Deployment:Padding:{position}", "x"))
            .ToArray();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => deployment.Writer.WriteAsync(
            edits,
            expectedVersion: 4,
            TestContext.Current.CancellationToken));
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>A version no document ever stood at is a caller's mistake rather than a refused write.</summary>
    [Fact]
    public async Task WriteAsync_ANegativeVersion_IsRefusedAsACallersMistake()
    {
        // Arrange
        using var deployment = Deployment.WithPersisted("{}", version: 4);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => deployment.Writer.WriteAsync(
            [ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3")],
            expectedVersion: -1,
            TestContext.Current.CancellationToken));
    }

    /// <summary>A deployment holding the persisted layer, the row beneath it, and the writer between the two.</summary>
    private sealed class Deployment : IDisposable
    {
        private readonly ConfigurationManager configuration;
        private readonly RootSettingsConfigurationSource layer;
        private readonly RecordingLogger<RootSettingsWriter> writerLogger = new();

        private Deployment(
            ConfigurationManager configuration,
            RootSettingsConfigurationSource layer,
            InMemoryRootSettingsRow row)
        {
            this.configuration = configuration;
            this.layer = layer;
            this.Row = row;

            this.Writer = new RootSettingsWriter(
                row,
                row,
                new CandidateConfigurationComposer(configuration, layer),
                new CandidateSettingsValidator(new FakeTimeProvider(AnyInstant), []),
                new RootSettingsReloader(layer.Provider, row, new RecordingLogger<RootSettingsReloader>()),
                new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
                this.writerLogger);
        }

        public RootSettingsWriter Writer { get; }

        public InMemoryRootSettingsRow Row { get; }

        public RootSettingsConfigurationProvider Layer => this.layer.Provider;

        /// <summary>Reads a setting as the published layer answers it, which is what a commit and a reload move together.</summary>
        /// <remarks>
        /// Asked of the provider rather than of the row, because the two are what the assertion is about: a reload that
        /// published a stale document while the row moved on is exactly the regression a comparison of the row with
        /// itself cannot see.
        /// </remarks>
        public string? PublishedValueOf(string key)
        {
            this.Layer.TryGet(key, out var published);

            return published;
        }

        /// <summary>Gets what the write recorded for an operator to read.</summary>
        public IReadOnlyList<string> WriterRecords => this.writerLogger.Messages;

        /// <summary>Composes a deployment whose only sources are the persisted layer and, when named, one an operator supplied.</summary>
        /// <remarks>
        /// The override is layered in as the user-secrets file, because that is what the layer's own insertion point
        /// recognizes as the lowest of the operator's sources — which puts the candidate below it exactly as a real
        /// host does.
        /// </remarks>
        public static Deployment WithPersisted(string persisted, long version, string? operatorOverride = null)
        {
            var configuration = new ConfigurationManager();
            var files = new InMemoryConfigurationFileProvider();

            if (operatorOverride is not null)
            {
                files.WithFile("secrets.json", operatorOverride);
                configuration.AddJsonFile(files, "secrets.json", optional: false, reloadOnChange: false);
            }

            configuration.AddRootSettings(new RootSettingsDocument(persisted, version));

            var layer = configuration.Sources.OfType<RootSettingsConfigurationSource>().Last();

            return new Deployment(configuration, layer, new InMemoryRootSettingsRow(persisted, version));
        }

        public Task<ConfigurationWriteResult> WriteAsync(params ConfigurationEdit[] edits) =>
            this.WriteAsync(this.Row.Version, edits);

        public Task<ConfigurationWriteResult> WriteAsync(CancellationToken cancellationToken, params ConfigurationEdit[] edits) =>
            this.Writer.WriteAsync(edits, this.Row.Version, cancellationToken);

        public Task<ConfigurationWriteResult> WriteAsync(long expectedVersion, params ConfigurationEdit[] edits) =>
            this.Writer.WriteAsync(edits, expectedVersion, TestContext.Current.CancellationToken);

        public void Dispose() => this.configuration.Dispose();
    }
}
