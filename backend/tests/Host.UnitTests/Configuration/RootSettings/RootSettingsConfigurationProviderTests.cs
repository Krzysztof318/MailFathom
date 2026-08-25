// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Infrastructure.Persistence.Settings;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.RootSettings;

/// <summary>
/// Covers what the persisted configuration document becomes once it is a configuration source: the keys it publishes,
/// the snapshot it holds, and what a reload does when the candidate is not usable.
/// </summary>
/// <remarks>
/// The flattening is the framework's own, which is the property under test rather than an implementation detail: a
/// persisted setting has to arrive at a binder as the same key an equivalent JSON file would have produced, because
/// that is what lets one options class read either without knowing which source supplied it.
/// </remarks>
public sealed class RootSettingsConfigurationProviderTests
{
    /// <summary>A nested object becomes colon-delimited keys, exactly as the same JSON in a file would.</summary>
    [Fact]
    public void Load_NestedObject_PublishesColonDelimitedKeys()
    {
        // Arrange
        var provider = LoadedProvider("""{ "MailboxSearch": { "SnippetsPerEmail": 3 } }""");

        // Act
        var found = provider.TryGet("MailboxSearch:SnippetsPerEmail", out var value);

        // Assert
        Assert.True(found);
        Assert.Equal("3", value);
    }

    /// <summary>An array's elements take the ordinary numeric child keys, which is what lets one index override one index.</summary>
    [Fact]
    public void Load_Array_PublishesNumericChildKeys()
    {
        // Arrange
        var provider = LoadedProvider("""{ "Rules": [ { "Name": "first" }, { "Name": "second" } ] }""");

        // Act
        provider.TryGet("Rules:0:Name", out var first);
        provider.TryGet("Rules:1:Name", out var second);

        // Assert
        Assert.Equal("first", first);
        Assert.Equal("second", second);
    }

    /// <summary>An empty document publishes no key at all, which is what a deployment that has persisted nothing looks like.</summary>
    [Fact]
    public void Load_EmptyDocument_PublishesNoKey()
    {
        // Arrange
        var provider = LoadedProvider("{}");

        // Act
        var keys = provider.GetChildKeys([], parentPath: null);

        // Assert
        Assert.Empty(keys);
    }

    /// <summary>A document that is not a configuration object fails the composition it was loaded into.</summary>
    [Fact]
    public void Load_DocumentThatIsNotAnObject_Fails()
    {
        // Arrange
        var provider = new RootSettingsConfigurationProvider(new RootSettingsDocument("[1, 2]", Version: 4));

        // Act
        var refusal = Record.Exception(provider.Load);

        // Assert
        Assert.IsType<FormatException>(refusal);
    }

    /// <summary>A reload replaces the snapshot whole, so a key the new document dropped stops being published.</summary>
    [Fact]
    public void Apply_LaterDocument_ReplacesTheSnapshotRatherThanMergingIntoIt()
    {
        // Arrange
        var provider = LoadedProvider("""{ "Kept": "before", "Dropped": "gone" }""");

        // Act
        provider.Apply(new RootSettingsDocument("""{ "Kept": "after" }""", Version: 8));

        // Assert
        provider.TryGet("Kept", out var kept);
        Assert.Equal("after", kept);
        Assert.False(provider.TryGet("Dropped", out _));
        Assert.Equal(8, provider.Version);
    }

    /// <summary>A reload raises the change token, which is what republishes the values to everything bound to them.</summary>
    [Fact]
    public void Apply_LaterDocument_RaisesTheChangeToken()
    {
        // Arrange
        var provider = LoadedProvider("""{ "Kept": "before" }""");
        var reloaded = false;

        provider.GetReloadToken().RegisterChangeCallback(_ => reloaded = true, state: null);

        // Act
        provider.Apply(new RootSettingsDocument("""{ "Kept": "after" }""", Version: 2));

        // Assert
        Assert.True(reloaded);
    }

    /// <summary>
    /// A candidate that does not parse leaves the version in force exactly where it was, rather than emptying the layer
    /// and letting the sources beneath it answer for settings the deployment had already adopted. The change token
    /// stays unraised with it: a refusal changed nothing, so anything bound to the layer has nothing to re-read, and
    /// raising the token anyway would push every options monitor through a rebind for a document nobody adopted.
    /// </summary>
    [Fact]
    public void Apply_CandidateThatIsNotAnObject_KeepsTheLastValidSnapshotAndRaisesNoChangeToken()
    {
        // Arrange
        var provider = LoadedProvider("""{ "Kept": "before" }""");
        var reloaded = false;

        provider.GetReloadToken().RegisterChangeCallback(_ => reloaded = true, state: null);

        // Act
        var refusal = Record.Exception(() => provider.Apply(new RootSettingsDocument("\"not settings\"", Version: 9)));

        // Assert
        Assert.IsType<FormatException>(refusal);
        Assert.False(reloaded);
        provider.TryGet("Kept", out var kept);
        Assert.Equal("before", kept);
        Assert.Equal(1, provider.Version);
    }

    private static RootSettingsConfigurationProvider LoadedProvider(string json)
    {
        var provider = new RootSettingsConfigurationProvider(new RootSettingsDocument(json, Version: 1));

        provider.Load();

        return provider;
    }
}
