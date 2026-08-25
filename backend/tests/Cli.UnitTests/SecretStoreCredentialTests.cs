// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Credentials;
using MailFathom.Cli.Credentials.SecretStores;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what the credential store does once a machine has a place for secrets that is not its own file.</summary>
/// <remarks>
/// Two arrangements rather than one, and every test here is about which of them a profile ended up under: what the file
/// then holds, what it stops holding, what happens on a machine that has no store at all, and what happens to a profile
/// written before there was one. <see cref="CredentialStoreTests" /> covers the file itself and deliberately runs
/// against no store, which is the arrangement it was written about.
/// </remarks>
public sealed class SecretStoreCredentialTests : IDisposable
{
    private static readonly Uri Production = new("https://mail.example.test:8443");
    private static readonly Uri Staging = new("https://staging.example.test:8443");

    private readonly string storeDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-cli-tests-{Guid.NewGuid():N}");

    private readonly FakeOperatorSecretStore secretStore = new();

    private readonly CredentialStore store;

    public SecretStoreCredentialTests() => this.store = this.CreateStore(this.secretStore);

    /// <summary>The whole point of the change: the file records what a profile is, and the platform records what it knows.</summary>
    [Fact]
    public void Save_AStoreThatAccepts_LeavesNoSecretInTheFile()
    {
        // Arrange, Act
        this.store.Save("production", Production, "not-a-real-token", "workstation");

        // Assert
        var stored = this.store.Read().Profiles["production"];

        Assert.Null(stored.Token);
        Assert.Equal("https://mail.example.test:8443", stored.Endpoint);
        Assert.Equal("workstation", stored.Credential);
        Assert.DoesNotContain(
            "not-a-real-token",
            File.ReadAllText(this.StorePath),
            StringComparison.Ordinal);
    }

    /// <summary>Nothing seals anything, so the weaker arrangement's key never comes into existence.</summary>
    [Fact]
    public void Save_AStoreThatAccepts_WritesNoSealingKey()
    {
        // Arrange, Act
        this.store.Save("production", Production, "not-a-real-token", "workstation");

        // Assert
        Assert.False(File.Exists(this.KeyPath));
    }

    [Fact]
    public void Save_AStoreThatAccepts_ReportsThatThePlatformHoldsTheCredential()
    {
        // Arrange, Act
        var placement = this.store.Save("production", Production, "not-a-real-token", "workstation");

        // Assert
        Assert.Equal("the platform's secret store", placement.Store);
        Assert.Null(placement.Refusal);
        Assert.Equal("The credential is held by the platform's secret store.", placement.Describe());
    }

    /// <summary>A headless host is the ordinary case rather than a broken one, and it has to keep working.</summary>
    [Fact]
    public void Save_AnUnavailableStore_SealsIntoTheFileInstead()
    {
        // Arrange
        this.secretStore.Refusal = "there is no D-Bus session bus here";

        // Act
        var placement = this.store.Save("production", Production, "not-a-real-token", "workstation");

        // Assert
        Assert.NotNull(this.store.Read().Profiles["production"].Token);
        Assert.Empty(this.secretStore.Entries);
        Assert.Equal("not-a-real-token", this.store.Resolve("production").Token);
        Assert.True(File.Exists(this.KeyPath));
        Assert.Null(placement.Store);
    }

    /// <summary>Taking the weaker storage silently is the failure this sentence exists to prevent.</summary>
    [Fact]
    public void Save_AnUnavailableStore_SaysWhichOfTheTwoHoldsTheCredentialAndWhy()
    {
        // Arrange
        this.secretStore.Refusal = "there is no D-Bus session bus here";

        // Act
        var placement = this.store.Save("production", Production, "not-a-real-token", "workstation");

        // Assert
        Assert.Equal(
            "The credential is sealed in the credentials file under a key beside it, because there is no D-Bus session bus here.",
            placement.Describe());
    }

    [Fact]
    public void Resolve_AProfileThePlatformHolds_ReadsTheCredentialBack()
    {
        // Arrange
        this.store.Save("production", Production, "not-a-real-token", "workstation");

        // Act
        var profile = this.store.Resolve("production");

        // Assert
        Assert.Equal("not-a-real-token", profile.Token);
        Assert.Equal("workstation", profile.Credential);
    }

    /// <summary>An OAuth profile's refresh token is the longer-lived of its two secrets, so it goes where the other one does.</summary>
    [Fact]
    public void Save_AnOAuthProfile_KeepsBothSecretsInTheStoreAndNeitherInTheFile()
    {
        // Arrange
        var session = Session();

        // Act
        this.store.Save("production", Production, "access-token", "workstation", session);

        // Assert
        var stored = this.store.Read().Profiles["production"];

        Assert.Null(stored.Token);
        Assert.Null(stored.Session?.RefreshToken);
        Assert.Equal("refresh-token", this.store.Resolve("production").Session?.RefreshToken);
        Assert.Equal(2, this.secretStore.Entries.Count);
    }

