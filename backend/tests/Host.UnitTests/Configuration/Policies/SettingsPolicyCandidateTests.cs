// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.Json.Nodes;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Infrastructure.Persistence.Policies;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Policies;

public sealed class SettingsPolicyCandidateTests
{
    [Fact]
    public void Judge_APolicyStatingNothing_IsAcceptedAsItWasSaved()
    {
        // Arrange
        const string saved = "{}";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Empty(candidate.Refusals);
        Assert.Equal("{}", candidate.Json);
    }

    /// <summary>One policy using every statement both sections take, so the whole vocabulary is accepted together.</summary>
    [Fact]
    public void Judge_APolicyUsingEveryStatementOfBothSections_IsAccepted()
    {
        // Arrange
        const string saved =
            """
            {
              "Users": {
                "Defaults": { "Language": "Polish", "TimeZone": "Europe/Warsaw", "EndpointAccess": { "McpEndpoint": false } },
                "Forced": { "ClientTelemetryLevel": "Warn", "ClientPreferences": { "TelemetryEnabled": false } },
                "Editing": { "Mode": "NoneExcept", "Properties": ["TimeZone", "DisplayName", "Portrait"] }
              },
              "MailAccounts": {
                "Defaults": { "Language": "Polish", "Port": 993, "Folders": [{ "Alias": "inbox", "RemotePath": "INBOX" }] },
                "Forced": {
                  "Mode": "Polling",
                  "TransportSecurity": { "ConnectionSecurity": "TlsOnConnect" },
                  "AuditTrail": { "Retention": "30.00:00:00" }
                },
                "Editing": { "Mode": "AllExcept", "Properties": ["Secrets:Password", "Delivery", "EmailAddress", "Folders"] }
              }
            }
            """;

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Empty(candidate.Refusals);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(saved), JsonNode.Parse(candidate.Json!)));
    }

    /// <summary>
    /// A statement is a sparse record rather than a record, so forcing one property of a block says nothing about its
    /// siblings: delivery forced on with no host stated is a rule about each account, not about the policy.
    /// </summary>
    [Fact]
    public void Judge_AForcedPropertyWhoseSiblingsEachRecordStates_IsNotHeldToARuleAboutAWholeRecord()
    {
        // Arrange
        const string saved = """{"MailAccounts":{"Forced":{"Delivery":{"Enabled":true}}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Empty(candidate.Refusals);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"Users\"")]
    [InlineData("""{"Users":{},"Users":{}}""")]
    public void Judge_ADocumentThatIsNotOneJsonObject_IsRefusedAsNoPolicy(string saved)
    {
        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains("not a JSON object", Assert.Single(candidate.Refusals), StringComparison.Ordinal);
    }

    [Fact]
    public void Judge_ADocumentPastTheCeiling_IsRefused()
    {
        // Arrange
        var saved = $$$$"""{"Users":{"Defaults":{"Language":"{{{{new string('a', SettingsPolicyDocument.MaximumOctets)}}}}"}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains(
            $"past the {SettingsPolicyDocument.MaximumOctets} octets",
            Assert.Single(candidate.Refusals),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The ceiling is held against what is committed rather than against what was typed. The committed rendering
    /// escapes a letter the typed form carries in two octets, so a policy well under the ceiling as typed can be past
    /// it as stored — and the store would refuse that one as a fault of the caller rather than of the policy.
    /// </summary>
    [Fact]
    public void Judge_ADocumentUnderTheCeilingAsTypedAndPastItAsCommitted_IsRefused()
    {
        // Arrange
        var saved = $$$$"""{"Users":{"Defaults":{"Language":"{{{{new string('ą', SettingsPolicyDocument.MaximumOctets / 4)}}}}"}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.True(Encoding.UTF8.GetByteCount(saved) < SettingsPolicyDocument.MaximumOctets);
        Assert.Null(candidate.Json);
        Assert.Contains(
            $"past the {SettingsPolicyDocument.MaximumOctets} octets",
            Assert.Single(candidate.Refusals),
            StringComparison.Ordinal);
    }

    /// <summary>The refusals that say a name is not part of a policy at all, each naming what belongs there instead.</summary>
    [Theory]
    [InlineData("""{"Groups":{}}""", "The policy names 'Groups', which is not a section of one.")]
    [InlineData("""{"Users":[]}""", "Users is a section of the policy")]
    [InlineData("""{"Users":{"Locked":{}}}""", "Users names 'Locked', which is not something a policy says about a user's record.")]
    [InlineData("""{"Users":{"Defaults":[]}}""", "Users:Defaults is an object in the shape of a user's record")]
    [InlineData("""{"Users":{"Editing":[]}}""", "Users:Editing is an object holding Mode and Properties.")]
    [InlineData("""{"Users":{"Editing":{"Lock":true}}}""", "Users:Editing names 'Lock', which is not part of an editing restriction.")]
    [InlineData("""{"users":{},"Users":{}}""", "The policy states 'users' more than once, in different casing.")]
    [InlineData("""{"Users":{"Defaults":{"Language":"Polish","language":"English"}}}""", "Users:Defaults states 'Language' more than once")]
    public void Judge_ANameThatIsNoPartOfAPolicy_IsRefusedNamingWhatBelongsThere(string saved, string expected)
    {
        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains(candidate.Refusals, refusal => refusal.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>A mistyped path is never a rule that silently governs nothing.</summary>
    [Theory]
    [InlineData("""{"Users":{"Defaults":{"Dialect":"Polish"}}}""", "Users:Defaults names Dialect, which is not a property of a user's record.")]
    [InlineData("""{"Users":{"Forced":{"EndpointAccess":{"Imap":true}}}}""", "Users:Forced names EndpointAccess:Imap, which is not a property of a user's record.")]
    [InlineData("""{"MailAccounts":{"Forced":{"Delivery":{"Hots":"smtp.example.test"}}}}""", "MailAccounts:Forced names Delivery:Hots, which is not a property of a mail account.")]
    [InlineData("""{"MailAccounts":{"Defaults":{"EffectiveFolders":[]}}}""", "MailAccounts:Defaults names EffectiveFolders, which is not a property of a mail account.")]
    [InlineData("""{"Users":{"Defaults":{"Host":"imap.example.test"}}}""", "Users:Defaults names Host, which is not a property of a user's record.")]
    [InlineData("""{"Users":{"Defaults":{"a b":1}}}""", "Users:Defaults names a name that is not a setting's, which names nothing a policy governs.")]
    public void Judge_APathThatNamesNothing_IsRefusedNamingThePath(string saved, string expected)
    {
        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains(candidate.Refusals, refusal => refusal.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>
    /// A key joined by colons binds exactly as the nested one does, so accepting it would store one property under two
    /// spellings. Inside a statement a property is named by nesting, and by a joined path only in an editing list.
    /// </summary>
    [Theory]
    [InlineData("""{"Users":{"Forced":{"EndpointAccess:McpEndpoint":false}}}""", "Users:Forced names 'EndpointAccess:McpEndpoint' as one key")]
    [InlineData("""{"MailAccounts":{"Defaults":{"Delivery":{"Secrets:Password":{}}}}}""", "MailAccounts:Defaults names 'Secrets:Password' as one key")]
    public void Judge_APropertyNamedByAJoinedKeyInsideAStatement_IsRefusedAskingForItNested(string saved, string expected)
    {
        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains(expected, Assert.Single(candidate.Refusals), StringComparison.Ordinal);
    }

    /// <summary>A policy governs the properties of a record and never which records exist or how one is identified.</summary>
    [Theory]
    [InlineData("""{"Users":{"Defaults":{"MailAccounts":[]}}}""", "Users:Defaults names MailAccounts, which is the list of a user's mail accounts")]
    [InlineData("""{"Users":{"Editing":{"Mode":"AllExcept","Properties":["MailAccounts"]}}}""", "Users:Editing:Properties lists MailAccounts, which is the list of a user's mail accounts")]
    [InlineData("""{"MailAccounts":{"Forced":{"AccountId":"x"}}}""", "MailAccounts:Forced names AccountId, which is the identifier this deployment generates")]
    public void Judge_APropertyNoPolicyGoverns_IsRefusedSayingWhy(string saved, string expected)
    {
        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains(candidate.Refusals, refusal => refusal.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>An identity property says who or which, so one statement for many records could only be wrong.</summary>
    [Theory]
    [InlineData("Users", "Defaults", """{"Portrait":"0197a3c0-0000-7000-8000-000000000001"}""", "Portrait")]
    [InlineData("Users", "Forced", """{"DisplayName":"Somebody"}""", "DisplayName")]
    [InlineData("MailAccounts", "Defaults", """{"UserName":"somebody"}""", "UserName")]
    [InlineData("MailAccounts", "Forced", """{"displayname":"Shared"}""", "DisplayName")]
    [InlineData("MailAccounts", "Forced", """{"EmailAddress":"somebody@example.test"}""", "EmailAddress")]
    [InlineData("MailAccounts", "Defaults", """{"Delivery":{"FromAddress":"somebody@example.test"}}""", "Delivery:FromAddress")]
    [InlineData("MailAccounts", "Forced", """{"Delivery":{"FromDisplayName":"Somebody","UserName":"somebody"}}""", "Delivery:UserName")]
    public void Judge_ADefaultOrAForcedValueForAnIdentityProperty_IsRefusedByName(
        string section,
        string statement,
        string stated,
        string property)
    {
        // Arrange
        var saved = $$$"""{"{{{section}}}":{"{{{statement}}}":{{{stated}}}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains(
            candidate.Refusals,
            refusal => refusal.Contains(
                $"{section}:{statement} states {property}, which says who or which rather than how",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Defaults", """{"Secrets":{"Password":{"Name":"imap","SecretReference":"env:IMAP"}}}""", "Secrets:Password")]
    [InlineData("Forced", """{"OAuth":{"ClientSecret":{"SecretReference":"env:CLIENT"}}}""", "OAuth:ClientSecret")]
    [InlineData("Forced", """{"OAuth":{"RefreshToken":"env:REFRESH"}}""", "OAuth:RefreshToken")]
    [InlineData("Defaults", """{"TransportSecurity":{"TrustedCertificateAuthority":{"SecretReference":"file:/ca.pem"}}}""", "TransportSecurity:TrustedCertificateAuthority")]
    [InlineData("Forced", """{"Delivery":{"Secrets":{"Password":{"SecretReference":"env:SMTP"}}}}""", "Delivery:Secrets:Password")]
    public void Judge_ADefaultOrAForcedValueForASecretBlock_IsRefusedByName(string statement, string stated, string property)
    {
        // Arrange
        var saved = $$$"""{"MailAccounts":{"{{{statement}}}":{{{stated}}}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains(
            candidate.Refusals,
            refusal => refusal.Contains(
                $"MailAccounts:{statement} states {property}, and a policy states nothing about the value of a secret block",
                StringComparison.Ordinal));
    }

    /// <summary>The non-secret half of a block holding a secret is ordinary, which is most of what stating a server once asks for.</summary>
    [Fact]
    public void Judge_TheNonSecretSettingsOfABlockHoldingASecret_AreAccepted()
    {
        // Arrange
        const string saved =
            """{"MailAccounts":{"Forced":{"Host":"imap.example.test","OAuth":{"TokenEndpoint":"https://login.example.test/token","ClientId":"mailfathom"}}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Empty(candidate.Refusals);
    }

    /// <summary>A value binds as the property it is stated for does, so a wrong type is the refusal a record would get.</summary>
    [Theory]
    [InlineData("""{"MailAccounts":{"Defaults":{"Port":"many"}}}""", "The value MailAccounts:Defaults gives Port is not of the type that setting takes.")]
    [InlineData("""{"Users":{"Forced":{"EndpointAccess":{"McpEndpoint":"sometimes"}}}}""", "The value Users:Forced gives EndpointAccess:McpEndpoint is not of the type that setting takes.")]
    [InlineData("""{"MailAccounts":{"Forced":{"Mode":"Pushing"}}}""", "The value MailAccounts:Forced gives Mode is not of the type that setting takes.")]
    [InlineData("""{"MailAccounts":{"Forced":{"Folders":[{"Alias":"inbox","Synchronize":"sometimes"}]}}}""", "The value MailAccounts:Forced gives Folders:0:Synchronize is not of the type that setting takes.")]
    [InlineData("""{"MailAccounts":{"Defaults":{"Folders":[{"Aliass":"inbox"}]}}}""", "MailAccounts:Defaults names 'Aliass' inside a list")]
    public void Judge_AValueOfTheWrongType_IsRefusedNamingThePath(string saved, string expected)
    {
        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains(candidate.Refusals, refusal => refusal.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>What the binder cannot refuse is asked beside it: an unknown member, a rule the property declares, and a written name.</summary>
    [Theory]
    [InlineData("""{"MailAccounts":{"Forced":{"Mode":7}}}""", "MailAccounts:Forced gives Mode a value that names none of ")]
    [InlineData("""{"MailAccounts":{"Defaults":{"Port":70000}}}""", "MailAccounts:Defaults gives Port a value that setting refuses")]
    [InlineData("""{"Users":{"Defaults":{"Language":"Klingon"}}}""", "Users:Defaults: Language states 'Klingon', which is not a language MailFathom writes in.")]
    [InlineData("""{"Users":{"Forced":{"TimeZone":"Europe/Warszawa"}}}""", "Users:Forced: TimeZone states 'Europe/Warszawa'")]
    [InlineData("""{"Users":{"Forced":{"ClientTelemetryLevel":"Verbose"}}}""", "Users:Forced: ClientTelemetryLevel states 'Verbose'")]
    [InlineData("""{"MailAccounts":{"Defaults":{"Language":"Klingon"}}}""", "MailAccounts:Defaults: Language states 'Klingon', which is not a language MailFathom writes in.")]
    public void Judge_AValueTheBinderAcceptsAndTheRecordWouldNot_IsRefusedByTheRecordsOwnRule(string saved, string expected)
    {
        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains(candidate.Refusals, refusal => refusal.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>
    /// A list is stated whole, so each entry of one is a whole value and is held to the rules the entry declares for
    /// itself — the one place a rule a record writes in code can be asked of a statement.
    /// </summary>
    [Theory]
    [InlineData("""[{"Alias":""}]""", "MailAccounts:Forced gives Folders an entry that list refuses: Configured folder aliases must be non-empty.")]
    [InlineData("""[{"Alias":"inbox"}]""", "MailAccounts:Forced gives Folders an entry that list refuses: Folder alias 'inbox' must name at least one of RemotePath and SpecialUse.")]
    public void Judge_AListEntryItsOwnRulesRefuse_IsRefusedByThatRule(string folders, string expected)
    {
        // Arrange
        var saved = $$$$"""{"MailAccounts":{"Forced":{"Folders":{{{{folders}}}}}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains(candidate.Refusals, refusal => refusal.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>
    /// Two lists carry their entry's rule as a method the account's validator calls rather than as one the entry
    /// declares. It reads the entry and nothing beside it, so it is asked of a stated list as it is of a record's —
    /// and its refusal names the entry's position and never the address or the domain the entry holds.
    /// </summary>
    [Theory]
    [InlineData("""{"TrustedSenders":[{"Domain":"partner.example"},{"Domain":"a.example","Address":"b@a.example"}]}""", "MailAccounts:Forced: TrustedSenders entry 1 must name exactly one of")]
    [InlineData("""{"TrustedSenders":[{"IncludeSubdomains":true}]}""", "MailAccounts:Forced: TrustedSenders entry 0 must name exactly one of")]
    [InlineData("""{"TrustedSenders":[{"Address":"b@a.example","IncludeSubdomains":true}]}""", "MailAccounts:Forced: TrustedSenders entry 0 must name exactly one of")]
    [InlineData("""{"ContactCollection":{"Exclusions":[{"Domain":"a.example","AddressPattern":"*@a.example"}]}}""", "MailAccounts:Forced: ContactCollection:Exclusions entry 0 must name exactly one of")]
    [InlineData("""{"ContactCollection":{"Exclusions":[{"Domain":"partner.example"},{"AddressPattern":"*@*"}]}}""", "MailAccounts:Forced: ContactCollection:Exclusions entry 1 must name exactly one of")]
    public void Judge_AListEntryTheAccountsOwnRuleRefuses_IsRefusedNamingItsPositionAndNotWhatItHolds(string forced, string expected)
    {
        // Arrange
        var saved = $$$"""{"MailAccounts":{"Forced":{{{forced}}}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.StartsWith(expected, Assert.Single(candidate.Refusals), StringComparison.Ordinal);
        Assert.DoesNotContain("a.example", candidate.Refusals[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"TrustedSenders":[{"Domain":"partner.example","IncludeSubdomains":true},{"Address":"b@a.example"}]}""")]
    [InlineData("""{"ContactCollection":{"Exclusions":[{"Domain":"partner.example"},{"AddressPattern":"no-reply@*"}]}}""")]
    public void Judge_AListWhoseEveryEntryTheAccountsOwnRuleAccepts_MayBeCommitted(string forced)
    {
        // Arrange
        var saved = $$$"""{"MailAccounts":{"Forced":{{{forced}}}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Empty(candidate.Refusals);
        Assert.NotNull(candidate.Json);
    }

    /// <summary>
    /// A key inside a list entry is repeated back only where it is shaped like a setting's name. Where a setting's name
    /// belongs in a list of trusted senders, what somebody is likeliest to have written instead is an address.
    /// </summary>
    [Theory]
    [InlineData("""{"TrustedSenders":[{"jane@example.test":true}]}""", "MailAccounts:Forced names something inside TrustedSenders that is not a setting an entry of that list carries.")]
    [InlineData("""{"TrustedSenders":[{"Realm":"a.example","jane@example.test":true}]}""", "MailAccounts:Forced names something inside TrustedSenders that is not a setting an entry of that list carries.")]
    [InlineData("""{"Folders":[{"Alias":"inbox","Remote Path":"INBOX"}]}""", "MailAccounts:Forced names something inside Folders that is not a setting an entry of that list carries.")]
    public void Judge_AListEntryKeyThatIsNotShapedLikeASettingsName_IsRefusedWithoutRepeatingIt(string forced, string expected)
    {
        // Arrange
        var saved = $$$"""{"MailAccounts":{"Forced":{{{forced}}}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.StartsWith(expected, Assert.Single(candidate.Refusals), StringComparison.Ordinal);
        Assert.DoesNotContain("jane", candidate.Refusals[0], StringComparison.Ordinal);
        Assert.DoesNotContain("Remote Path", candidate.Refusals[0], StringComparison.Ordinal);
    }

    /// <summary>A name an entry was given is text whoever wrote the policy chose, so one carrying a line break is not repeated back.</summary>
    [Fact]
    public void Judge_AListEntryNamedWithAControlCharacter_IsRefusedWithoutRepeatingTheName()
    {
        // Arrange
        const string saved = """{"MailAccounts":{"Forced":{"Folders":[{"Alias":"inbox\nDatabase error: reached"}]}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.NotEmpty(candidate.Refusals);
        Assert.All(candidate.Refusals, refusal => Assert.DoesNotContain("Database error", refusal, StringComparison.Ordinal));
    }

    /// <summary>
    /// Each stated value is bound on its own, so a value the binder cannot convert does not hide the faults beside it:
    /// whoever corrects a statement learns of all three here rather than one a save.
    /// </summary>
    [Fact]
    public void Judge_AStatementWithSeveralFaultyValues_ReportsEveryOneOfThem()
    {
        // Arrange
        const string saved = """{"MailAccounts":{"Forced":{"Port":"many","Mode":7,"Language":"Klingon"}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Equal(3, candidate.Refusals.Count);
        Assert.Contains(candidate.Refusals, refusal => refusal.Contains("gives Port is not of the type", StringComparison.Ordinal));
        Assert.Contains(candidate.Refusals, refusal => refusal.Contains("gives Mode a value that names none of", StringComparison.Ordinal));
        Assert.Contains(candidate.Refusals, refusal => refusal.Contains("Language states 'Klingon'", StringComparison.Ordinal));
    }

    /// <summary>A value shaped as its property is not would bind as something else, or as nothing, rather than be refused.</summary>
    [Theory]
    [InlineData("""{"Users":{"Defaults":{"Language":null}}}""", "Users:Defaults names Language and states no value for it.")]
    [InlineData("""{"Users":{"Defaults":{"Language":"  "}}}""", "Users:Defaults names Language and states no value for it.")]
    [InlineData("""{"MailAccounts":{"Forced":{"Folders":"inbox"}}}""", "MailAccounts:Forced gives Folders one value, and Folders is a list.")]
    [InlineData("""{"MailAccounts":{"Forced":{"Port":{"Value":993}}}}""", "MailAccounts:Forced gives Port more than one value, and Port takes one.")]
    [InlineData("""{"MailAccounts":{"Forced":{"Delivery":true}}}""", "MailAccounts:Forced gives Delivery one value, and Delivery is a block of settings.")]
    public void Judge_AValueShapedAsItsPropertyIsNot_IsRefusedNamingTheShape(string saved, string expected)
    {
        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains(candidate.Refusals, refusal => refusal.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>A property only an administrator writes takes a default and a forced value, and no editing mode makes it the person's.</summary>
    [Theory]
    [InlineData("EndpointAccess")]
    [InlineData("EndpointAccess:ClientEndpoint")]
    [InlineData("clienttelemetrylevel")]
    public void Judge_AnAllowListNamingAnAdministratorOnlyProperty_IsRefused(string listed)
    {
        // Arrange
        var saved = $$$$"""{"Users":{"Editing":{"Mode":"NoneExcept","Properties":["Language","{{{{listed}}}}"]}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains("by an administrator alone", Assert.Single(candidate.Refusals), StringComparison.Ordinal);
    }

    [Fact]
    public void Judge_AnAdministratorOnlyPropertyDefaultedForcedAndListedAsLocked_IsAccepted()
    {
        // Arrange
        const string saved =
            """
            {"Users":{
              "Defaults":{"ClientTelemetryLevel":"Warn"},
              "Forced":{"EndpointAccess":{"McpEndpoint":false}},
              "Editing":{"Mode":"AllExcept","Properties":["EndpointAccess","ClientTelemetryLevel"]}}}
            """;

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Empty(candidate.Refusals);
    }

    [Theory]
    [InlineData("""{"Mode":"Open"}""", "Users:Editing:Mode takes 'AllExcept' or 'NoneExcept'.")]
    [InlineData("""{"Mode":1}""", "Users:Editing:Mode takes 'AllExcept' or 'NoneExcept'.")]
    [InlineData("""{"Properties":["Language"]}""", "Users:Editing lists Properties and states no Mode")]
    [InlineData("""{"Mode":"AllExcept","Properties":"Language"}""", "Users:Editing:Properties is a list of property paths")]
    [InlineData("""{"Mode":"AllExcept","Properties":[1]}""", "Users:Editing:Properties is a list of property paths")]
    [InlineData("""{"Mode":"AllExcept","Properties":["Dialect"]}""", "Users:Editing:Properties lists Dialect, which is not a property of a user's record.")]
    [InlineData("""{"Mode":"AllExcept","Properties":["a b"]}""", "which is not a property path")]
    [InlineData("""{"Mode":"AllExcept","Properties":[""]}""", "which is not a property path")]
    public void Judge_AnEditingRestrictionThatSaysNothingUsable_IsRefused(string editing, string expected)
    {
        // Arrange
        var saved = $$$"""{"Users":{"Editing":{{{editing}}}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Contains(candidate.Refusals, refusal => refusal.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>A mode that cannot be read does not hide a path beside it that names nothing.</summary>
    [Theory]
    [InlineData("""{"Mode":"Open","Properties":["Dialect"]}""", "Users:Editing:Mode takes")]
    [InlineData("""{"Properties":["Dialect"]}""", "states no Mode")]
    public void Judge_AnEditingRestrictionWithAFaultyModeAndAFaultyPath_ReportsBoth(string editing, string expectedOfTheMode)
    {
        // Arrange
        var saved = $$$"""{"Users":{"Editing":{{{editing}}}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Equal(2, candidate.Refusals.Count);
        Assert.Contains(candidate.Refusals, refusal => refusal.Contains(expectedOfTheMode, StringComparison.Ordinal));
        Assert.Contains(candidate.Refusals, refusal => refusal.Contains("lists Dialect, which is not a property", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"Mode":"NoneExcept"}""")]
    [InlineData("""{"mode":"noneexcept","properties":[]}""")]
    [InlineData("""{}""")]
    public void Judge_AnEditingRestrictionListingNothing_IsAccepted(string editing)
    {
        // Arrange
        var saved = $$$"""{"MailAccounts":{"Editing":{{{editing}}}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Empty(candidate.Refusals);
    }

    /// <summary>A list is one value, so no path reaches inside one.</summary>
    [Fact]
    public void Judge_AnEditingPathReachingInsideAList_IsRefusedNamingTheList()
    {
        // Arrange
        const string saved = """{"MailAccounts":{"Editing":{"Mode":"AllExcept","Properties":["Folders:0:Alias"]}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Contains(
            "lists Folders:0:Alias, which reaches inside Folders, and a list is one value",
            Assert.Single(candidate.Refusals),
            StringComparison.Ordinal);
    }

    /// <summary>The one client preference a policy holds is the telemetry switch, and it holds it by forcing it.</summary>
    [Theory]
    [InlineData("""{"Forced":{"ClientPreferences":{"TelemetryEnabled":true}}}""", null)]
    [InlineData("""{"Forced":{"clientpreferences":{"telemetryEnabled":false}}}""", null)]
    [InlineData("""{"Defaults":{"ClientPreferences":{"TelemetryEnabled":true}}}""", "Users:Defaults states ClientPreferences:TelemetryEnabled, which takes a forced value and nothing else")]
    [InlineData("""{"Forced":{"ClientPreferences":{"TelemetryEnabled":"yes"}}}""", "Users:Forced gives ClientPreferences:TelemetryEnabled a value that is neither true nor false.")]
    [InlineData("""{"Forced":{"ClientPreferences":{"markReadOnOpen":true}}}""", "Users:Forced states ClientPreferences:MarkReadOnOpen, and a client preference is the person's own")]
    [InlineData("""{"Defaults":{"ClientPreferences":{"Theme":"Dark"}}}""", "Users:Defaults states ClientPreferences:Theme, and a client preference is the person's own")]
    [InlineData("""{"Forced":{"ClientPreferences":{"Colour":"red"}}}""", "Users:Forced names ClientPreferences:'Colour', which is not a client preference.")]
    [InlineData("""{"Forced":{"ClientPreferences":true}}""", "Users:Forced gives ClientPreferences one value")]
    [InlineData("""{"Editing":{"Mode":"AllExcept","Properties":["ClientPreferences:TelemetryEnabled"]}}""", "no editing restriction reaches a client preference")]
    [InlineData("""{"Editing":{"Mode":"NoneExcept","Properties":["ClientPreferences"]}}""", "no editing restriction reaches a client preference")]
    public void Judge_AStatementAboutAClientPreference_IsAcceptedOnlyAsAForcedTelemetrySwitch(string users, string? expected)
    {
        // Arrange
        var saved = $$$"""{"Users":{{{users}}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        if (expected is null)
        {
            Assert.Empty(candidate.Refusals);
        }
        else
        {
            Assert.Contains(expected, Assert.Single(candidate.Refusals), StringComparison.Ordinal);
        }
    }

    /// <summary>A mail account has no client preferences, so the name is an ordinary path that names nothing there.</summary>
    [Fact]
    public void Judge_AClientPreferenceInTheMailAccountSection_NamesNothing()
    {
        // Arrange
        const string saved = """{"MailAccounts":{"Forced":{"ClientPreferences":{"TelemetryEnabled":true}}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Contains(
            "MailAccounts:Forced names ClientPreferences, which is not a property of a mail account.",
            Assert.Single(candidate.Refusals),
            StringComparison.Ordinal);
    }

    /// <summary>Whoever corrects a policy one sentence at a time learns about the next only by saving it again.</summary>
    [Fact]
    public void Judge_APolicyWithSeveralFaults_ReportsEveryOneOfThem()
    {
        // Arrange
        const string saved =
            """
            {
              "Users": { "Defaults": { "Dialect": "Polish", "Portrait": "0197a3c0-0000-7000-8000-000000000001" } },
              "MailAccounts": { "Forced": { "Port": "many" }, "Editing": { "Mode": "Open" } }
            }
            """;

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        Assert.Null(candidate.Json);
        Assert.Equal(4, candidate.Refusals.Count);
    }

    /// <summary>A forced list of trusted senders is somebody's correspondents, so a refusal names the path and never what was written there.</summary>
    [Fact]
    public void Judge_ARefusedList_NeverRepeatsWhatItHeld()
    {
        // Arrange
        const string saved =
            """{"MailAccounts":{"Forced":{"TrustedSenders":[{"Address":"somebody@example.test","IncludeSubdomains":"sometimes"}]}}}""";

        // Act
        var candidate = SettingsPolicyCandidate.Judge(saved);

        // Assert
        var refusal = Assert.Single(candidate.Refusals);
        Assert.Contains("TrustedSenders:0:IncludeSubdomains", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("somebody@example.test", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("sometimes", refusal, StringComparison.Ordinal);
    }
}
