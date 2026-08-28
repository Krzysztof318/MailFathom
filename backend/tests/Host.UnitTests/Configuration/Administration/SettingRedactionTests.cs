// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Administration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Administration;

/// <summary>
/// Covers what a configuration reading may disclose. The rule is the one the writer refuses material by, so the two
/// cannot disagree about what a secret is — a value a write would refuse to persist must not be a value a read hands
/// back.
/// </summary>
public sealed class SettingRedactionTests
{
    /// <summary>The last segment is the property name a setting binds to, and it is what announces a secret.</summary>
    [Theory]
    [InlineData("MailSynchronization:Accounts:0:Secrets:Password:SecretReference")]
    [InlineData("Persistence:Password")]
    [InlineData("Ai:Providers:0:ApiKey")]
    public void Redacts_ASettingNamingASecret_ReportsTrue(string path) => Assert.True(SettingRedaction.Redacts(path));

    /// <summary>
    /// The second half of the rule, and the one no property name announces: a bootstrap-only setting is where the
    /// deployment is reached from — its database, the file its own configuration is read out of, how it interprets a
    /// secret reference — and it is redacted because it is that rather than because of what it is called. Reading a
    /// connection string names the host, the database, and often the user; reading the directory names where the
    /// material on disk is. Neither last segment carries a word <c>SecretPropertyNaming</c> would recognize, so a rule
    /// written against the name alone would hand every one of them back.
    /// </summary>
    [Theory]
    [InlineData("ConnectionStrings:mailfathom")]
    [InlineData("Persistence:CommandTimeoutSeconds")]
    [InlineData("ConfigurationSources:Directory")]
    [InlineData("Secrets:Interpretation:Scheme")]
    public void Redacts_ABootstrapOnlySetting_ReportsTrue(string path) => Assert.True(SettingRedaction.Redacts(path));

    /// <summary>
    /// A name that locates something rather than holding it is not a secret, and neither is the handle beside one. The
    /// second is what makes a redacted document still readable: an operator sees which secret a setting names without
    /// seeing where its material is kept.
    /// </summary>
    [Theory]
    [InlineData("Ai:Providers:0:TokenEndpoint")]
    [InlineData("MailSynchronization:Accounts:0:Secrets:Password:Name")]
    [InlineData("MailSynchronization:Accounts:0:Secrets:Password:Lifetime")]
    [InlineData("MailboxSearch:SnippetsPerEmail")]
    public void Redacts_ASettingHoldingNoSecret_ReportsFalse(string path) =>
        Assert.False(SettingRedaction.Redacts(path));

    /// <summary>A secret-bearing value is replaced by the marker, and every other value is reported as it stands.</summary>
    [Fact]
    public void Apply_ASecretBearingSetting_ReportsTheMarker()
    {
        // Act
        var redacted = SettingRedaction.Apply("Persistence:Password", "file:/run/secrets/postgres");
        var reported = SettingRedaction.Apply("MailboxSearch:SnippetsPerEmail", "3");

        // Assert
        Assert.Equal(SettingRedaction.Marker, redacted);
        Assert.Equal("3", reported);
    }

    /// <summary>
    /// The marker carries no colon, so it parses as a reference to no scheme. That is what makes it safe to leave in a
    /// buffer an operator saves: written into a setting that never bore one, the writer refuses it as material rather
    /// than persisting a value that looks deliberate.
    /// </summary>
    [Fact]
    public void Marker_NamesNoSecretScheme() =>
        Assert.DoesNotContain(":", SettingRedaction.Marker, StringComparison.Ordinal);

