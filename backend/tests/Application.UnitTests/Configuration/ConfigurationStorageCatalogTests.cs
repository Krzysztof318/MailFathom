// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Configuration;
using Xunit;

namespace MailFathom.Application.UnitTests.Configuration;

/// <summary>
/// Covers where each writable configuration path is persisted. What the catalog is for is that the answer is the same
/// on every call and comes from compiled code, so the tests state the routing, the exclusion the routing implies, and
/// the two things the catalog refuses: a bootstrap setting, and a store named by whoever asked.
/// </summary>
public sealed class ConfigurationStorageCatalogTests
{
    /// <summary>Almost every setting has no store of its own and is persisted in the deployment's one document.</summary>
    [Theory]
    [InlineData("MailboxSearch:SnippetsPerEmail")]
    [InlineData("Chat")]
    [InlineData("MailSynchronization:PollIntervalSeconds")]
    public void ResolveWriteTarget_PathWithNoSpecialRoute_IsPersistedInTheRootDocument(string configurationPath)
    {
        // Act
        var target = ConfigurationStorageCatalog.ResolveWriteTarget(configurationPath);

        // Assert
        Assert.True(target.IsWritable);
        Assert.Equal(ConfigurationStorageRoute.RootDocument, target.Route);
        Assert.Null(target.RefusalMessage);
    }

    /// <summary>
    /// The owner-account collection is the first special route, and everything beneath it travels with it: an owner's
    /// document is one row in its own store rather than a subtree of the deployment's document.
    /// </summary>
    [Theory]
    [InlineData("Accounts")]
    [InlineData("Accounts:0")]
    [InlineData("Accounts:0:DisplayName")]
    [InlineData("accounts:0:MailAccounts:1:Identifier")]
    public void ResolveWriteTarget_OwnerAccountCollection_IsPersistedInTheOwnerAccountsStore(string configurationPath)
    {
        // Act
        var target = ConfigurationStorageCatalog.ResolveWriteTarget(configurationPath);

        // Assert
        Assert.True(target.IsWritable);
        Assert.Equal(ConfigurationStorageRoute.OwnerAccounts, target.Route);
    }

    /// <summary>
    /// The mail-synchronization accounts are mailbox declarations rather than owners, and they carry the same word. The
    /// route is the top-level collection alone, so this one stays an ordinary deployment setting.
    /// </summary>
    [Theory]
    [InlineData("MailSynchronization:Accounts")]
    [InlineData("MailSynchronization:Accounts:0:Alias")]
    public void ResolveWriteTarget_MailSynchronizationAccounts_IsPersistedInTheRootDocument(string configurationPath)
    {
        // Act
        var target = ConfigurationStorageCatalog.ResolveWriteTarget(configurationPath);

        // Assert
        Assert.Equal(ConfigurationStorageRoute.RootDocument, target.Route);
    }

    /// <summary>
    /// A path that merely begins with a routed one names a different setting, and routing it would send a deployment
    /// setting into a store that has no shape for it.
    /// </summary>
    [Theory]
    [InlineData("AccountsReport:Schedule")]
    [InlineData("AccountsRetention")]
    public void ResolveWriteTarget_PathMerelyBeginningWithARoutedOne_IsPersistedInTheRootDocument(string configurationPath)
    {
        // Act
        var target = ConfigurationStorageCatalog.ResolveWriteTarget(configurationPath);

        // Assert
        Assert.Equal(ConfigurationStorageRoute.RootDocument, target.Route);
    }

    /// <summary>
    /// The catalog takes a configuration path and nothing else, so a caller naming a store or a relation instead of a
    /// setting reaches no store of its own: the name is read as the ordinary path it is. This is the whole of the
    /// refusal of a dynamic route — there is no argument, no configuration key, and no registration that adds one.
    /// </summary>
    [Theory]
    [InlineData("settings_accounts")]
    [InlineData("settings_root")]
    [InlineData("owner-accounts")]
    public void ResolveWriteTarget_StoreNameSuppliedAsThePath_IsPersistedInTheRootDocument(string configurationPath)
    {
        // Act
        var target = ConfigurationStorageCatalog.ResolveWriteTarget(configurationPath);

        // Assert
        Assert.Equal(ConfigurationStorageRoute.RootDocument, target.Route);
    }

