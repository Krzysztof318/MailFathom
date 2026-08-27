// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Configuration;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Administration;

/// <summary>
/// Covers what an administrator's change does to the deployment. The real writer is composed behind it rather than a
/// substitute, because the three things this layer adds — refusing a write nothing will read, dropping a change that
/// changes nothing, and reporting the effective value on both sides — are only worth anything if the commit beneath
/// them is the real one.
/// </summary>
public sealed class PersistedSettingsAdministrationTests
{
    /// <summary>A change the deployment binds commits, and both readings of the setting are reported.</summary>
    [Fact]
    public async Task ApplyAsync_AChangeTheConfigurationBinds_CommitsAndReportsBothReadings()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: "{}");

        // Act
        var outcome = await deployment.ApplyAsync(ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "5"));

        // Assert
        Assert.True(outcome.Committed);
        Assert.Equal(2, outcome.Version);

        var change = Assert.Single(outcome.Changes);
        Assert.Equal("2", change.Before?.Value);
        Assert.Equal(SettingSource.File, change.Before?.Source);
        Assert.Equal("5", change.After?.Value);
        Assert.Equal(SettingSource.PersistedLayer, change.After?.Source);
    }

    /// <summary>
    /// A write to a setting an outranking source supplies is refused, because it would commit and change nothing this
    /// deployment reads. The refusal names the source, which is where the operator actually changes the value.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ASettingAnOverrideSupplies_IsRefusedNamingTheSource()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: "{}",
            persisted: "{}",
            operatorOverride: """{ "MailboxSearch": { "SnippetsPerEmail": "9" } }""");

        // Act
        var outcome = await deployment.ApplyAsync(ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "5"));

        // Assert
        Assert.False(outcome.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationWriteShadowed, outcome.Refusal);
        Assert.Contains(
            outcome.Messages,
            message => message.Contains(SettingSource.UserSecrets.Name, StringComparison.Ordinal));
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>
    /// Staging a value beneath an override about to be removed is a thing an operator means, so the refusal is
    /// answerable rather than absolute.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_AShadowedSettingStatedDeliberately_Commits()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: "{}",
            persisted: "{}",
            operatorOverride: """{ "MailboxSearch": { "SnippetsPerEmail": "9" } }""");

        // Act
        var outcome = await deployment.ApplyAsync(
            evenIfShadowed: true,
            ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "5"));

        // Assert
        Assert.True(outcome.Committed);

        // The deployment goes on reading the override, which is exactly what the refusal was about.
        Assert.Equal("9", Assert.Single(outcome.Changes).After?.Value);
    }

    /// <summary>Persisting the value the document already carries costs no version, because nothing would change.</summary>
    [Fact]
    public async Task ApplyAsync_TheValueTheDocumentAlreadyCarries_WritesNothing()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: "{}",
            persisted: """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""");

        // Act
        var outcome = await deployment.ApplyAsync(ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "5"));

        // Assert
        Assert.False(outcome.Committed);
        Assert.False(outcome.Refusal.IsSpecified);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>Removing a setting the layer never carried is understood and leaves the document alone.</summary>
    [Fact]
    public async Task ApplyAsync_ARemovalOfASettingTheLayerDoesNotCarry_WritesNothing()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: "{}");

        // Act
        var outcome = await deployment.ApplyAsync(ConfigurationEdit.Removing("MailboxSearch:SnippetsPerEmail"));

        // Assert
        Assert.False(outcome.Committed);
        Assert.False(outcome.Refusal.IsSpecified);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>A removal restores the file beneath the layer rather than shadowing it with an empty value.</summary>
    [Fact]
    public async Task ApplyAsync_ARemoval_LetsTheFileBeneathSupplyTheSettingAgain()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""");

        // Act
        var outcome = await deployment.ApplyAsync(ConfigurationEdit.Removing("MailboxSearch:SnippetsPerEmail"));

        // Assert
        Assert.True(outcome.Committed);

        var change = Assert.Single(outcome.Changes);
        Assert.Equal("5", change.Before?.Value);
        Assert.Equal("2", change.After?.Value);
        Assert.Equal(SettingSource.File, change.After?.Source);
    }

    /// <summary>A buffer saved exactly as it was opened writes nothing, whatever the document happens to hold.</summary>
    [Fact]
    public async Task ApplyDocumentAsync_AnUnchangedBuffer_WritesNothing()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: "{}",
            persisted: """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""");

        var opened = await deployment.ReadDocumentAsync();

        // Act
        var outcome = await deployment.ApplyDocumentAsync(opened.Json, opened.Version);

        // Assert
        Assert.False(outcome.Committed);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>
    /// A secret left at the marker is left exactly as it was, which is what makes an editing session safe over a
    /// document carrying one: the buffer never had the reference to save back.
    /// </summary>
    [Fact]
    public async Task ApplyDocumentAsync_ASecretLeftAtTheMarker_LeavesTheReferenceStanding()
    {
        // Arrange
        const string persisted = """
            {
              "Chat": {
                "Alias": "primary",
                "Address": "https://models.example/",
                "Model": "small",
                "ApiKey": { "Name": "chat-key", "SecretReference": "file:/run/secrets/chat" }
              }
            }
            """;

        using var deployment = Composed(provisioned: "{}", persisted: persisted);
        var opened = await deployment.ReadDocumentAsync();

        // Act
        var outcome = await deployment.ApplyDocumentAsync(
            opened.Json.Replace("\"chat-key\"", "\"chat-key-renamed\"", StringComparison.Ordinal),
            opened.Version);

        // Assert
        Assert.True(outcome.Committed);
        Assert.Contains("file:/run/secrets/chat", deployment.Row.Json, StringComparison.Ordinal);
        Assert.DoesNotContain(SettingRedaction.Marker, deployment.Row.Json, StringComparison.Ordinal);
    }

    /// <summary>A setting the operator deleted from the buffer stops being persisted.</summary>
    [Fact]
    public async Task ApplyDocumentAsync_ASettingDeletedFromTheBuffer_StopsBeingCarried()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: "{}",
            persisted: """{ "MailboxSearch": { "SnippetsPerEmail": "5", "WordsPerSnippet": "12" } }""");

        var opened = await deployment.ReadDocumentAsync();

        // Act
        var outcome = await deployment.ApplyDocumentAsync(
            """{ "MailboxSearch": { "WordsPerSnippet": "12" } }""",
            opened.Version);

        // Assert
        Assert.True(outcome.Committed);
        Assert.DoesNotContain("SnippetsPerEmail", deployment.Row.Json, StringComparison.Ordinal);
    }

    /// <summary>An editing session composed over a version somebody else has passed is refused rather than merged.</summary>
    [Fact]
    public async Task ApplyDocumentAsync_AVersionSomebodyElseMovedPast_IsRefused()
    {
        // Arrange
        using var deployment = Composed(provisioned: "{}", persisted: "{}");
        var opened = await deployment.ReadDocumentAsync();

        deployment.Row.CommitFromElsewhere("""{ "MailboxSearch": { "WordsPerSnippet": "12" } }""");

        // Act
        var outcome = await deployment.ApplyDocumentAsync(
            """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""",
            opened.Version);

        // Assert
        Assert.False(outcome.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, outcome.Refusal);
        Assert.Equal(deployment.Row.Version, outcome.Version);
    }

    /// <summary>A buffer that is not a document of configuration settings is refused, and the message says so.</summary>
    [Fact]
    public async Task ApplyDocumentAsync_ABufferThatIsNotAConfigurationDocument_IsRefused()
    {
        // Arrange
        using var deployment = Composed(provisioned: "{}", persisted: "{}");

        // Act
        var outcome = await deployment.ApplyDocumentAsync("this is not JSON", deployment.Row.Version);

        // Assert
        Assert.False(outcome.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome.Refusal);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>An adoption offers what the files decide and leaves alone what the layer already carries.</summary>
    [Fact]
    public void ReadAdoptable_ASettingTheLayerAlreadyCarries_IsNotOffered()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2", "WordsPerSnippet": "12" } }""",
            persisted: """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""");

        // Act
        var adoptable = deployment.Administration.ReadAdoptable("MailboxSearch");

        // Assert
        var setting = Assert.Single(adoptable.Settings);
        Assert.Equal("MailboxSearch:WordsPerSnippet", setting.Path);
    }

    /// <summary>An adoption persists what the files supplied, so the file stops deciding the setting.</summary>
    [Fact]
    public async Task AdoptAsync_ASettingAFileDecides_PersistsTheFileValue()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: "{}");

        // Act
        var outcome = await deployment.Administration.AdoptAsync(
            "MailboxSearch",
            deployment.Row.Version,
            evenIfShadowed: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(outcome.Committed);

        var change = Assert.Single(outcome.Changes);
        Assert.Equal("2", change.After?.Value);
        Assert.Equal(SettingSource.PersistedLayer, change.After?.Source);
    }

    /// <summary>An adoption of a path the files decide nothing beneath is understood and writes nothing.</summary>
    [Fact]
    public async Task AdoptAsync_APathTheFilesDecideNothingBeneath_WritesNothing()
    {
        // Arrange
        using var deployment = Composed(provisioned: "{}", persisted: "{}");

        // Act
        var outcome = await deployment.Administration.AdoptAsync(
            "MailboxSearch",
            deployment.Row.Version,
            evenIfShadowed: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(outcome.Committed);
        Assert.False(outcome.Refusal.IsSpecified);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>The document an editing session opens carries no secret material, whatever the row holds.</summary>
    [Fact]
    public async Task ReadDocumentAsync_ADocumentCarryingASecret_ReportsTheMarker()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: "{}",
            persisted: """{ "Chat": { "ApiKey": { "SecretReference": "file:/run/secrets/chat" } } }""");

        // Act
        var document = await deployment.ReadDocumentAsync();

        // Assert
        Assert.DoesNotContain("/run/secrets/chat", document.Json, StringComparison.Ordinal);
        Assert.Contains(SettingRedaction.Marker, document.Json, StringComparison.Ordinal);
    }


    /// <summary>
    /// A marker stands for the value at the path it was saved at, so a save that moved the path it sits at is refused
    /// rather than committed. An array position is the one part of a path an edit can move: deleting the first account
    /// leaves the second one's marker sitting where the first one's stood, and placing it would write one mailbox's
    /// credential onto another mailbox.
    /// </summary>
    [Fact]
    public async Task ApplyDocumentAsync_AMarkerWhoseArrayElementMoved_IsRefusedNamingTheElement()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: "{}",
            persisted: """
                {
                  "MailSynchronization": {
                    "Accounts": [
                      { "AccountId": "first", "Secrets": { "Password": { "SecretReference": "file:/run/secrets/first" } } },
                      { "AccountId": "second", "Secrets": { "Password": { "SecretReference": "file:/run/secrets/second" } } }
                    ]
                  }
                }
                """);

        var opened = await deployment.ReadDocumentAsync();

        // Act
        var outcome = await deployment.ApplyDocumentAsync(
            """
            {
              "MailSynchronization": {
                "Accounts": [
                  { "AccountId": "second", "Secrets": { "Password": { "SecretReference": "(redacted)" } } }
                ]
              }
            }
            """,
            opened.Version);

        // Assert
        Assert.False(outcome.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome.Refusal);
        Assert.Equal(0, deployment.Row.AcceptedCommits);

        var refusal = Assert.Single(outcome.Messages);
        Assert.Contains("MailSynchronization:Accounts:0", refusal, StringComparison.Ordinal);
        Assert.Contains("mfctl config set", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The marker means "the value the document already carries here", so writing one where the document carries
    /// nothing names no value at all. Committing it would persist the marker's own text as though it were deliberate.
    /// </summary>
    [Fact]
    public async Task ApplyDocumentAsync_AMarkerWhereTheDocumentCarriesNothing_IsRefused()
    {
        // Arrange
        using var deployment = Composed(provisioned: "{}", persisted: "{}");
        var opened = await deployment.ReadDocumentAsync();

        // Act
        var outcome = await deployment.ApplyDocumentAsync(
            """{ "Chat": { "ApiKey": { "SecretReference": "(redacted)" } } }""",
            opened.Version);

        // Assert
        Assert.False(outcome.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome.Refusal);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
        Assert.Contains(
            "Chat:ApiKey:SecretReference",
            Assert.Single(outcome.Messages),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A bootstrap-only setting is one the persisted layer may not carry, so offering it for adoption would preview a
    /// change the commit behind it refuses. What the preview leaves out is what the operator would otherwise take for
    /// an adoption that half worked.
    /// </summary>
    [Fact]
    public void ReadAdoptable_ABootstrapOnlySettingAFileDecides_IsNotOffered()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: """{ "Persistence": { "CommandTimeoutSeconds": "45", "TextSearchConfiguration": "english" } }""",
            persisted: "{}");

        // Act
        var adoptable = deployment.Administration.ReadAdoptable("Persistence");

        // Assert
        var setting = Assert.Single(adoptable.Settings);
        Assert.Equal("Persistence:TextSearchConfiguration", setting.Path);
    }

    /// <summary>
    /// Every reading asks for the permission it is published under with the transport absent, so a route filter is not
    /// the only thing standing between a credential and the deployment's own configuration — which is where a
    /// connection string, a secret reference, and every other credential's grant are named.
    /// </summary>
    [Fact]
    public async Task Read_ACallerGrantedNothing_IsRefusedOnEveryReading()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: "{}",
            persisted: "{}",
            granted: []);

        // Act & Assert
        Assert.Throws<PrincipalNotAuthorizedException>(() => deployment.Administration.Read(prefix: null));
        Assert.Throws<PrincipalNotAuthorizedException>(() => deployment.Administration.ReadAdoptable("MailboxSearch"));
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => deployment.ReadDocumentAsync());
    }

    /// <summary>
    /// Reading the configuration and changing it are separate grants, so a credential narrowed to the reading half is
    /// refused by every write rather than by the route alone.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ACallerGrantedOnlyTheReading_IsRefusedOnEveryWrite()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: "{}",
            persisted: "{}",
            granted: [MailFathomPermission.AdminRead]);

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            deployment.ApplyAsync(ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3")));
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            deployment.ApplyDocumentAsync("{}", deployment.Row.Version));
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            deployment.Administration.AdoptAsync(
                "MailboxSearch",
                deployment.Row.Version,
                evenIfShadowed: false,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>Composes the deployment these tests write against.</summary>
    private static ComposedConfigurationDeployment Composed(
        string provisioned,
        string persisted,
        string? operatorOverride = null) =>
        ComposedConfigurationDeployment.Composed(provisioned, persisted, operatorOverride);
}
