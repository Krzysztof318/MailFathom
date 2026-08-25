// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Configuration;
using Xunit;

namespace MailFathom.Application.UnitTests.Configuration;

/// <summary>
/// Covers what a resolved write target tells its caller. A refused write is answered rather than raised, so the
/// message is part of the contract: it names the setting, says where the setting is configured instead, and repeats no
/// value.
/// </summary>
public sealed class ConfigurationWriteTargetTests
{
    /// <summary>A routed target is writable and carries the store rather than a reason it is not.</summary>
    [Fact]
    public void RoutedTo_Store_IsWritableAndCarriesNoRefusal()
    {
        // Act
        var target = ConfigurationWriteTarget.RoutedTo(ConfigurationStorageRoute.OwnerAccounts);

        // Assert
        Assert.True(target.IsWritable);
        Assert.Equal(ConfigurationStorageRoute.OwnerAccounts, target.Route);
        Assert.Null(target.RefusalMessage);
    }

    /// <summary>
    /// The struct default is not a store, and a target built from it would report a writable path with nowhere to write
    /// it. That is a caller defect rather than an outcome an administrator reads.
    /// </summary>
    [Fact]
    public void RoutedTo_UnspecifiedRoute_IsRejectedAsAnArgument()
    {
        // Act
        var rejection = Record.Exception(() => ConfigurationWriteTarget.RoutedTo(default));

        // Assert
        Assert.IsType<ArgumentException>(rejection);
    }

    /// <summary>A refused target names the setting and offers the sources the bootstrap read takes it from.</summary>
    [Fact]
    public void RefusedAsBootstrapOnly_RefusedSettingItself_NamesItOnce()
    {
        // Act
        var target = ConfigurationWriteTarget.RefusedAsBootstrapOnly("Secrets:Interpretation", "Secrets:Interpretation");

        // Assert
        Assert.False(target.IsWritable);
        Assert.False(target.Route.IsSpecified);
        Assert.NotNull(target.RefusalMessage);
        Assert.Contains("Secrets:Interpretation", target.RefusalMessage, StringComparison.Ordinal);
        Assert.Contains("command-line argument", target.RefusalMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("which is part of", target.RefusalMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// A path beneath a refused section is refused by that section, so the message names both: the path the caller
    /// wrote, which is what they will look for, and the setting the deny-list declares, which is what explains it.
    /// </summary>
    [Fact]
    public void RefusedAsBootstrapOnly_PathBeneathARefusedSection_NamesThePathAndTheSection()
    {
        // Act
        var target = ConfigurationWriteTarget.RefusedAsBootstrapOnly("Persistence:Password:SecretReference", "Persistence:Password");

        // Assert
        Assert.NotNull(target.RefusalMessage);
        Assert.Contains("Persistence:Password:SecretReference, which is part of Persistence:Password", target.RefusalMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// A section carrying a refused setting is refused the other way round, and the message says so: what the caller
    /// has to do about it is write a narrower path, which reads differently from removing a value.
    /// </summary>
    [Fact]
    public void RefusedAsBootstrapOnly_SectionCarryingARefusedSetting_SaysWhatItContains()
    {
        // Act
        var target = ConfigurationWriteTarget.RefusedAsBootstrapOnly("Persistence", "Persistence:Password");

        // Assert
        Assert.NotNull(target.RefusalMessage);
        Assert.Contains("Persistence, which contains Persistence:Password", target.RefusalMessage, StringComparison.Ordinal);
    }
}