    /// <summary>
    /// A write to a setting the persisted layer is itself reached through is refused where the write is validated,
    /// naming the setting, rather than committed into the layer it would have had to open to be read.
    /// </summary>
    [Theory]
    [InlineData("ConnectionStrings:mailfathom")]
    [InlineData("Persistence:ConnectionString")]
    [InlineData("Persistence:Password")]
    [InlineData("Persistence:CommandTimeoutSeconds")]
    [InlineData("Secrets:Interpretation")]
    [InlineData("ConfigurationSources:Directory")]
    [InlineData("ConfigurationSources:File")]
    public void ResolveWriteTarget_BootstrapSetting_IsRefusedNamingTheSetting(string configurationPath)
    {
        // Act
        var target = ConfigurationStorageCatalog.ResolveWriteTarget(configurationPath);

        // Assert
        Assert.False(target.IsWritable);
        Assert.False(target.Route.IsSpecified);
        Assert.Contains(configurationPath, target.RefusalMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// A secret block is a section, so the value that decides the credential sits beneath the refused name. The refusal
    /// names both, because the path the caller wrote is not the setting the deny-list declares.
    /// </summary>
    [Fact]
    public void ResolveWriteTarget_PathBeneathABootstrapSection_IsRefusedNamingBoth()
    {
        // Act
        var target = ConfigurationStorageCatalog.ResolveWriteTarget("Persistence:Password:SecretReference");

        // Assert
        Assert.False(target.IsWritable);
        Assert.Contains("Persistence:Password:SecretReference", target.RefusalMessage, StringComparison.Ordinal);
        Assert.Contains("part of Persistence:Password", target.RefusalMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// A write carries the subtree it names, so a section containing a refused setting would persist that setting as a
    /// child. Accepting it would put the credential into the document the next start refuses whole, which locks the
    /// deployment out of its own configuration through a write that had been validated.
    /// </summary>
    [Theory]
    [InlineData("Persistence", "Persistence:ConnectionString")]
    [InlineData("Secrets", "Secrets:Interpretation")]
    [InlineData("ConnectionStrings", "ConnectionStrings:mailfathom")]
    [InlineData("ConfigurationSources", "ConfigurationSources:Directory")]
    public void ResolveWriteTarget_SectionContainingABootstrapSetting_IsRefusedNamingWhatItContains(
        string configurationPath,
        string contained)
    {
        // Act
        var target = ConfigurationStorageCatalog.ResolveWriteTarget(configurationPath);

        // Assert
        Assert.False(target.IsWritable);
        Assert.Contains($"{configurationPath}, which contains {contained}", target.RefusalMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal above is a section-level one, so the settings beside a refused one in the same section stay writable.
    /// Widening it to the whole section would take ordinary settings away from an administrator.
    /// </summary>
    [Theory]
    [InlineData("Persistence:MaximumConcurrencyCommitAttempts")]
    [InlineData("Secrets:Files:0:Name")]
    public void ResolveWriteTarget_SettingBesideABootstrapOneInTheSameSection_IsWritable(string configurationPath)
    {
        // Act
        var target = ConfigurationStorageCatalog.ResolveWriteTarget(configurationPath);

        // Assert
        Assert.True(target.IsWritable);
        Assert.Equal(ConfigurationStorageRoute.RootDocument, target.Route);
    }

    /// <summary>A path nobody supplied is a caller defect rather than a refusal an administrator reads.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveWriteTarget_PathThatNamesNothing_IsRejectedAsAnArgument(string configurationPath)
    {
        // Act
        var rejection = Record.Exception(() => ConfigurationStorageCatalog.ResolveWriteTarget(configurationPath));

        // Assert
        Assert.IsType<ArgumentException>(rejection);
    }

    /// <summary>
    /// The exclusion is the routing read from the root document's side: a key the catalog routes elsewhere is one the
    /// deployment's document may not carry, because the store that owns it holds it already.
    /// </summary>
    [Theory]
    [InlineData("Accounts:0:DisplayName")]
    [InlineData("accounts")]
    public void FindRoutedElsewhereIn_DocumentCarryingARoutedPath_ReportsThePath(string persistedKey)
    {
        // Act
        var routed = ConfigurationStorageCatalog.FindRoutedElsewhereIn(["MailboxSearch:SnippetsPerEmail", persistedKey]);

        // Assert
        Assert.Equal(["Accounts"], routed);
    }

    /// <summary>An ordinary document reaches no other store, which is what the exclusion costs a correct deployment.</summary>
    [Fact]
    public void FindRoutedElsewhereIn_OrdinaryDocument_ReportsNothing()
    {
        // Act
        var routed = ConfigurationStorageCatalog.FindRoutedElsewhereIn(
            ["MailboxSearch:SnippetsPerEmail", "MailSynchronization:Accounts:0:Alias", "AccountsReport:Schedule"]);

        // Assert
        Assert.Empty(routed);
    }

    /// <summary>A path reported once however many keys reach it, so one repair answers the whole document.</summary>
    [Fact]
    public void FindRoutedElsewhereIn_SeveralKeysBeneathOneRoutedPath_ReportsThePathOnce()
    {
        // Act
        var routed = ConfigurationStorageCatalog.FindRoutedElsewhereIn(
            ["Accounts:0:DisplayName", "Accounts:1:DisplayName"]);

        // Assert
        Assert.Equal(["Accounts"], routed);
    }
}
