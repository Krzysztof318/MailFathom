// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
    /// document carrying one: the buffer never had the reference to save back. The edit is outside the block the secret
    /// belongs to, which is what a save leaving a marker may change — everything inside that block is what the two
    /// tests below refuse.
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
              },
              "MailboxSearch": { "WordsPerSnippet": "12" }
            }
            """;

        using var deployment = Composed(provisioned: "{}", persisted: persisted);
        var opened = await deployment.ReadDocumentAsync();

        // Act
        var outcome = await deployment.ApplyDocumentAsync(
            opened.Json.Replace("\"12\"", "\"14\"", StringComparison.Ordinal),
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
    /// The same rule where the operator keyed the collection by name rather than by position. Nothing in the path says
    /// which of the two a segment is — the binder builds the list from whatever child keys it finds — so a save
    /// repointing this mailbox at another server while leaving its credential at the marker would otherwise commit,
    /// and the next start would present the provisioned credential to that server.
    /// </summary>
    [Fact]
    public async Task ApplyDocumentAsync_AMarkerWhoseNameKeyedElementChanged_IsRefusedNamingTheElement()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: "{}",
            persisted: """
                {
                  "MailSynchronization": {
                    "Accounts": {
                      "work": {
                        "AccountId": "work",
                        "Host": "imap.example.test",
                        "Secrets": { "Password": { "SecretReference": "file:/run/secrets/work" } }
                      }
                    }
                  }
                }
                """);

        var opened = await deployment.ReadDocumentAsync();

        // Act
        var outcome = await deployment.ApplyDocumentAsync(
            opened.Json.Replace("imap.example.test", "imap.elsewhere.test", StringComparison.Ordinal),
            opened.Version);

        // Assert
        Assert.False(outcome.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome.Refusal);
        Assert.Equal(0, deployment.Row.AcceptedCommits);

        var refusal = Assert.Single(outcome.Messages);

        Assert.Contains("MailSynchronization:Accounts:work", refusal, StringComparison.Ordinal);
        Assert.Contains("mfctl config set", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A credential belongs to whatever it is presented to, and the block it sits in is what says which that is. So a
    /// save changing the address beside a key left at the marker is the same refusal as one changing an element around
    /// it: neither can be committed on the strength of a path that no longer means what it did.
    /// </summary>
    [Fact]
    public async Task ApplyDocumentAsync_AMarkerWhoseOwnBlockWasRepointed_IsRefusedNamingTheBlock()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: "{}",
            persisted: """
                {
                  "Chat": {
                    "Alias": "primary",
                    "Address": "https://models.example/",
                    "Model": "small",
                    "ApiKey": { "Name": "chat-key", "SecretReference": "file:/run/secrets/chat" }
                  }
                }
                """);

        var opened = await deployment.ReadDocumentAsync();

        // Act
        var outcome = await deployment.ApplyDocumentAsync(
            opened.Json.Replace("https://models.example/", "https://models.elsewhere/", StringComparison.Ordinal),
            opened.Version);

        // Assert
        Assert.False(outcome.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome.Refusal);

        var refusal = Assert.Single(outcome.Messages);

        Assert.Contains("'Chat'", refusal, StringComparison.Ordinal);
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
    /// The other half of what the writer refuses on: a setting the storage catalog routes somewhere other than the
    /// root document. The top-level <c>Accounts</c> section is one owner document per owner rather than a settings
    /// row, so adopting it could only ever be refused — and the catalog is built to grow, which is why the preview
    /// asks it rather than restating the rule.
    /// </summary>
    [Fact]
    public void ReadAdoptable_ASettingTheCatalogPersistsOutsideTheRootDocument_IsNotOffered()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: """{ "Accounts": { "owner": { "Address": "someone@example.test" } } }""",
            persisted: "{}");

        // Act
        var adoptable = deployment.Administration.ReadAdoptable("Accounts");

        // Assert
        Assert.Empty(adoptable.Settings);
    }

    /// <summary>
    /// The adoption's own no-writer branch, which leaves without reaching the writer exactly as the keyed change's
    /// does. A replica that has not itself written composes the layer it started with, so it finds nothing adoptable
    /// beneath a prefix the row actually needs — and would otherwise answer that with an exit code saying it
    /// succeeded, over a version the operator's next write is then refused as superseded.
    /// </summary>
    [Fact]
    public async Task AdoptAsync_NothingAdoptableComposedOverASupersededVersion_IsRefusedRatherThanReportedAsNothingToAdopt()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "3" } }""",
            persisted: """{ "MailboxSearch": { "SnippetsPerEmail": "3" } }""",
            version: 3);

        // Act
        var refused = await deployment.Administration.AdoptAsync(
            "MailboxSearch",
            expectedVersion: 2,
            evenIfShadowed: false,
            TestContext.Current.CancellationToken);

        var reported = await deployment.AdoptAsync("MailboxSearch");

        // Assert
        Assert.False(refused.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, refused.Refusal);
        Assert.Equal(3, refused.Version);

        // The same preview over the version in force is what the branch is actually for.
        Assert.False(reported.Committed);
        Assert.False(reported.Refusal.IsSpecified);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
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


    /// <summary>
    /// The adoption commit asks for the one permission its own route publishes and no more. The preview it reads on
    /// the way past is the same code the preview route serves, so a use case reaching it through that route's entry
    /// point would refuse a caller the transport had already admitted — inside the use case, on a route whose published
    /// metadata names a permission they hold.
    /// </summary>
    [Fact]
    public async Task AdoptAsync_ACallerGrantedTheWriteAlone_Commits()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: "{}",
            granted: [MailFathomPermission.AdminConfigurationWrite]);

        // Act
        var outcome = await deployment.AdoptAsync("MailboxSearch");

        // Assert
        Assert.True(outcome.Committed);
        Assert.Equal("2", Assert.Single(outcome.Changes).After?.Value);
    }

    /// <summary>The preview is the reading half and stays published under the reading permission.</summary>
    [Fact]
    public void ReadAdoptable_ACallerGrantedTheWriteAlone_IsRefused()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: "{}",
            granted: [MailFathomPermission.AdminConfigurationWrite]);

        // Act & Assert
        Assert.Throws<PrincipalNotAuthorizedException>(() => deployment.Administration.ReadAdoptable("MailboxSearch"));
    }

    /// <summary>
    /// Staging a value beneath an override about to be removed is a thing an operator means on every command that
    /// writes, not only on the keyed one. This is the saved-buffer half of that: without it, the endpoint and the
    /// command could both drop the flag on the way through and <c>mfctl config edit --even-if-shadowed</c> would be
    /// refused under <c>12013</c> forever with every test still passing.
    /// </summary>
    [Fact]
    public async Task ApplyDocumentAsync_AShadowedSettingStatedDeliberately_Commits()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: "{}",
            persisted: "{}",
            operatorOverride: """{ "MailboxSearch": { "SnippetsPerEmail": "9" } }""");

        var opened = await deployment.ReadDocumentAsync();

        // Act
        var outcome = await deployment.ApplyDocumentAsync(
            """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""",
            opened.Version,
            evenIfShadowed: true);

        // Assert
        Assert.True(outcome.Committed);
        Assert.Equal(1, deployment.Row.AcceptedCommits);
    }

    /// <summary>A saved buffer that stages a shadowed setting without saying so is refused, naming the source.</summary>
    [Fact]
    public async Task ApplyDocumentAsync_AShadowedSettingNotStated_IsRefusedNamingTheSource()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: "{}",
            persisted: "{}",
            operatorOverride: """{ "MailboxSearch": { "SnippetsPerEmail": "9" } }""");

        var opened = await deployment.ReadDocumentAsync();

        // Act
        var outcome = await deployment.ApplyDocumentAsync(
            """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""",
            opened.Version);

        // Assert
        Assert.False(outcome.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationWriteShadowed, outcome.Refusal);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>The adoption half of the same flag, which no other test reaches.</summary>
    [Fact]
    public async Task AdoptAsync_AShadowedSettingStatedDeliberately_Commits()
    {
        // Arrange
        using var deployment = Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: "{}",
            operatorOverride: """{ "MailboxSearch": { "SnippetsPerEmail": "9" } }""");

        // Act
        var refused = await deployment.AdoptAsync("MailboxSearch");
        var committed = await deployment.AdoptAsync("MailboxSearch", evenIfShadowed: true);

        // Assert
        Assert.False(refused.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationWriteShadowed, refused.Refusal);
        Assert.True(committed.Committed);
        Assert.Equal(1, deployment.Row.AcceptedCommits);

        // The setting still reads as the override supplies it, which is the whole of what the flag says an operator
        // means: the value is staged beneath an override they are about to remove, and until they do the reading is
        // unchanged.
        var change = Assert.Single(committed.Changes);
        Assert.Equal("9", change.After?.Value);
        Assert.Equal(SettingSource.UserSecrets, change.After?.Source);
    }

    /// <summary>
    /// An adoption refuses a prefix beneath which the files decide more settings than one adoption carries. Nothing
    /// else reaches this branch: a too-broad reading carries an empty settings list, so a dropped or inverted guard
    /// leaves no edits at all and the adoption would answer that the files supply nothing beneath the prefix — a false
    /// statement about the operator's own configuration, made with an exit code that says it succeeded.
    /// </summary>
    [Fact]
    public async Task AdoptAsync_APrefixTheFilesDecideMoreSettingsBeneathThanOneAdoptionCarries_RefusesWithTheCount()
    {
        // Arrange
        var beyondTheBound = EffectiveSettingsReader.MaximumSettings + 1;

        using var deployment = Composed(
            provisioned: SectionOf("Wide", beyondTheBound),
            persisted: "{}");

        // Act
        var outcome = await deployment.AdoptAsync("Wide");

        // Assert
        Assert.False(outcome.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome.Refusal);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
        Assert.Contains(
            outcome.Messages,
            message => message.Contains(
                $"The files supply {beyondTheBound} settings beneath 'Wide'",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// A keyed change that alters nothing is the one branch a write leaves by without reaching the writer, so the
    /// version guard is applied here rather than inherited from it. The case is a replica that has not itself written:
    /// nothing on the read path reloads the layer, so it goes on composing the document it started with and would
    /// otherwise answer that a change the row needs is already in place — with its own stale version, which the writer
    /// then refuses as superseded on the operator's next attempt.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_AChangeAlteringNothingComposedOverASupersededVersion_IsRefusedRatherThanReportedAsNothingToDo()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: "{}",
            persisted: """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""",
            version: 3);

        // Act
        var refused = await deployment.Administration.ApplyAsync(
            [ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "5")],
            expectedVersion: 2,
            evenIfShadowed: false,
            TestContext.Current.CancellationToken);

        var reported = await deployment.ApplyAsync(ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "5"));

        // Assert
        Assert.False(refused.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationVersionSuperseded, refused.Refusal);
        Assert.Equal(3, refused.Version);

        // The same change over the version in force is what the branch is actually for.
        Assert.False(reported.Committed);
        Assert.False(reported.Refusal.IsSpecified);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
    }

    /// <summary>
    /// An adoption beneath the reading bound can still be past what one write carries, and the window between the two
    /// is live: a thousand edits is half of the two thousand settings a reading answers with. Nothing else reaches this
    /// branch, and dropping it would hand the writer a change it refuses as an argument failure — which arrives at the
    /// operator as a failed request rather than as the sentence naming the bound.
    /// </summary>
    [Fact]
    public async Task AdoptAsync_APrefixTheFilesDecideMoreSettingsBeneathThanOneWriteCarries_RefusesWithTheCount()
    {
        // Arrange
        var beyondTheWrite = IConfigurationWriter.MaximumEdits + 1;

        using var deployment = Composed(
            provisioned: SectionOf("Wide", beyondTheWrite),
            persisted: "{}");

        // Act
        var outcome = await deployment.AdoptAsync("Wide");

        // Assert
        Assert.False(outcome.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome.Refusal);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
        Assert.Contains(
            outcome.Messages,
            message => message.Contains(
                $"would write {beyondTheWrite} settings in one change",
                StringComparison.Ordinal));
    }

    /// <summary>The same bound on the other surface that composes its edits from a document rather than from a caller's list.</summary>
    [Fact]
    public async Task ApplyDocumentAsync_ABufferDifferingInMoreSettingsThanOneWriteCarries_RefusesWithTheCount()
    {
        // Arrange
        var beyondTheWrite = IConfigurationWriter.MaximumEdits + 1;

        using var deployment = Composed(provisioned: "{}", persisted: "{}");

        // Act
        var outcome = await deployment.ApplyDocumentAsync(
            SectionOf("Wide", beyondTheWrite),
            deployment.Row.Version);

        // Assert
        Assert.False(outcome.Committed);
        Assert.Equal(MailFathomErrorCode.ConfigurationCandidateInvalid, outcome.Refusal);
        Assert.Equal(0, deployment.Row.AcceptedCommits);
        Assert.Contains(
            outcome.Messages,
            message => message.Contains(
                $"differs from the document in force in {beyondTheWrite} settings",
                StringComparison.Ordinal));
    }

    /// <summary>Composes the deployment these tests write against.</summary>
    private static ComposedConfigurationDeployment Composed(
        string provisioned,
        string persisted,
        string? operatorOverride = null) =>
        ComposedConfigurationDeployment.Composed(provisioned, persisted, operatorOverride);

    /// <summary>Composes one section holding the stated number of settings, each a value of its own.</summary>
    private static string SectionOf(string section, int settings)
    {
        var written = string.Join(
            ", ",
            Enumerable.Range(0, settings).Select(position => $"\"{position}\": \"value\""));

        return $$"""{ "{{section}}": { {{written}} } }""";
    }
}
