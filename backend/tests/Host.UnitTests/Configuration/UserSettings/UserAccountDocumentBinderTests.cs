// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Scheduling;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.Observability.ClientTelemetry;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Settings;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Asserts what a user's document has to be before it is a record this deployment acts on. The binder is the one
/// place both directions are meant to meet — a row read back and a candidate about to be written — so every rule
/// proved here is one rule rather than a pair that could drift. This release carries no path that drives it: the read
/// bounds the column and hands the text back unjudged, and a write is still refused under <c>12006</c>, so what these
/// tests hold is the rule each direction arrives by once <c>#1223</c> and <c>#1224</c> reach it.
/// </summary>
public sealed class UserAccountDocumentBinderTests
{
    private const string PasswordReference = "file:/run/secrets/work-password";

    /// <summary>The instant every binding here is judged against, so a date-bound rule is decided rather than drawn.</summary>
    private static readonly DateTimeOffset Today = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A user is provisioned before their first mailbox, so a record declaring nothing is an ordinary one.</summary>
    [Fact]
    public void Bind_ADocumentDeclaringNothing_IsAUserAssignedNoMailAccount()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("{}", UserRecordArrival.BeingWritten);

        // Assert
        Assert.True(binding.IsBound);
        Assert.Empty(binding.User!.MailAccounts);
    }

    /// <summary>An account states the language its mail is read in, and the record around it states none.</summary>
    [Fact]
    public void Bind_ADeclaredAccountNamingALanguage_BindsThatLanguageOntoTheAccount()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaringAccountReading(("work", "The work mailbox"), "Polish"), UserRecordArrival.BeingWritten);

        // Assert
        Assert.True(binding.IsBound);
        var account = Assert.Single(binding.User!.MailAccounts);
        Assert.Equal(MailAccountLanguage.Polish, account.ReadingLanguage);
    }

    /// <summary>
    /// The one property a declaration must state. What this deployment writes about a mailbox comes out in some
    /// language whether or not anybody chose it, so an unstated one is an account recorded before the property
    /// existed rather than a mailbox nobody asked anything of.
    /// </summary>
    [Fact]
    public void Bind_ADeclaredAccountNamingNoLanguage_IsRefusedNamingBothItTakes()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaringAccountReading(("work", "The work mailbox"), language: null), UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.Contains("Language is not stated", refusal, StringComparison.Ordinal);
        Assert.Contains("'English'", refusal, StringComparison.Ordinal);
        Assert.Contains("'Polish'", refusal, StringComparison.Ordinal);
    }

    /// <summary>A language this build does not write in is a value to correct rather than one to fall back from.</summary>
    [Theory]
    [InlineData("German")]
    [InlineData("pl")]
    [InlineData("0")]
    [InlineData("")]
    public void Bind_ADeclaredAccountNamingALanguageThisBuildDoesNotWriteIn_IsRefused(string language)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaringAccountReading(("work", "The work mailbox"), language), UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(
            binding.Refusals,
            refusal => refusal.Contains("'English'", StringComparison.Ordinal));
    }

    /// <summary>
    /// An account recorded before the property existed states no language and could not have stated one, so a start
    /// reads it rather than refusing it — the surface an administrator would add the line from is behind the gate
    /// that would be failing.
    /// </summary>
    [Fact]
    public void Bind_AHeldAccountNamingNoLanguage_BindsAndIsReadAsEnglish()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaringAccountReading(("work", "The work mailbox"), language: null), UserRecordArrival.AlreadyHeld);

        // Assert
        Assert.True(binding.IsBound);
        var account = Assert.Single(binding.User!.MailAccounts);
        Assert.Null(account.ReadingLanguage);
    }

    /// <summary>
    /// The value no release ever accepted is refused whichever direction it arrived from, so the leniency above covers
    /// the absence alone rather than the property.
    /// </summary>
    [Fact]
    public void Bind_AHeldAccountNamingALanguageThisBuildDoesNotWriteIn_IsRefused()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaringAccountReading(("work", "The work mailbox"), "German"), UserRecordArrival.AlreadyHeld);

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(
            binding.Refusals,
            refusal => refusal.Contains("not a language MailFathom writes in", StringComparison.Ordinal));
    }

    /// <summary>The name is written by hand in a declaration, so it is read the way it was typed.</summary>
    [Theory]
    [InlineData("polish", MailAccountLanguage.Polish)]
    [InlineData("ENGLISH", MailAccountLanguage.English)]
    public void Bind_ALanguageNamedInAnotherCase_BindsToTheSameLanguage(string written, MailAccountLanguage expected)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaringAccountReading(("work", "The work mailbox"), written), UserRecordArrival.BeingWritten);

        // Assert
        Assert.True(binding.IsBound);
        var account = Assert.Single(binding.User!.MailAccounts);
        Assert.Equal(expected, account.ReadingLanguage);
    }

    /// <summary>The record states the language this deployment writes for the person in, which is a value of its own beside the one each of their mailboxes states.</summary>
    [Fact]
    public void Bind_ARecordNamingALanguage_BindsThatLanguageOntoTheRecord()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""{"Language": "Polish"}""", UserRecordArrival.BeingWritten);

        // Assert
        Assert.True(binding.IsBound);
        Assert.Equal(UserLanguage.Polish, binding.User!.ReadingLanguage);
    }

    /// <summary>
    /// A record committed before the property existed states none and could not have stated one, so every reading
    /// binds it and reads it as English. What refuses an absence is the write that rewrites the record, where the
    /// document is in front of whoever is saving it.
    /// </summary>
    [Fact]
    public void Bind_ARecordBeingWrittenNamingNoLanguage_BindsAndIsReadAsEnglish() =>
        AssertBindsWithNoLanguage(UserRecordArrival.BeingWritten);

    /// <summary>A record already held states none whenever it was committed before the property existed, which every start reads.</summary>
    [Fact]
    public void Bind_AHeldRecordNamingNoLanguage_BindsAndIsReadAsEnglish() =>
        AssertBindsWithNoLanguage(UserRecordArrival.AlreadyHeld);

    /// <summary>A language this build does not write in was never committed through this binder, so it is refused whichever direction the record arrived from.</summary>
    [Fact]
    public void Bind_ARecordBeingWrittenNamingALanguageThisBuildDoesNotWriteIn_IsRefused() =>
        AssertRefusesTheUnwritableLanguage(UserRecordArrival.BeingWritten);

    /// <summary>No release ever accepted the value, so a stored record carrying one was never committed through this binder either.</summary>
    [Fact]
    public void Bind_AHeldRecordNamingALanguageThisBuildDoesNotWriteIn_IsRefused() =>
        AssertRefusesTheUnwritableLanguage(UserRecordArrival.AlreadyHeld);

    /// <summary>The record states the zone that person's own days are read in, which is what a relative period is resolved against.</summary>
    [Fact]
    public void Bind_ARecordNamingATimeZone_BindsThatZoneOntoTheRecord()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(
            """{"Language": "English", "TimeZone": "Europe/Warsaw"}""",
            UserRecordArrival.BeingWritten);

        // Assert
        Assert.True(binding.IsBound);
        Assert.Equal("Europe/Warsaw", binding.User!.ReadingTimeZone!.Id);
    }

    /// <summary>
    /// A record stating no zone has asked for nothing, which is an ordinary state rather than an unfinished one, and it
    /// binds as nothing rather than as the coordinated zone: a reader falls back for itself, and the one reader that
    /// acts on the difference offers this person the zone their own machine reports.
    /// </summary>
    [Fact]
    public void Bind_ARecordBeingWrittenNamingNoTimeZone_BindsStatingNoZone() =>
        AssertStatesNoZone(UserRecordArrival.BeingWritten);

    /// <summary>A record committed before the field existed states no zone either, and is read exactly as a new one is.</summary>
    [Fact]
    public void Bind_AHeldRecordNamingNoTimeZone_BindsStatingNoZone() =>
        AssertStatesNoZone(UserRecordArrival.AlreadyHeld);

    /// <summary>A zone this deployment cannot resolve would be read in UTC without anybody being told, so it is refused instead.</summary>
    [Theory]
    [InlineData("Europe/Warszawa")]
    [InlineData("+02:00")]
    public void Bind_ARecordNamingATimeZoneThisDeploymentDoesNotKnow_IsRefusedNamingTheFormItTakes(string zoneId)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(
            $$"""{"Language": "English", "TimeZone": "{{zoneId}}"}""",
            UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.Contains("is not a time zone this deployment knows", refusal, StringComparison.Ordinal);
        Assert.Contains("Europe/Warsaw", refusal, StringComparison.Ordinal);
    }

    /// <summary>The record states the level this person's own client is asked to record at, read however it was capitalized.</summary>
    [Theory]
    [InlineData("Debug")]
    [InlineData("debug")]
    [InlineData("DEBUG")]
    public void Bind_ARecordNamingAClientTelemetryLevel_BindsThatLevelOntoTheRecord(string written)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(
            $$"""{"Language": "English", "ClientTelemetryLevel": "{{written}}"}""",
            UserRecordArrival.BeingWritten);

        // Assert
        Assert.True(binding.IsBound);
        Assert.Equal(ClientTelemetryLevel.Debug, binding.User!.ReadingClientTelemetryLevel);
    }

    /// <summary>A record stating no level asked for nothing, which is an ordinary state rather than an unfinished one.</summary>
    [Fact]
    public void Bind_ARecordBeingWrittenNamingNoClientTelemetryLevel_BindsStatingNoLevel() =>
        AssertStatesNoClientTelemetryLevel(UserRecordArrival.BeingWritten);

    /// <summary>Every record committed before the key existed states no level, and is read exactly as a new one is.</summary>
    [Fact]
    public void Bind_AHeldRecordNamingNoClientTelemetryLevel_BindsStatingNoLevel() =>
        AssertStatesNoClientTelemetryLevel(UserRecordArrival.AlreadyHeld);

    /// <summary>
    /// A level nothing publishes would otherwise leave this person served the deployment's level with nobody told the
    /// raise never landed, which is precisely the case an operator is waiting on records for.
    /// </summary>
    [Theory]
    [InlineData("verbose")]
    [InlineData("2")]
    [InlineData("off")]
    public void Bind_ARecordNamingAClientTelemetryLevelNoClientCanRecordAt_IsRefusedNamingTheValuesItTakes(string written)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(
            $$"""{"Language": "English", "ClientTelemetryLevel": "{{written}}"}""",
            UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.Contains("ClientTelemetryLevel", refusal, StringComparison.Ordinal);
        Assert.Contains("is not a level a client can be asked to record at", refusal, StringComparison.Ordinal);
        Assert.Contains("'Debug'", refusal, StringComparison.Ordinal);
    }

    /// <summary>The bound is the stored zone identifier's own, so a value past it is refused rather than truncated.</summary>
    [Fact]
    public void Bind_ARecordNamingATimeZoneLongerThanOneMayBe_IsRefused()
    {
        // Arrange
        var binder = CreateBinder();
        var overLong = new string('a', ZonedInstant.MaximumZoneIdLength + 1);

        // Act
        var binding = binder.Bind(
            $$"""{"Language": "English", "TimeZone": "{{overLong}}"}""",
            UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(
            "is not a time zone this deployment knows",
            Assert.Single(binding.Refusals),
            StringComparison.Ordinal);
    }

    /// <summary>The name is written by hand in a record too, so it is read the way it was typed.</summary>
    [Theory]
    [InlineData("polish", UserLanguage.Polish)]
    [InlineData("ENGLISH", UserLanguage.English)]
    public void Bind_ARecordLanguageNamedInAnotherCase_BindsToTheSameLanguage(string written, UserLanguage expected)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind($$"""{"Language": "{{written}}"}""", UserRecordArrival.BeingWritten);

        // Assert
        Assert.True(binding.IsBound);
        Assert.Equal(expected, binding.User!.ReadingLanguage);
    }

    private static void AssertBindsWithNoLanguage(UserRecordArrival arrival)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("{}", arrival);

        // Assert
        Assert.True(binding.IsBound);
        Assert.Null(binding.User!.ReadingLanguage);
    }

    private static void AssertStatesNoZone(UserRecordArrival arrival)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""{"Language": "English"}""", arrival);

        // Assert
        Assert.True(binding.IsBound);
        Assert.Null(binding.User!.ReadingTimeZone);
    }

    private static void AssertStatesNoClientTelemetryLevel(UserRecordArrival arrival)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""{"Language": "English"}""", arrival);

        // Assert
        Assert.True(binding.IsBound);
        Assert.Null(binding.User!.ReadingClientTelemetryLevel);
    }

    private static void AssertRefusesTheUnwritableLanguage(UserRecordArrival arrival)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""{"Language": "German"}""", arrival);

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.Contains("not a language MailFathom writes in", refusal, StringComparison.Ordinal);
        Assert.Contains("'English'", refusal, StringComparison.Ordinal);
        Assert.Contains("'Polish'", refusal, StringComparison.Ordinal);
    }

    /// <summary>A declaration in the record is the same declaration a file carried, bound by the same type.</summary>
    [Fact]
    public void Bind_DeclaredMailAccount_BindsItAsTheUsersOwn()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaring(("work", "The work mailbox")), UserRecordArrival.BeingWritten);

        // Assert
        Assert.True(binding.IsBound);
        var account = Assert.Single(binding.User!.MailAccounts);
        Assert.Equal("work", account.AccountId);
        Assert.Equal("The work mailbox", account.DisplayName);
        Assert.Equal("imap.example.test", account.Host);
    }

    /// <summary>Within one user the same identifier twice is a name that could select either mailbox.</summary>
    /// <remarks>
    /// Within one user, and nowhere wider: the collision is read from the declarations of the document in front of
    /// the binder, which takes no user and keeps nothing between calls, so two users each declaring <c>work</c> is a
    /// pair of ordinary records. That is a property of the subject rather than a claim a test could break, which is
    /// why it is stated here instead of asserted by binding one document twice.
    /// </remarks>
    [Fact]
    public void Bind_OneUserDeclaringAnIdentifierTwice_IsRefused()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(DocumentDeclaring(("work", "The work mailbox"), ("work", "The other mailbox")), UserRecordArrival.BeingWritten);

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
        var binding = binder.Bind(DocumentDeclaring(("work", "personal"), ("personal", "The personal mailbox")), UserRecordArrival.BeingWritten);

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
            """,
            UserRecordArrival.BeingWritten);

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
        var binding = binder.Bind("""{ "MailAccounts": [], "TrustedSenders": [] }""", UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.Contains("TrustedSenders", refusal, StringComparison.Ordinal);

        // What the framework says here names MailFathom's own type and the binder option that was set, neither of
        // which is a thing whoever wrote the record can act on.
        Assert.DoesNotContain(nameof(UserAccountOptions), refusal, StringComparison.Ordinal);
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
        var binding = binder.Bind("""{ "MailAccounts": [], "quiet\nDatabase error: reached": 1 }""", UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.DoesNotContain("Database error", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', refusal);
        Assert.Contains("does not bind to a user's settings", refusal, StringComparison.Ordinal);
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
        var binding = binder.Bind(json, UserRecordArrival.BeingWritten);

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
        var binding = binder.Bind("""{ "MailAccounts": [ { "AccountId": "work", "Port": "hunter2" } ] }""", UserRecordArrival.BeingWritten);

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
        var binding = binder.Bind(DocumentDeclaring(("work", "The work mailbox"), passwordReference: "hunter2"), UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.Contains("MailAccounts:0:Secrets:Password:SecretReference", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal names the setting only when the whole path is one, because the rule that finds material reads the
    /// last segment and an earlier one is a user's own text — a place to forge a sentence an administrator would
    /// read as MailFathom's.
    /// </summary>
    [Fact]
    public void Bind_MaterialUnderAPathCarryingAControlCharacter_IsRefusedWithoutRepeatingThePath()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind("""{ "quiet\nDatabase error: reached": { "Password": "hunter2" } }""", UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(
            binding.Refusals,
            candidate => candidate.Contains("does not persist secret material", StringComparison.Ordinal));
        Assert.DoesNotContain("Database error", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', refusal);
        Assert.DoesNotContain("hunter2", refusal, StringComparison.Ordinal);
        Assert.Contains("a setting of the user record", refusal, StringComparison.Ordinal);
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
        var binding = binder.Bind(DocumentDeclaring(("work", "The work mailbox"), passwordReference: "Pa55:word"), UserRecordArrival.BeingWritten);

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
        var binding = binder.Bind(DocumentDeclaring(("work", "The work mailbox")), UserRecordArrival.BeingWritten);

        // Assert
        Assert.True(binding.IsBound);
        Assert.Equal(PasswordReference, binding.User!.MailAccounts[0].Secrets.Password!.SecretReference);
    }

    /// <summary>A row nothing could parse is refused as one, rather than read as a user who declared nothing.</summary>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("[ 1, 2 ]")]
    public void Bind_DocumentThatIsNotAJsonObject_IsRefused(string document)
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(document, UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(binding.Refusals, refusal => refusal.Contains("not a JSON object", StringComparison.Ordinal));
    }

    /// <summary>The block a mailbox states its own posture in binds as its own type, on the account that carries it.</summary>
    [Fact]
    public void Bind_ADocumentStatingAClassificationPosture_BindsItAsTheAccountsOwn()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(
            DocumentDeclaringAccountCarrying("""
                "Folders": [
                  { "Alias": "inbox", "RemotePath": "INBOX" },
                  { "Alias": "quarantine", "RemotePath": "Quarantine" }
                ],
                "SpamClassification": {
                  "Enabled": true,
                  "UseScanner": true,
                  "ScannedFolders": [ "inbox" ],
                  "ScannerThreshold": 6.5,
                  "Actions": { "MoveToJunkFolder": true, "JunkFolder": "quarantine", "Threshold": 8 }
                }
                """),
            UserRecordArrival.BeingWritten);

        // Assert
        Assert.True(binding.IsBound);

        var classification = binding.User!.MailAccounts.Single().SpamClassification;

        Assert.True(classification.Enabled);
        Assert.True(classification.UseScanner);
        Assert.Equal(["inbox"], classification.ScannedFolders!);
        Assert.Equal(6.5, classification.ScannerThreshold);
        Assert.True(classification.Actions.MoveToJunkFolder);
        Assert.Equal(8, classification.Actions.Threshold);
    }

    /// <summary>What the engine costs is the deployment's, so a record reaching for one of its settings is refused.</summary>
    /// <remarks>
    /// The key is one the deployment's own section really binds, so this fails if the account's type ever grows it —
    /// which is the shape the refusal exists against. An invented name would only prove what
    /// <see cref="Bind_PropertyNothingBinds_IsRefused" /> already proves about any unknown property.
    /// </remarks>
    [Fact]
    public void Bind_ADocumentStatingADeploymentOnlyClassificationSetting_IsRefused()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(
            DocumentDeclaringAccountCarrying("""
                "SpamClassification": { "Enabled": true, "ClassificationWait": "00:30:00" }
                """),
            UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(binding.Refusals, refusal => refusal.Contains("ClassificationWait", StringComparison.Ordinal));
    }

    /// <summary>An account writing outside the range the deployment permits is refused at the write, naming the range.</summary>
    [Fact]
    public void Bind_ADocumentStatingAThresholdOutsideTheDeploymentsRange_IsRefusedNamingTheRange()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(
            DocumentDeclaringAccountCarrying("""
                "SpamClassification": { "Enabled": true, "ScannerThreshold": 5000 }
                """),
            UserRecordArrival.BeingWritten);

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
        var oversized = $$"""{ "MailAccounts": [], "Padding": "{{new string('x', UserSettingsDocument.MaximumOctets)}}" }""";

        // Act
        var binding = binder.Bind(oversized, UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.Contains(UserSettingsDocument.MaximumOctets.ToString(CultureInfo.InvariantCulture), refusal, StringComparison.Ordinal);
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
        var binding = binder.Bind(manySmallPairs, UserRecordArrival.BeingWritten);

        // Assert
        Assert.True(Encoding.UTF8.GetByteCount(manySmallPairs) <= UserSettingsDocument.MaximumOctets);
        Assert.True(RootSettingsCommitRules.PersistedOctetsOf(manySmallPairs) > UserSettingsDocument.MaximumOctets);
        Assert.False(binding.IsBound);
        var refusal = Assert.Single(binding.Refusals);
        Assert.Contains("as the database stores it", refusal, StringComparison.Ordinal);
        Assert.Contains(UserSettingsDocument.MaximumOctets.ToString(CultureInfo.InvariantCulture), refusal, StringComparison.Ordinal);
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
              """,
            UserRecordArrival.BeingWritten);

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
        var rejected = Record.Exception(() => binder.Bind(document, UserRecordArrival.BeingWritten));

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
            .Range(0, UserSettingsDocument.MaximumOctets / 15)
            .Select(index => string.Create(CultureInfo.InvariantCulture, $"\"k{index:D6}\":\"v\""));

        return $$"""{"MailAccounts":[],{{string.Join(",", pairs)}}}""";
    }

    /// <summary>An account switching on a scanner the deployment left off is the record this block exists for.</summary>
    [Fact]
    public void Bind_ARecordSwitchingOnAScannerTheDeploymentLeftOff_BindsWhatItAsksFor()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(
            DocumentDeclaringAccountCarrying("""
                "SensitiveContent": { "Secrets": { "Enabled": true } }
                """),
            UserRecordArrival.BeingWritten);

        // Assert
        Assert.True(binding.IsBound);
        Assert.True(binding.User!.MailAccounts.Single().SensitiveContent.Secrets.Enabled);
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
        var binding = binder.Bind(
            DocumentDeclaringAccountCarrying("""
                "SensitiveContent": { "Secrets": { "Enabled": false } }
                """),
            UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(
            binding.Refusals,
            refusal => refusal.Contains("MailAccounts:0:SensitiveContent:Secrets:Enabled", StringComparison.Ordinal));
    }

    /// <summary>Asking for a scanner this deployment stood up no analyzer for is refused here rather than at the first message.</summary>
    [Fact]
    public void Bind_ARecordAskingForThePersonalDataScannerWithNoAnalyzer_IsRefusedNamingTheDeploymentSetting()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(
            DocumentDeclaringAccountCarrying("""
                "SensitiveContent": { "Pii": { "Enabled": true } }
                """),
            UserRecordArrival.BeingWritten);

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(
            binding.Refusals,
            refusal => refusal.Contains("PersonalDataAnalyzer:Endpoint", StringComparison.Ordinal));
    }

    /// <summary>
    /// A record accepted while the deployment screened less is read back after the deployment tightened, which is the
    /// case an operator creates by doing the thing this feature exists to make safe. Refusing it would refuse the start
    /// for every user, over a record whose author cannot reach the surface that would rewrite it.
    /// </summary>
    [Fact]
    public void Bind_AHeldRecordTheDeploymentHasSinceTightenedPast_IsStillBound()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        deployment.ScreenOutgoingMailFor = ["Secrets", "Pii"];
        var binder = CreateBinder(deployment);
        var held = DocumentDeclaringAccountCarrying("""
            "SensitiveContent": { "Secrets": { "Enabled": false }, "ScreenOutgoingMailFor": [ "Secrets" ] }
            """);

        // Act
        var written = binder.Bind(held, UserRecordArrival.BeingWritten);
        var alreadyHeld = binder.Bind(held, UserRecordArrival.AlreadyHeld);

        // Assert
        Assert.False(written.IsBound);
        Assert.True(alreadyHeld.IsBound);
        Assert.Equal(["Secrets"], alreadyHeld.User!.MailAccounts.Single().SensitiveContent.ScreenOutgoingMailFor!);
    }

    /// <summary>Everything a record is judged by other than the deployment's own posture holds in both directions.</summary>
    [Fact]
    public void Bind_AHeldRecordNamingASettingNothingBinds_IsRefusedAsOneBeingWrittenIs()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var binding = binder.Bind(
            """{"MailAccounts":[],"Nonsense":1}""",
            UserRecordArrival.AlreadyHeld);

        // Assert
        Assert.False(binding.IsBound);
    }

    /// <summary>
    /// Both blocks are the mail account's now, so a record naming either one beside the user's own settings names a
    /// property nothing binds and is refused by the name it wrote. Held records are judged the same way as one being
    /// written, because a record written before the move carries the block where it no longer reads: dropping it
    /// quietly would leave every one of that user's mailboxes scanned under the deployment's posture while the record
    /// on file still says what it asked for.
    /// </summary>
    [Theory]
    [InlineData(false, "SensitiveContent", """{"Secrets":{"Enabled":true}}""")]
    [InlineData(false, "SpamClassification", """{"IsEnabled":true}""")]
    [InlineData(true, "SensitiveContent", """{"Secrets":{"Enabled":true}}""")]
    [InlineData(true, "SpamClassification", """{"IsEnabled":true}""")]
    public void Bind_ARecordNamingABlockThatMovedOntoTheMailAccount_IsRefusedByThePropertyItWrote(
        bool alreadyHeld,
        string property,
        string block)
    {
        // Arrange
        var binder = CreateBinder();
        var arrival = alreadyHeld ? UserRecordArrival.AlreadyHeld : UserRecordArrival.BeingWritten;

        // Act
        var binding = binder.Bind(
            $$"""{"MailAccounts":[],"{{property}}":{{block}}}""",
            arrival);

        // Assert
        Assert.False(binding.IsBound);
        Assert.Contains(binding.Refusals, refusal => refusal.Contains(property, StringComparison.Ordinal));
    }

    /// <summary>Builds the binder, over a deployment that scans nothing unless a test says otherwise.</summary>
    /// <param name="deployment">The deployment's own scanning section, which a record's scanning block is judged against.</param>
    private static UserAccountDocumentBinder CreateBinder(SensitiveContentOptions? deployment = null) =>
        new(
            new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
            new FakeTimeProvider(Today),
            Options.Create(deployment ?? new SensitiveContentOptions()));

    /// <summary>Composes a record whose one mailbox carries the stated block beside everything an account must declare.</summary>
    /// <param name="block">The settings block, written as the JSON properties it occupies on the account.</param>
    /// <returns>A document one account long, carrying that block.</returns>
    private static string DocumentDeclaringAccountCarrying(string block) =>
        $$"""
          {
            "MailAccounts": [
              {
                "AccountId": "work",
                "DisplayName": "The work mailbox",
                "Language": "English",
                "Host": "imap.example.test",
                "UserName": "mailfathom@example.test",
                "Secrets": { "Password": { "Name": "work-password", "SecretReference": "{{PasswordReference}}" } },
                {{block}}
              }
            ]
          }
          """;

    private static string DocumentDeclaring(
        params (string AccountId, string DisplayName)[] accounts) =>
        DocumentDeclaring(accounts, PasswordReference, "English");

    private static string DocumentDeclaring(
        (string AccountId, string DisplayName) account,
        string passwordReference) =>
        DocumentDeclaring([account], passwordReference, "English");

    private static string DocumentDeclaringAccountReading(
        (string AccountId, string DisplayName) account,
        string? language) =>
        DocumentDeclaring([account], PasswordReference, language);

    private static string DocumentDeclaring(
        IReadOnlyList<(string AccountId, string DisplayName)> accounts,
        string passwordReference,
        string? language)
    {
        var declared = language is null
            ? string.Empty
            : $$"""
                "Language": "{{language}}",
                """;

        var declarations = accounts.Select(account =>
            $$"""
              {
                "AccountId": "{{account.AccountId}}",
                "DisplayName": "{{account.DisplayName}}",
                {{declared}}
                "Host": "imap.example.test",
                "UserName": "mailfathom@example.test",
                "Secrets": { "Password": { "Name": "{{account.AccountId}}-password", "SecretReference": "{{passwordReference}}" } }
              }
              """);

        return $$"""{ "MailAccounts": [ {{string.Join(",", declarations)}} ] }""";
    }
}
