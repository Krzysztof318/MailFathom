// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Owners;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.OwnerSettings;

/// <summary>
/// Asserts what an owner's document has to be before it is a record this deployment acts on. The binder is the one
/// place both directions meet — a row read back and a candidate about to be written — so every rule proved here is a
/// rule a write is refused by and a rule a read refuses to act on.
/// </summary>
public sealed class OwnerAccountDocumentBinderTests
{
    private const string PasswordReference = "file:/run/secrets/work-password";

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

    /// <summary>
    /// The identifier and the display name are unique within the owner and nowhere else, so two owners each declaring
    /// <c>work</c> is a pair of ordinary records rather than a collision either of them is refused for.
    /// </summary>
    [Fact]
    public void Bind_TwoOwnersDeclaringTheSameAccountName_BindsBothRecords()
    {
        // Arrange
        var binder = CreateBinder();

        // Act
        var first = binder.Bind(DocumentDeclaring(("work", "The work mailbox")));
        var second = binder.Bind(DocumentDeclaring(("work", "The work mailbox")));

        // Assert
        Assert.True(first.IsBound);
        Assert.True(second.IsBound);
    }

    /// <summary>Within one owner the same identifier twice is a name that could select either mailbox.</summary>
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

    /// <summary>The ceiling is applied before the parse, so a payload in the column costs the expansion it refuses.</summary>
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

    private static OwnerAccountDocumentBinder CreateBinder() =>
        new(new PersistedSecretMaterial(DeclaredSecretScheme.Registered));

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
