// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers which arrangements of an authentication entry the endpoint accepts.</summary>
/// <remarks>
/// A method is selected by carrying its own block, so the shapes worth stating are the ones an operator could write
/// believing they had turned something on. An entry stating nothing is the one that has to fail, because it is what a
/// misspelled block name binds to and it would otherwise be an endpoint quietly accepting one credential fewer.
/// </remarks>
public sealed class TransportAuthenticationOptionsTests
{
    private const string SettingPath = "McpEndpoint:Authentication:0";

    [Fact]
    public void StatesAMethod_AnEntryCarryingAPublicKey_SelectsThatMethod()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { PublicKey = APublicKey() };

        // Act, Assert
        Assert.True(entry.StatesAMethod);
    }

    [Fact]
    public void FindConfigurationErrors_AnEntryCarryingAPublicKey_ReportsNothing()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { PublicKey = APublicKey() };

        // Act, Assert
        Assert.Empty(entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail));
    }

    /// <summary>Nothing conflicts between the methods, so an operator who groups a key and a public key into one entry gets both rather than a refusal.</summary>
    [Fact]
    public void FindConfigurationErrors_AnEntryCarryingSeveralBlocks_ReportsNothing()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions
        {
            ApiKey = new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:a-key" },
            PublicKey = APublicKey(),
        };

        // Act, Assert
        Assert.Empty(entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail));
    }

    /// <summary>The refusal has to name every block an operator could have meant, or the one they misspelled goes unmentioned.</summary>
    [Fact]
    public void FindConfigurationErrors_AnEntryStatingNoMethod_NamesEveryBlockItCouldHaveCarried()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions();

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains(SettingPath, reported, StringComparison.Ordinal);
        Assert.Contains("ApiKey", reported, StringComparison.Ordinal);
        Assert.Contains("PublicKey", reported, StringComparison.Ordinal);
        Assert.Contains("OAuth", reported, StringComparison.Ordinal);
    }

    /// <summary>Every configured key is offered to the surface in configuration order, because rotation is a second entry rather than a nested list.</summary>
    [Fact]
    public void PublicKeysIn_SeveralEntries_ReportsEveryConfiguredKeyInOrder()
    {
        // Arrange
        TransportAuthenticationOptions[] entries =
        [
            new() { PublicKey = APublicKey("nightly") },
            new() { ApiKey = new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:a-key" } },
            new() { PublicKey = APublicKey("nightly-next") },
        ];

        // Act
        var publicKeys = TransportAuthenticationConfiguration.PublicKeysIn(entries);

        // Assert
        Assert.Equal(["nightly", "nightly-next"], publicKeys.Select(key => key.Name));
    }

    /// <summary>
    /// The pair this whole reading exists for. An absent key and an emptied list arrive from the binder identically
    /// and mean opposite things, so both are pinned together: one reaches the surface's whole half, the other reaches
    /// nothing and is how a credential is retired without its entry being deleted.
    /// </summary>
    [Fact]
    public void GrantedPermissions_AnEntryThatWroteNoGrant_ReachesTheWholeSurface()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.GrantTheWholeSurface();

        // Act
        var granted = entry.GrantedPermissions(ProtectedSurface.Mail);

        // Assert
        Assert.Equal(MailFathomPermission.PublishedFor(ProtectedSurface.Mail), granted);
    }

    [Fact]
    public void GrantedPermissions_AnEntryThatWroteAnEmptyGrant_ReachesNothing()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };

        // Act
        var granted = entry.GrantedPermissions(ProtectedSurface.Mail);

        // Assert
        Assert.Empty(granted);
    }

    /// <summary>An entry built anywhere but from configuration starts from the grant that reaches nothing, so nothing composed by hand inherits the permissive default.</summary>
    [Fact]
    public void Permissions_AnEntryTheBinderNeverTouched_StartsFromNothing()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions();

        // Assert
        Assert.Empty(entry.Permissions);
        Assert.False(entry.GrantsTheWholeSurface);
    }

    [Fact]
    public void GrantedPermissions_AnEntryNamingPermissions_ReachesExactlyThose()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add(MailFathomPermission.MailRead.Name);

        // Act
        var granted = entry.GrantedPermissions(ProtectedSurface.Mail);

        // Assert
        Assert.Equal([MailFathomPermission.MailRead], granted);
    }

    /// <summary>A grant is a set, and the order it resolves in is what the startup line reads in and what the claims are stamped in — so two entries granting the same permissions must not be reported as two different grants.</summary>
    [Fact]
    public void GrantedPermissions_AGrantWrittenOutOfOrder_ResolvesInThePublishedOrder()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add(MailFathomPermission.MailAsk.Name);
        entry.Permissions.Add(MailFathomPermission.MailRead.Name);

        // Act
        var granted = entry.GrantedPermissions(ProtectedSurface.Mail);

        // Assert
        Assert.Equal([MailFathomPermission.MailRead, MailFathomPermission.MailAsk], granted);
    }

    /// <summary>A name nothing publishes is a grant nobody enforces, so it fails startup rather than being written into a configuration file that reads as narrowed.</summary>
    [Fact]
    public void FindConfigurationErrors_AGrantNamingAnUnpublishedPermission_NamesTheEntryAndTheIndex()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add("mailfathom.mail.write");

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:Permissions", reported, StringComparison.Ordinal);
        Assert.Contains("mailfathom.mail.write", reported, StringComparison.Ordinal);
        Assert.Contains(MailFathomPermission.MailRead.Name, reported, StringComparison.Ordinal);
    }

    /// <summary>A cross-surface name would sit in the file granting nothing while an operator believed they had granted something.</summary>
    [Fact]
    public void FindConfigurationErrors_AGrantNamingTheOtherSurfacesPermission_IsRefused()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add(MailFathomPermission.AdminSpend.Name);

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:Permissions", reported, StringComparison.Ordinal);
        Assert.Contains(MailFathomPermission.AdminSpend.Name, reported, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_AGrantRepeatingAPermission_IsRefused()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add(MailFathomPermission.MailRead.Name);
        entry.Permissions.Add(MailFathomPermission.MailRead.Name);

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:Permissions", reported, StringComparison.Ordinal);
    }

    /// <summary>An emptied grant is a posture rather than a mistake, so it is the one arrangement here that is not refused.</summary>
    [Fact]
    public void FindConfigurationErrors_AnEmptiedGrant_ReportsNothing()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };

        // Act, Assert
        Assert.Empty(entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail));
    }

    /// <summary>Neither credential can carry a scope, so asking a token to narrow the grant beside one is a question nothing could answer.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FindConfigurationErrors_TokenScopeNarrowingBesideAConfiguredCredential_IsRefused(
        bool statesAnApiKey,
        bool statesAPublicKey)
    {
        // Arrange
        var entry = new TransportAuthenticationOptions
        {
            ApiKey = statesAnApiKey ? AnApiKey() : null,
            PublicKey = statesAPublicKey ? APublicKey() : null,
            PermissionsFromTokenScopes = true,
        };

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:PermissionsFromTokenScopes", reported, StringComparison.Ordinal);
    }

    /// <summary>It is the form an entry whose sole block is OAuth exists to use, so it must not be refused there.</summary>
    [Fact]
    public void FindConfigurationErrors_TokenScopeNarrowingOnAnOAuthOnlyEntry_ReportsNothing()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions
        {
            OAuth = AnOAuthBlock(),
            PermissionsFromTokenScopes = true,
        };

        // Act, Assert
        Assert.Empty(entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail));
    }

    /// <summary>Requiring a permission would close the door on a caller the deployment meant to serve less, which is the opposite of narrowing them.</summary>
    [Fact]
    public void FindConfigurationErrors_APermissionWrittenIntoTheRequiredScopes_IsRefused()
    {
        // Arrange
        var oauth = AnOAuthBlock();
        oauth.RequiredScopes.Add(MailFathomPermission.MailRead.Name);

        var entry = new TransportAuthenticationOptions { OAuth = oauth };

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:OAuth:RequiredScopes:0", reported, StringComparison.Ordinal);
        Assert.Contains(MailFathomPermission.MailRead.Name, reported, StringComparison.Ordinal);
    }

    /// <summary>The grant that reads a permission already advertises it, and an entry reading none would be telling a client to ask for something nothing here grants.</summary>
    [Fact]
    public void FindConfigurationErrors_APermissionWrittenIntoTheAdvertisedScopes_IsRefused()
    {
        // Arrange
        var oauth = AnOAuthBlock();
        oauth.AdvertisedScopes.Add(MailFathomPermission.MailAsk.Name);

        var entry = new TransportAuthenticationOptions { OAuth = oauth };

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:OAuth:AdvertisedScopes:0", reported, StringComparison.Ordinal);
        Assert.Contains(MailFathomPermission.MailAsk.Name, reported, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pairing is positional, so it means nothing once the children and the bound entries are different lengths.
    /// Marking anyway would read one entry's grant off another entry's key, so nothing is marked and every entry keeps
    /// the grant that reaches nothing — which is the direction to fail in if a binder ever drops an element.
    /// </summary>
    [Fact]
    public void ReadWhatTheBinderCannotSay_MoreChildrenThanEntries_LeavesEveryGrantAtTheRestrictiveDefault()
    {
        // Arrange
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["McpEndpoint:Authentication:0:ApiKey:Name"] = "workstation",
                ["McpEndpoint:Authentication:1:ApiKey:Name"] = "reporting-job",
            })
            .Build()
            .GetSection("McpEndpoint");

        var method = new TransportAuthenticationOptions { ApiKey = AnApiKey() };

        // Act
        TransportAuthenticationConfiguration.ReadWhatTheBinderCannotSay(section, [method]);

        // Assert
        Assert.False(method.GrantsTheWholeSurface);
        Assert.Null(method.ConfigurationKey);
    }

    /// <summary>The grant belongs to the entry, so which entry a credential sits in decides what it may do rather than only how the file was grouped.</summary>
    [Fact]
    public void GrantsByApiKeyName_TwoEntriesGrantedDifferently_MapsEachKeyToTheGrantOfItsOwnEntry()
    {
        // Arrange
        var narrowed = new TransportAuthenticationOptions { ApiKey = AnApiKey("reporting-job") };
        narrowed.Permissions.Add(MailFathomPermission.MailRead.Name);

        var unnarrowed = new TransportAuthenticationOptions { ApiKey = AnApiKey("workstation") };
        unnarrowed.GrantTheWholeSurface();

        // Act
        var grants = TransportAuthenticationConfiguration.GrantsByApiKeyName(
            [narrowed, unnarrowed],
            ProtectedSurface.Mail);

        // Assert
        Assert.Equal([MailFathomPermission.MailRead], grants["reporting-job"]);
        Assert.Equal(MailFathomPermission.PublishedFor(ProtectedSurface.Mail), grants["workstation"]);
    }

    [Fact]
    public void GrantsByPublicKeyName_AnEntryGrantedNothing_MapsItsKeyToAnEmptyGrant()
    {
        // Arrange
        var retired = new TransportAuthenticationOptions { PublicKey = APublicKey("nightly") };

        // Act
        var grants = TransportAuthenticationConfiguration.GrantsByPublicKeyName([retired], ProtectedSurface.Mail);

        // Assert
        Assert.Empty(grants["nightly"]);
    }

    /// <summary>The shorthand this whole shape exists for: one written value in place of a list an operator would otherwise revisit whenever a name is added beneath it.</summary>
    [Fact]
    public void GrantedPermissions_AGrantNamingASubtree_ReachesEveryPermissionBeneathIt()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add("mailfathom.mail.contacts.*");

        // Act
        var granted = entry.GrantedPermissions(ProtectedSurface.Mail);

        // Assert
        Assert.Equal(
            [MailFathomPermission.MailContactsRead, MailFathomPermission.MailContactsWrite],
            granted);
    }

    /// <summary>A subtree is shorthand for names rather than a value of its own, so it resolves into the same set a written-out grant would and in the same order.</summary>
    [Fact]
    public void GrantedPermissions_ASubtreeBesideAName_ResolvesBothIntoThePublishedOrder()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add("mailfathom.mail.contacts.*");
        entry.Permissions.Add(MailFathomPermission.MailRead.Name);

        // Act
        var granted = entry.GrantedPermissions(ProtectedSurface.Mail);

        // Assert
        Assert.Equal(
            [
                MailFathomPermission.MailRead,
                MailFathomPermission.MailContactsRead,
                MailFathomPermission.MailContactsWrite,
            ],
            granted);
    }

    /// <summary>Nothing downstream expands a pattern, so what a key is mapped to is what the claims, the startup line, and the session response all read.</summary>
    [Fact]
    public void GrantsByApiKeyName_AnEntryGrantingASubtree_MapsTheKeyToTheResolvedNames()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey("reporting-job") };
        entry.Permissions.Add("mailfathom.admin.audit.*");

        // Act
        var grants = TransportAuthenticationConfiguration.GrantsByApiKeyName(
            [entry],
            ProtectedSurface.Administration);

        // Assert
        Assert.Equal([MailFathomPermission.AdminAuditRead], grants["reporting-job"]);
    }

    [Fact]
    public void FindConfigurationErrors_AGrantNamingASubtreeOfItsOwnSurface_ReportsNothing()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add("mailfathom.admin.*");

        // Act, Assert
        Assert.Empty(entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Administration));
    }

    /// <summary>A prefix nothing sits beneath is a grant that reaches nothing while reading as a broad one, so it fails startup rather than being accepted as narrowed all the way.</summary>
    [Fact]
    public void FindConfigurationErrors_ASubtreeNothingIsPublishedBeneath_IsRefused()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add("mailfathom.post.*");

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:Permissions", reported, StringComparison.Ordinal);
        Assert.Contains("mailfathom.post.*", reported, StringComparison.Ordinal);
        Assert.Contains(MailFathomPermission.MailRead.Name, reported, StringComparison.Ordinal);
    }

    /// <summary>A cross-surface subtree would sit in the file reaching six names none of which this endpoint enforces.</summary>
    [Fact]
    public void FindConfigurationErrors_ASubtreeOfTheOtherSurface_IsRefused()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add("mailfathom.admin.*");

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:Permissions", reported, StringComparison.Ordinal);
        Assert.Contains("mailfathom.admin.*", reported, StringComparison.Ordinal);
    }

    /// <summary>Both spellings say what leaving the key out already says, and giving that posture a second spelling would leave two arrangements meaning one thing.</summary>
    [Theory]
    [InlineData("*")]
    [InlineData("mailfathom.*")]
    public void FindConfigurationErrors_AGrantReachingBothSurfaces_IsRefused(string written)
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add(written);

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:Permissions", reported, StringComparison.Ordinal);
        Assert.Contains(written, reported, StringComparison.Ordinal);
        Assert.Contains("Remove the key", reported, StringComparison.Ordinal);
    }

    /// <summary>A partial segment is no pattern, so the refusal has to be the one an unpublished name draws rather than one about a subtree matching nothing.</summary>
    [Fact]
    public void FindConfigurationErrors_AWildcardInsideASegment_IsRefusedAsAnUnpublishedName()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add("mailfathom.mail.c*");

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains("is not a permission MailFathom publishes", reported, StringComparison.Ordinal);
    }

    /// <summary>A grant carrying one permission twice says nothing twice, whichever of the two spellings reached it.</summary>
    [Fact]
    public void FindConfigurationErrors_ASubtreeCoveringAPermissionTheGrantAlreadyCarries_IsRefused()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add(MailFathomPermission.MailContactsRead.Name);
        entry.Permissions.Add("mailfathom.mail.contacts.*");

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains("mailfathom.mail.contacts.*", reported, StringComparison.Ordinal);
        Assert.Contains(MailFathomPermission.MailContactsRead.Name, reported, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_APermissionASubtreeAlreadyCarries_IsRefused()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add("mailfathom.mail.contacts.*");
        entry.Permissions.Add(MailFathomPermission.MailContactsWrite.Name);

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains(MailFathomPermission.MailContactsWrite.Name, reported, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ASubtreeInsideAnotherSubtree_IsRefused()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add("mailfathom.mail.*");
        entry.Permissions.Add("mailfathom.mail.contacts.*");

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains("mailfathom.mail.contacts.*", reported, StringComparison.Ordinal);
    }

    /// <summary>Two subtrees that reach different names are an ordinary grant: nothing about a pattern makes a second one suspicious.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoDisjointSubtrees_ReportsNothing()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { ApiKey = AnApiKey() };
        entry.Permissions.Add("mailfathom.admin.audit.*");
        entry.Permissions.Add("mailfathom.admin.credentials.*");

        // Act, Assert
        Assert.Empty(entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Administration));
    }

    /// <summary>A scope is compared byte for byte at the authorization server, so a pattern is one no token could carry and no client could ask for.</summary>
    [Fact]
    public void FindConfigurationErrors_ASubtreeWrittenIntoTheRequiredScopes_IsRefused()
    {
        // Arrange
        var oauth = AnOAuthBlock();
        oauth.RequiredScopes.Add("mailfathom.mail.*");

        var entry = new TransportAuthenticationOptions { OAuth = oauth };

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:OAuth:RequiredScopes:0", reported, StringComparison.Ordinal);
        Assert.Contains("mailfathom.mail.*", reported, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ASubtreeWrittenIntoTheAdvertisedScopes_IsRefused()
    {
        // Arrange
        var oauth = AnOAuthBlock();
        oauth.AdvertisedScopes.Add("mailfathom.mail.*");

        var entry = new TransportAuthenticationOptions { OAuth = oauth };

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SettingPath}:OAuth:AdvertisedScopes:0", reported, StringComparison.Ordinal);
        Assert.Contains("mailfathom.mail.*", reported, StringComparison.Ordinal);
    }

    /// <summary>An asterisk is a perfectly good scope character, so what is refused is a value that would have granted something rather than every value spelled with one.</summary>
    [Fact]
    public void FindConfigurationErrors_AnotherResourcesWildcardScope_IsAccepted()
    {
        // Arrange
        var oauth = AnOAuthBlock();
        oauth.RequiredScopes.Add("files.read.*");
        oauth.AdvertisedScopes.Add("calendar.*");

        var entry = new TransportAuthenticationOptions { OAuth = oauth };

        // Act, Assert
        Assert.Empty(entry.FindConfigurationErrors(SettingPath, ProtectedSurface.Mail));
    }

    private static ConfiguredSecret APublicKey(string name = "nightly") =>
        new() { Name = name, SecretReference = "file:/etc/mailfathom/nightly.pub" };

    private static ConfiguredSecret AnApiKey(string name = "workstation") =>
        new() { Name = name, SecretReference = "plaintext:a-key" };

    private static OAuthValidationOptions AnOAuthBlock()
    {
        var oauth = new OAuthValidationOptions { Resource = "https://mail.example.test/mcp" };

        var authorizationServer = new AuthorizationServerOptions
        {
            Name = "workforce",
            Issuer = "https://sso.example.test",
        };

        authorizationServer.AuthorizedSubjects.Add("11111111-2222-3333-4444-555555555555");
        oauth.AuthorizationServers.Add(authorizationServer);

        return oauth;
    }
}