    /// <summary>A document reports every secret-bearing value as the marker, wherever it sits.</summary>
    [Fact]
    public void ApplyToDocument_ASecretInsideAnArrayElement_ReportsTheMarker()
    {
        // Arrange
        const string persisted = """
            {
              "MailSynchronization": {
                "Accounts": [
                  {
                    "AccountId": "primary",
                    "Secrets": { "Password": { "Name": "primary", "SecretReference": "file:/run/secrets/imap" } }
                  }
                ]
              }
            }
            """;

        // Act
        var redacted = SettingRedaction.ApplyToDocument(persisted);

        // Assert
        Assert.DoesNotContain("/run/secrets/imap", redacted, StringComparison.Ordinal);
        Assert.Contains(SettingRedaction.Marker, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyToDocument_ADatabaseSecretReference_ReportsTheMarkerRatherThanTheStoredIdentifier()
    {
        // Arrange
        const string persisted =
            """{ "MailAccounts": [{ "Secrets": { "Password": { "SecretReference": "database:019925df-96f4-7c6d-8f91-b9f6cf27f5b2" } } }] }""";

        // Act
        var redacted = SettingRedaction.ApplyToDocument(persisted);

        // Assert
        Assert.DoesNotContain("019925df", redacted, StringComparison.Ordinal);
        Assert.Contains(SettingRedaction.Marker, redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// Everything that is not a secret is handed over unchanged, including the handle a secret is named by. The secret
    /// here is a chat provider's key rather than the database's, because a database setting is bootstrap-only and the
    /// rule above redacts the whole of one — handle included, since a bootstrap-only setting is withheld for where it
    /// points rather than for what its last segment is called.
    /// </summary>
    [Fact]
    public void ApplyToDocument_ASettingHoldingNoSecret_CarriesItThrough()
    {
        // Arrange
        const string persisted = """
            {
              "MailboxSearch": { "SnippetsPerEmail": "3" },
              "Chat": { "ApiKey": { "Name": "chat-provider", "SecretReference": "file:/run/secrets/chat" } }
            }
            """;

        // Act
        var redacted = SettingRedaction.ApplyToDocument(persisted);

        // Assert
        Assert.Contains("\"SnippetsPerEmail\"", redacted, StringComparison.Ordinal);
        Assert.Contains("chat-provider\"", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("/run/secrets/chat", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bootstrap-only setting is withheld whole, so the handle beside one goes with it. That is the one place the
    /// rule above stops holding, and it is deliberate: what a database secret is called is part of what an operator
    /// would need in order to reach the database, and the persisted layer refuses to carry the setting either way.
    /// </summary>
    [Fact]
    public void ApplyToDocument_ABootstrapOnlySettingsHandle_IsWithheldWithIt()
    {
        // Arrange
        const string persisted = """
            {
              "Persistence": { "Password": { "Name": "postgres", "SecretReference": "file:/run/secrets/postgres" } }
            }
            """;

        // Act
        var redacted = SettingRedaction.ApplyToDocument(persisted);

        // Assert
        Assert.DoesNotContain("postgres", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// A null leaf is left as it is, whatever its name says. It contributes no configuration key, so there is nothing
    /// there to withhold — and marking it would open an editing buffer whose marker stands for no setting, which the
    /// save then refuses for naming a path the document carries nothing at, including a save of the buffer unchanged.
    /// </summary>
    [Fact]
    public void ApplyToDocument_ASecretNamedPathHoldingNull_LeavesItAlone()
    {
        // Arrange
        const string persisted = """{ "Chat": { "ApiKey": { "SecretReference": null } } }""";

        // Act
        var redacted = SettingRedaction.ApplyToDocument(persisted);

        // Assert
        Assert.DoesNotContain(SettingRedaction.Marker, redacted, StringComparison.Ordinal);
        Assert.Contains("null", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scalar sitting directly in an array is decided by its path like any other value. A position announces no
    /// secret, so the name rule never reaches one — but the bootstrap-only rule matches a path prefix-wise, and
    /// <c>Persistence:Password:0</c> is a path a write is refused at. A hand-written row is what puts an array there,
    /// and it reaches this walk from the database rather than through the layer that would have refused it.
    /// </summary>
    [Fact]
    public void ApplyToDocument_ABootstrapOnlySettingHoldingAnArrayOfScalars_WithholdsEachElement()
    {
        // Arrange
        const string persisted = """{ "Persistence": { "Password": ["postgres", "second"] } }""";

        // Act
        var redacted = SettingRedaction.ApplyToDocument(persisted);

        // Assert
        Assert.DoesNotContain("postgres", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("second", redacted, StringComparison.Ordinal);
    }

    /// <summary>An array element at a path no rule names is carried through, which is what says the assertion above reports a rule rather than the walk.</summary>
    [Fact]
    public void ApplyToDocument_AnArrayOfScalarsAtAnOrdinaryPath_CarriesEachElementThrough()
    {
        // Arrange
        const string persisted = """{ "MailboxSearch": { "Stopwords": ["ordinary", "value"] } }""";

        // Act
        var redacted = SettingRedaction.ApplyToDocument(persisted);

        // Assert
        Assert.Contains("ordinary", redacted, StringComparison.Ordinal);
        Assert.Contains("value", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(SettingRedaction.Marker, redacted, StringComparison.Ordinal);
    }

    /// <summary>A row that is not a JSON object describes no settings, and is refused rather than reported as an empty one.</summary>
    [Fact]
    public void ApplyToDocument_ADocumentThatIsNotAnObject_IsRefused() =>
        Assert.Throws<FormatException>(() => SettingRedaction.ApplyToDocument("[1, 2, 3]"));
}
