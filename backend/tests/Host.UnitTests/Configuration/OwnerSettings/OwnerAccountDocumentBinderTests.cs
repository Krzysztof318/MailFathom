// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.Infrastructure.Persistence.Settings;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.OwnerSettings;

/// <summary>
/// Asserts what an owner's document has to be before it is a record this deployment acts on. The binder is the one
/// place both directions are meant to meet — a row read back and a candidate about to be written — so every rule
/// proved here is one rule rather than a pair that could drift. This release carries no path that drives it: the read
/// bounds the column and hands the text back unjudged, and a write is still refused under <c>12006</c>, so what these
/// tests hold is the rule each direction arrives by once <c>#1223</c> and <c>#1224</c> reach it.
/// </summary>
public sealed class OwnerAccountDocumentBinderTests
{
    private const string PasswordReference = "file:/run/secrets/work-password";

    /// <summary>The instant every binding here is judged against, so a date-bound rule is decided rather than drawn.</summary>
    private static readonly DateTimeOffset Today = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An owner is provisioned before their first mailbox, so the empty record is an ordinary one.</summary>
    [Fact]
    public void Bind_EmptyDocument_IsAnOwnerWhoOwnsNoMailAccount()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("{}");

        // Assert
        Assert.True(binding.IsBound);
        Assert.Empty(binding.Owner!.MailAccounts);
    }

    /// <summary>A declaration in the record is the same declaration a file carried, bound by the same type.</summary>
    [Fact]
    public void Bind_DeclaredMailAccount_BindsItAsTheOwnersOwn()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaring(("work", "The work mailbox")));

        // Assert
        Assert.True(binding.IsBound);
        var account = Assert.Single(binding.Owner!.MailAccounts);
        Assert.Equal("work", account.AccountId);
        Assert.Equal("The work mailbox", account.DisplayName);
        Assert.Equal("imap.example.test", account.Host);
    }

    /// <summary>Within one owner the same identifier twice is a name that could select either mailbox.</summary>
    /// <remarks>
    /// Within one owner, and nowhere wider: the collision is read from the declarations of the document in front of
    /// the binder, which takes no owner and keeps nothing between calls, so two owners each declaring <c>work</c> is a
    /// pair of ordinary records. That is a property of the subject rather than a claim a test could break, which is
    /// why it is stated here instead of asserted by binding one document twice.
    /// </remarks>
    [Fact]
    public void Bind_OneOwnerDeclaringAnIdentifierTwice_IsRefused()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaring(("work", "The work mailbox"), ("work", "The other mailbox")));

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(binding.Refusals, refusal => refusal.Contains("more than one account", StringComparison.Ordinal));
    }

    /// <summary>A display name carried by another account is the ambiguity resolution would answer by first match.</summary>
    [Fact]
    public void Bind_DisplayNameAlreadyNamingAnotherAccount_IsRefused()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaring(("work", "personal"), ("personal", "The personal mailbox")));

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(binding.Refusals, refusal => refusal.Contains("could not say which mailbox it meant", StringComparison.Ordinal));
    }

    /// <summary>Every rule a mail account is declared under is applied here, not only the ones about names.</summary>
    [Fact]
    public void Bind_AccountDeclaringNoHost_IsRefused()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(
            """
            { "MailAccounts": [ { "AccountId": "work", "DisplayName": "The work mailbox", "UserName": "mailfathom@example.test" } ] }
            """);

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(binding.Refusals, refusal => refusal.Contains("IMAP host is required", StringComparison.Ordinal));
    }

    /// <summary>A property nothing binds is a setting somebody believes they wrote, so it is refused rather than dropped.</summary>
    [Fact]
    public void Bind_PropertyNothingBinds_IsRefused()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""{ "MailAccounts": [], "TrustedSenders": [] }""");

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.Contains("TrustedSenders", refusal, StringComparison.Ordinal);

        // What the framework says here names MailFathom's own type and the binder option that was set, neither of
        // which is a thing whoever wrote the record can act on.
        Assert.DoesNotContain(nameof(OwnerAccountOptions), refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("BinderOptions", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A property name is text whoever wrote the row chose, so one carrying a newline is not repeated back: the
    /// refusal an administrator reads, and any log of it, would otherwise carry a line of that record's choosing.
    /// </summary>
    [Fact]
    public void Bind_PropertyNameCarryingAControlCharacter_IsRefusedWithoutRepeatingTheName()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""{ "MailAccounts": [], "quiet\nDatabase error: reached": 1 }""");

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.DoesNotContain("Database error", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', refusal);
        Assert.Contains("does not bind to an owner's settings", refusal, StringComparison.Ordinal);
    }

    /// <summary>A property naming nothing is a record refused like any other rather than a failure thrown at a caller.</summary>
    /// <remarks>
    /// JSON admits an empty property name and the configuration parser carries it through verbatim, so a row written
    /// by hand flattens to a section whose path is the empty string. The scan for secret material runs ahead of the
    /// binding and asks a rule that refuses to be asked about a path that is not one, so a key naming nothing is
    /// passed over there and answered by the binding, which is the only way out of this class a caller handles.
    /// </remarks>
    [Theory]
    [InlineData("""{ "": 1 }""")]
    [InlineData("""{ "   ": 1 }""")]
    public void Bind_PropertyNamingNothing_IsRefusedRatherThanThrown(string json)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(json);

        // Assert
        Assert.False(binding.IsBound);
        Assert.NotEmpty(binding.Refusals);
    }

    /// <summary>
    /// A value of the wrong type names the setting to correct and never the value, which is where the framework puts
    /// it: the failure it raises quotes what it could not convert, and the setting whose value that is may be a
    /// mailbox password rather than a port.
    /// </summary>
    [Fact]
    public void Bind_ValueThatWillNotConvert_NamesTheSettingAndNotTheValue()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""{ "MailAccounts": [ { "AccountId": "work", "Port": "hunter2" } ] }""");

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.Contains("MailAccounts:0:Port", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Int32", refusal, StringComparison.Ordinal);
    }

    /// <summary>The record names where a credential is kept and never keeps one.</summary>
    [Fact]
    public void Bind_PasswordCarryingTheMaterialItself_IsRefusedWithoutRepeatingIt()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaring(("work", "The work mailbox"), passwordReference: "hunter2"));

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.Contains("MailAccounts:0:Secrets:Password:SecretReference", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal names the setting only when the whole path is one, because the rule that finds material reads the
    /// last segment and an earlier one is an owner's own text — a place to forge a sentence an administrator would
    /// read as MailFathom's.
    /// </summary>
    [Fact]
    public void Bind_MaterialUnderAPathCarryingAControlCharacter_IsRefusedWithoutRepeatingThePath()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""{ "quiet\nDatabase error: reached": { "Password": "hunter2" } }""");

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(
            binding.Refusals,
            candidate => candidate.Contains("does not persist secret material", StringComparison.Ordinal));
        Assert.DoesNotContain("Database error", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', refusal);
        Assert.DoesNotContain("hunter2", refusal, StringComparison.Ordinal);
        Assert.Contains("a setting of the owner record", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A value that merely parses as a reference is material too: a scheme is minted for any name before the first
    /// colon, so what decides is whether this deployment serves the scheme rather than whether the syntax admits it.
    /// </summary>
    [Fact]
    public void Bind_PasswordUnderASchemeNothingServes_IsRefused()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaring(("work", "The work mailbox"), passwordReference: "Pa55:word"));

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(binding.Refusals, refusal => refusal.Contains("does not persist secret material", StringComparison.Ordinal));
    }

    /// <summary>A reference to a scheme the deployment resolves is what the record is meant to carry.</summary>
    [Fact]
    public void Bind_PasswordReference_IsBound()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaring(("work", "The work mailbox")));

        // Assert
        Assert.True(binding.IsBound);
        Assert.Equal(PasswordReference, binding.Owner!.MailAccounts[0].Secrets.Password!.SecretReference);
    }

    /// <summary>A row nothing could parse is refused as one, rather than read as an owner who declared nothing.</summary>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("[ 1, 2 ]")]
    public void Bind_DocumentThatIsNotAJsonObject_IsRefused(string document)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(document);

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(binding.Refusals, refusal => refusal.Contains("not a JSON object", StringComparison.Ordinal));
    }

    /// <summary>The block an owner states their own posture in binds as its own type, beside their mailboxes.</summary>
    [Fact]
    public void Bind_ADocumentStatingAClassificationPosture_BindsItAsTheOwnersOwn()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""
            {
              "MailAccounts": [],
              "SpamClassification": {
                "Enabled": true,
                "UseScanner": true,
                "ScannedFolders": [ "inbox" ],
                "ScannerThreshold": 6.5,
                "Actions": { "MoveToJunkFolder": true, "JunkFolder": "role:Junk", "Threshold": 8 }
              }
            }
            """);

        // Assert
        Assert.True(binding.IsBound);

        var classification = binding.Owner!.SpamClassification;

        Assert.True(classification.Enabled);
        Assert.True(classification.UseScanner);
        Assert.Equal(["inbox"], classification.ScannedFolders!);
        Assert.Equal(6.5, classification.ScannerThreshold);
        Assert.True(classification.Actions.MoveToJunkFolder);
        Assert.Equal(8, classification.Actions.Threshold);
    }

    /// <summary>What the engine costs is the deployment's, so a record reaching for one of its settings is refused.</summary>
    /// <remarks>
    /// The key is one the deployment's own section really binds, so this fails if the owner's type ever grows it — which
    /// is the shape the refusal exists against. An invented name would only prove what
    /// <see cref="Bind_PropertyNothingBinds_IsRefused" /> already proves about any unknown property.
    /// </remarks>
    [Fact]
    public void Bind_ADocumentStatingADeploymentOnlyClassificationSetting_IsRefused()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""
            { "MailAccounts": [], "SpamClassification": { "Enabled": true, "ClassificationWait": "00:30:00" } }
            """);

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(binding.Refusals, refusal => refusal.Contains("ClassificationWait", StringComparison.Ordinal));
    }

    /// <summary>An owner writing outside the range the deployment permits is refused at the write, naming the range.</summary>
    [Fact]
    public void Bind_ADocumentStatingAThresholdOutsideTheDeploymentsRange_IsRefusedNamingTheRange()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""
            { "MailAccounts": [], "SpamClassification": { "Enabled": true, "ScannerThreshold": 5000 } }
            """);

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(
            binding.Refusals,
            refusal => refusal.Contains(
                SpamClassificationOptions.LargestThreshold.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal));
    }

    /// <summary>The ceiling is applied before the parse, so a payload never costs the expansion it is refused for.</summary>
    [Fact]
    public void Bind_DocumentPastTheCeiling_IsRefusedNamingTheBound()
    {
        // Arrange
        var binder = CreateBinder();
        var oversized = $$"""{ "MailAccounts": [], "Padding": "{{new string('x', OwnerSettingsDocument.MaximumOctets)}}" }""";

        // Act
        var binding = binder.Bind(oversized);

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.Contains(OwnerSettingsDocument.MaximumOctets.ToString(CultureInfo.InvariantCulture), refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A record that fits as it was written and not as the database stores it is refused here rather than persisted
    /// and then refused on every read. PostgreSQL renders <c>jsonb</c> with a space after every colon and every comma,
    /// so a page of short pairs grows by two octets a pair — which is why the ceiling is measured over that rendering
    /// and this document, compact and under the bound, is over it once stored.
    /// </summary>
    [Fact]
    public void Bind_DocumentPastTheCeilingOnlyAsTheDatabaseStoresIt_IsRefused()
    {
        // Arrange
        var binder = CreateBinder();
        var manySmallPairs = DocumentOfShortPairsUnderTheCeilingAsWritten();

        // Act
        var binding = binder.Bind(manySmallPairs);

        // Assert
        Assert.True(Encoding.UTF8.GetByteCount(manySmallPairs) <= OwnerSettingsDocument.MaximumOctets);
        Assert.True(RootSettingsCommitRules.PersistedOctetsOf(manySmallPairs) > OwnerSettingsDocument.MaximumOctets);
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.Contains("as the database stores it", refusal, StringComparison.Ordinal);
        Assert.Contains(OwnerSettingsDocument.MaximumOctets.ToString(CultureInfo.InvariantCulture), refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A synchronization bound in the future excludes every email the mailbox holds, and the deployment's own section
    /// refuses one at startup. A record judged by every rule but that one would accept from a row what configuration
    /// refuses from a file.
    /// </summary>
    [Fact]
    public void Bind_EarliestReceivedDateAfterToday_IsRefused()
    {
        // Arrange
        var binder = CreateBinder();
        var tomorrow = DateOnly.FromDateTime(Today.UtcDateTime).AddDays(1);

        // Act
        var binding = binder.Bind(
            $$"""
              {
                "MailAccounts": [
                  {
                    "AccountId": "work",
                    "DisplayName": "The work mailbox",
                    "Host": "imap.example.test",
                    "UserName": "mailfathom@example.test",
                    "EarliestEmailReceivedDate": "{{tomorrow:yyyy-MM-dd}}",
                    "Secrets": { "Password": { "Name": "work-password", "SecretReference": "{{PasswordReference}}" } }
                  }
                ]
              }
              """);

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(
            binding.Refusals,
            refusal => refusal.Contains("would exclude every email in the mailbox", StringComparison.Ordinal));
    }

    /// <summary>An absent document is not an empty record: every row this reads carries at least the empty object.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Bind_NoDocumentAtAll_IsRejectedAsAnArgument(string document)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var rejected = Record.Exception(() => binder.Bind(document));

        // Assert
        Assert.IsType<ArgumentException>(rejected);
    }

    /// <summary>Composes a page of short pairs whose written form fits and whose stored rendering does not.</summary>
    /// <remarks>
    /// Each pair costs fourteen octets written and sixteen stored — a space after its colon and one after the comma
    /// before it — so a count near a fifteenth of the bound leaves the written form inside it while the rendering
    /// passes it. The test asserts both halves rather than trusting the arithmetic here.
    /// </remarks>
    private static string DocumentOfShortPairsUnderTheCeilingAsWritten()
    {
        var pairs = Enumerable
            .Range(0, OwnerSettingsDocument.MaximumOctets / 15)
            .Select(index => string.Create(CultureInfo.InvariantCulture, $"\"k{index:D6}\":\"v\""));

        return $$"""{"MailAccounts":[],{{string.Join(",", pairs)}}}""";
    }

    /// <summary>An owner switching on a scanner the deployment left off is the record this block exists for.</summary>
    [Fact]
    public void Bind_ARecordSwitchingOnAScannerTheDeploymentLeftOff_BindsWhatItAsksFor()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""{"SensitiveContent":{"Secrets":{"Enabled":true}}}""");

        // Assert
        Assert.True(binding.IsBound);
        Assert.True(binding.Owner!.SensitiveContent.Secrets.Enabled);
    }

    /// <summary>
    /// The write is where a loosening is stopped, so whoever wrote the record learns which deployment switch refused it
    /// rather than finding their mail scanned anyway and their own record describing something else.
    /// </summary>
    [Fact]
    public void Bind_ARecordSwitchingOffAScannerTheDeploymentRequires_IsRefusedNamingTheSetting()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        var binder = CreateBinder(deployment);

        // Act
        var binding = binder.Bind("""{"SensitiveContent":{"Secrets":{"Enabled":false}}}""");

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(
            binding.Refusals,
            refusal => refusal.Contains("SensitiveContent:Secrets:Enabled", StringComparison.Ordinal));
    }

    /// <summary>Asking for a scanner this deployment stood up no analyzer for is refused here rather than at the first message.</summary>
    [Fact]
    public void Bind_ARecordAskingForThePersonalDataScannerWithNoAnalyzer_IsRefusedNamingTheDeploymentSetting()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""{"SensitiveContent":{"Pii":{"Enabled":true}}}""");

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(
            binding.Refusals,
            refusal => refusal.Contains("PersonalDataAnalyzer:Endpoint", StringComparison.Ordinal));
    }

    /// <summary>Builds the binder, over a deployment that scans nothing unless a test says otherwise.</summary>
    /// <param name="deployment">The deployment's own scanning section, which a record's scanning block is judged against.</param>
    private static OwnerAccountDocumentBinder CreateBinder(SensitiveContentOptions? deployment = null) =>
        new(
            new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
            new FakeTimeProvider(Today),
            Options.Create(deployment ?? new SensitiveContentOptions()));

    private static string DocumentDeclaring(
        params (string AccountId, string DisplayName)[] accounts) =>
        DocumentDeclaring(accounts, PasswordReference);

    private static string DocumentDeclaring(
        (string AccountId, string DisplayName) account,
        string passwordReference) =>
        DocumentDeclaring([account], passwordReference);

    private static string DocumentDeclaring(
        IReadOnlyList<(string AccountId, string DisplayName)> accounts,
        string passwordReference)
    {
        var declarations = accounts.Select(account =>
            $$"""
              {
                "AccountId": "{{account.AccountId}}",
                "DisplayName": "{{account.DisplayName}}",
                "Host": "imap.example.test",
                "UserName": "mailfathom@example.test",
                "Secrets": { "Password": { "Name": "{{account.AccountId}}-password", "SecretReference": "{{passwordReference}}" } }
              }
              """);

        return $$"""{ "MailAccounts": [ {{string.Join(",", declarations)}} ] }""";
    }
}