    /// <summary>One deployment's credential must never be presented to another, which is what the address key promises.</summary>
    [Fact]
    public void Save_TwoDeployments_KeysEachProfilesSecretsByItsOwnAddress()
    {
        // Arrange, Act
        this.store.Save("production", Production, "production-token", "workstation");
        this.store.Save("staging", Staging, "staging-token", "workstation");

        // Assert
        Assert.Equal(
            "production-token",
            this.secretStore.Entries[FakeOperatorSecretStore.KeyOf(ProfileSecret.BearerToken("https://mail.example.test:8443"))]);
        Assert.Equal(
            "staging-token",
            this.secretStore.Entries[FakeOperatorSecretStore.KeyOf(ProfileSecret.BearerToken("https://staging.example.test:8443"))]);
        Assert.Equal("production-token", this.store.Resolve("production").Token);
        Assert.Equal("staging-token", this.store.Resolve("staging").Token);
    }

    /// <summary>The entry being gone and the keyring being locked are different things, and only one of them is answered by signing in again.</summary>
    [Fact]
    public void Resolve_AProfileTheStoreNoLongerHolds_SaysToStoreTheCredentialAgain()
    {
        // Arrange
        this.store.Save("production", Production, "not-a-real-token", "workstation");
        this.secretStore.Clear(ProfileSecret.BearerToken("https://mail.example.test:8443"));

        // Act
        var failure = Assert.Throws<CliFailure>(() => this.store.Resolve("production"));

        // Assert
        Assert.Contains("no longer holds the credential", failure.Message, StringComparison.Ordinal);
        Assert.Contains("mfctl login --endpoint", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A store failure is reported rather than swallowed: falling back to a file that holds nothing would fail later and say less.</summary>
    [Fact]
    public void Resolve_AStoreThatHasBecomeUnreachable_NamesTheStoreRatherThanTheFile()
    {
        // Arrange
        this.store.Save("production", Production, "not-a-real-token", "workstation");
        this.secretStore.Refusal = "the collection is locked";

        // Act
        var failure = Assert.Throws<CliFailure>(() => this.store.Resolve("production"));

        // Assert
        Assert.Contains("held by this machine's secret store", failure.Message, StringComparison.Ordinal);
        Assert.Contains("the collection is locked", failure.Message, StringComparison.Ordinal);
        Assert.Contains("mfctl login --endpoint", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An operator upgrading the command does not sign in again, and does not keep a key file that protects nothing.</summary>
    [Fact]
    public void Resolve_AProfileSealedBeforeThereWasAStore_MovesItAndRemovesTheKeyFile()
    {
        // Arrange: a sign-in on a machine that had no store at all.
        this.CreateStore(secretStore: null).Save("production", Production, "not-a-real-token", "workstation");

        Assert.True(File.Exists(this.KeyPath));

        // Act
        var profile = this.store.Resolve("production");

        // Assert
        Assert.Equal("not-a-real-token", profile.Token);
        Assert.Null(this.store.Read().Profiles["production"].Token);
        Assert.Equal(
            "not-a-real-token",
            this.secretStore.Entries[FakeOperatorSecretStore.KeyOf(ProfileSecret.BearerToken("https://mail.example.test:8443"))]);
        Assert.False(File.Exists(this.KeyPath));
    }

    /// <summary>An OAuth profile moves whole or not at all, because a session split between two places opens under neither.</summary>
    [Fact]
    public void Resolve_AnOAuthProfileSealedBeforeThereWasAStore_MovesBothSecrets()
    {
        // Arrange
        this.CreateStore(secretStore: null)
            .Save("production", Production, "access-token", "workstation", Session());

        // Act
        var profile = this.store.Resolve("production");

        // Assert
        var stored = this.store.Read().Profiles["production"];

        Assert.Equal("refresh-token", profile.Session?.RefreshToken);
        Assert.Null(stored.Token);
        Assert.Null(stored.Session?.RefreshToken);
        Assert.Equal(2, this.secretStore.Entries.Count);
    }

    /// <summary>A move that cannot complete leaves the profile exactly as it was, rather than half of it in each place.</summary>
    [Fact]
    public void Resolve_AProfileSealedAndAStoreThatRefuses_LeavesTheSealedProfileReadable()
    {
        // Arrange
        this.CreateStore(secretStore: null).Save("production", Production, "not-a-real-token", "workstation");
        this.secretStore.Refusal = "libsecret is not installed here";

        // Act
        var profile = this.store.Resolve("production");

        // Assert
        Assert.Equal("not-a-real-token", profile.Token);
        Assert.NotNull(this.store.Read().Profiles["production"].Token);
        Assert.True(File.Exists(this.KeyPath));
    }

    /// <summary>Such a profile stores no credential today, so it stores none in either place and needs no key file either.</summary>
    [Fact]
    public void Save_AKeyPairProfile_KeepsNoSecretInEitherPlace()
    {
        // Arrange, Act
        this.store.Save(
            "production",
            Production,
            string.Empty,
            "workstation",
            keyPair: new StoredKeyPair("/keys/mfctl.pem"));

        // Assert
        var stored = this.store.Read().Profiles["production"];

        Assert.Null(stored.Token);
        Assert.Empty(this.secretStore.Entries);
        Assert.False(File.Exists(this.KeyPath));
        Assert.Equal(string.Empty, this.store.Resolve("production").Token);
    }

    /// <summary>Forgetting a profile has to reach both places, or the credential outlives the profile that named it.</summary>
    [Fact]
    public void Remove_AProfileThePlatformHolds_ClearsTheStoreEntriesAsWellAsTheFileEntry()
    {
        // Arrange
        this.store.Save("production", Production, "access-token", "workstation", Session());

        // Act
        var removal = this.store.Remove("production");

        // Assert
        Assert.True(removal.Removed);
        Assert.Null(removal.Uncleared);
        Assert.Empty(this.secretStore.Entries);
        Assert.Empty(this.store.Read().Profiles);
    }

    /// <summary>An entry nothing here can reach is the operator's to remove, which they can only do once they are told it is there.</summary>
    [Fact]
    public void Remove_AProfileWhoseStoreHasGoneAway_ReportsWhatWasLeftBehind()
    {
        // Arrange
        this.store.Save("production", Production, "not-a-real-token", "workstation");
        this.secretStore.Refusal = "the collection is locked";

        // Act
        var removal = this.store.Remove("production");

        // Assert
        Assert.True(removal.Removed);
        Assert.Equal("the collection is locked", removal.Uncleared);
        Assert.Empty(this.store.Read().Profiles);
    }

    /// <summary>A profile that never used the platform store reports nothing about it, rather than a refusal it was always going to get.</summary>
    [Fact]
    public void Remove_ASealedProfileOnAMachineWithNoStore_ReportsNothingLeftBehind()
    {
        // Arrange
        var sealedStore = this.CreateStore(secretStore: null);

        sealedStore.Save("production", Production, "not-a-real-token", "workstation");

        // Act
        var removal = sealedStore.Remove("production");

        // Assert
        Assert.True(removal.Removed);
        Assert.Null(removal.Uncleared);
    }

    /// <summary>The entries belong to the deployment rather than to one name for it, so a second profile reaching it keeps them.</summary>
    [Fact]
    public void Remove_ADeploymentAnotherProfileStillReaches_LeavesItsEntriesAlone()
    {
        // Arrange
        this.store.Save("production", Production, "production-token", "workstation");
        this.store.Save("mirror", Production, "production-token", "workstation");

        // Act
        this.store.Remove("mirror");

        // Assert
        Assert.Equal("production-token", this.store.Resolve("production").Token);
        Assert.Single(this.secretStore.Entries);
    }

    /// <summary>A renewal replaces one value wherever that profile keeps it, and moves nothing between the two places.</summary>
    [Fact]
    public void RenewAccessToken_AProfileThePlatformHolds_ReplacesTheEntryAndLeavesTheFileWithoutASecret()
    {
        // Arrange
        this.store.Save("production", Production, "access-token", "workstation", Session());

        // Act
        this.store.RenewAccessToken("production", "renewed-token", DateTimeOffset.UnixEpoch.AddDays(2));

        // Assert
        var profile = this.store.Resolve("production");

        Assert.Equal("renewed-token", profile.Token);
        Assert.Equal("refresh-token", profile.Session?.RefreshToken);
        Assert.Null(this.store.Read().Profiles["production"].Token);
    }

    /// <summary>A key file left behind would be material protecting nothing, so the last profile to move takes it with it.</summary>
    [Fact]
    public void Save_OneSealedProfileBesideOneTheStoreTook_KeepsTheKeyUntilNothingNeedsIt()
    {
        // Arrange: staging is signed in while the store refuses, production while it answers.
        this.secretStore.Refusal = "the collection is locked";
        this.store.Save("staging", Staging, "staging-token", "workstation");
        this.secretStore.Refusal = null;

        // Act
        this.store.Save("production", Production, "production-token", "workstation");

        // Assert: staging is still sealed, so the key stays.
        Assert.True(File.Exists(this.KeyPath));

        // Act: and goes once the sealed profile does.
        this.store.Remove("staging");

        // Assert
        Assert.False(File.Exists(this.KeyPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }

    private static OAuthSession Session() => new(
        "refresh-token",
        DateTimeOffset.UnixEpoch.AddDays(1),
        new Uri("https://issuer.example.test/token"),
        "https://issuer.example.test",
        "mfctl",
        "https://mail.example.test:8443",
        "openid");

    private string StorePath => Path.Combine(this.storeDirectory, "credentials.json");

    private string KeyPath => Path.Combine(this.storeDirectory, "credentials.key");

    private CredentialStore CreateStore(IOperatorSecretStore? secretStore) =>
        new(this.StorePath, new TokenProtector(this.KeyPath), secretStore);
}
