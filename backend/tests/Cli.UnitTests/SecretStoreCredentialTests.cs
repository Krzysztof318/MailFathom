// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
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
    private static readonly Uri Moved = new("https://mail.example.test:9443");

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
        var (_, placement) = this.store.Save("production", Production, "not-a-real-token", "workstation");

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
        var (_, placement) = this.store.Save("production", Production, "not-a-real-token", "workstation");

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
        var (_, placement) = this.store.Save("production", Production, "not-a-real-token", "workstation");

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
            this.secretStore.Entries[FakeOperatorSecretStore.KeyOf(ProfileSecret.BearerToken("production", "https://mail.example.test:8443"))]);
        Assert.Equal(
            "staging-token",
            this.secretStore.Entries[FakeOperatorSecretStore.KeyOf(ProfileSecret.BearerToken("staging", "https://staging.example.test:8443"))]);
        Assert.Equal("production-token", this.store.Resolve("production").Token);
        Assert.Equal("staging-token", this.store.Resolve("staging").Token);
    }

    /// <summary>The entry being gone and the keyring being locked are different things, and only one of them is answered by signing in again.</summary>
    [Fact]
    public void Resolve_AProfileTheStoreNoLongerHolds_SaysToStoreTheCredentialAgain()
    {
        // Arrange
        this.store.Save("production", Production, "not-a-real-token", "workstation");
        this.secretStore.Clear(ProfileSecret.BearerToken("production", "https://mail.example.test:8443"));

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
            this.secretStore.Entries[FakeOperatorSecretStore.KeyOf(ProfileSecret.BearerToken("production", "https://mail.example.test:8443"))]);
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

    /// <summary>One entry refusing says nothing about the other, and nothing comes back for a forgotten profile's second secret.</summary>
    /// <remarks>
    /// The Credential Manager refuses a delete per target, so a refusal here can be about one entry rather than about
    /// the store. Ending the sequence on the first would leave the refresh token — the longer-lived of the two — in
    /// the keyring with no attempt ever made, under a warning saying an entry is still in the store.
    /// </remarks>
    [Fact]
    public void Remove_AStoreThatRefusesOneOfTheTwoEntries_StillClearsTheOther()
    {
        // Arrange
        this.store.Save("production", Production, "access-token", "workstation", Session());
        this.secretStore.RefuseClearing(
            ProfileSecret.BearerToken("production", "https://mail.example.test:8443"),
            "that entry is denied");

        // Act
        var removal = this.store.Remove("production");

        // Assert
        Assert.Equal("that entry is denied", removal.Uncleared);
        Assert.Single(this.secretStore.Entries);
        Assert.True(
            this.secretStore.Entries.ContainsKey(
                FakeOperatorSecretStore.KeyOf(ProfileSecret.BearerToken("production", "https://mail.example.test:8443"))));
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

    /// <summary>An entry names the profile holding it, so forgetting one profile at a deployment cannot take another's credential with it.</summary>
    [Fact]
    public void Remove_OneOfTwoProfilesAtOneDeployment_LeavesTheOthersEntryAlone()
    {
        // Arrange
        this.store.Save("administrator", Production, "administrator-token", "workstation");
        this.store.Save("readonly", Production, "readonly-token", "reporting");

        // Act
        this.store.Remove("readonly");

        // Assert
        Assert.Equal("administrator-token", this.store.Resolve("administrator").Token);
        Assert.Single(this.secretStore.Entries);
    }

    /// <summary>Two profiles may name one deployment under different credentials, and each has to present its own.</summary>
    /// <remarks>
    /// An administrator's profile and a read-only one at the same address is the ordinary way this happens. Keyed by
    /// the address alone, the second sign-in would overwrite the first entry and each profile would then present the
    /// other identity — silently, because the file still records each profile's own credential name beside it.
    /// </remarks>
    [Fact]
    public void Save_TwoProfilesAtOneDeployment_KeepsEachProfilesOwnCredential()
    {
        // Arrange, Act
        this.store.Save("administrator", Production, "administrator-token", "workstation");
        this.store.Save("readonly", Production, "readonly-token", "reporting");

        // Assert
        Assert.Equal("administrator-token", this.store.Resolve("administrator").Token);
        Assert.Equal("readonly-token", this.store.Resolve("readonly").Token);
        Assert.Equal(2, this.secretStore.Entries.Count);
    }

    /// <summary>A session split between the two places opens under neither, so the half that was taken is put back.</summary>
    [Fact]
    public void Save_AStoreThatRefusesTheRefreshToken_WithdrawsTheAccessTokenAndSealsTheProfileWhole()
    {
        // Arrange: the store takes the access token and will not take the refresh token, which is the longer of the
        // two and the one a size limit refuses.
        this.secretStore.RefuseWriting(
            ProfileSecret.RefreshToken("production", "https://mail.example.test:8443"),
            "the credential is larger than this store accepts");

        // Act
        var (_, placement) = this.store.Save("production", Production, "access-token", "workstation", Session());

        // Assert
        var stored = this.store.Read().Profiles["production"];
        var profile = this.store.Resolve("production");

        Assert.Empty(this.secretStore.Entries);
        Assert.Null(placement.Store);
        Assert.Equal("the credential is larger than this store accepts", placement.Refusal);
        Assert.NotNull(stored.Token);
        Assert.NotNull(stored.Session?.RefreshToken);
        Assert.Equal("access-token", profile.Token);
        Assert.Equal("refresh-token", profile.Session?.RefreshToken);
    }

    /// <summary>The withdrawal is of the profile rather than of this invocation's own write, because a profile sealed whole is one nothing reads either key of again.</summary>
    /// <remarks>
    /// The refresh entry an earlier sign-in left is the one at stake: this sign-in never wrote it, so a withdrawal
    /// scoped to what it did write would leave the longer-lived of the two secrets in the keyring, under a profile
    /// whose file entry says the store holds nothing.
    /// </remarks>
    [Fact]
    public void Save_AStoreThatRefusesTheRefreshTokenOverAProfileItHeld_WithdrawsBothOfThatProfilesEntries()
    {
        // Arrange: a sign-in the store took whole, and a store that will not take the refresh token on the next one.
        this.store.Save("production", Production, "access-token", "workstation", Session());

        this.secretStore.RefuseWriting(
            ProfileSecret.RefreshToken("production", "https://mail.example.test:8443"),
            "the credential is larger than this store accepts");

        // Act
        var (_, placement) = this.store.Save("production", Production, "a-second-token", "workstation", Session());

        // Assert
        Assert.Empty(this.secretStore.Entries);
        Assert.Null(placement.Store);
        Assert.Null(placement.Uncleared);
        Assert.Equal("a-second-token", this.store.Resolve("production").Token);
    }

    /// <summary>The withdrawal is the one step that can itself fail, and what it leaves behind is a live credential nothing later goes looking for.</summary>
    [Fact]
    public void Save_AStoreThatLocksBetweenTheTwoWrites_ReportsTheEntryItCouldNotWithdraw()
    {
        // Arrange: the collection locks after the access token is written, so the second write and the withdrawal of
        // the first are refused for the same reason.
        this.secretStore.RefuseWriting(
            ProfileSecret.RefreshToken("production", "https://mail.example.test:8443"),
            "the collection is locked");
        this.secretStore.RefuseClearing(
            ProfileSecret.BearerToken("production", "https://mail.example.test:8443"),
            "the collection is locked");

        // Act
        var (_, placement) = this.store.Save("production", Production, "access-token", "workstation", Session());

        // Assert
        Assert.Equal("the collection is locked", placement.Refusal);
        Assert.Equal("the collection is locked", placement.Uncleared);
        Assert.Single(this.secretStore.Entries);
    }

    /// <summary>The placement is made for a file, so a file that was not written leaves a credential in the keyring under a name nothing carries.</summary>
    /// <remarks>
    /// <c>Resolve</c> asks only for profiles the file holds and <c>logout</c> can name only those, so an entry filed
    /// under a name the failed sign-in never gave the file is one nothing would ever read and nothing would ever
    /// remove.
    /// </remarks>
    [Fact]
    public void Save_AFileWriteThatCannotComplete_TakesTheCredentialBackOutOfTheStore()
    {
        // Arrange: a directory where the pending file has to be created, so the placement is made and the file is not.
        Directory.CreateDirectory(this.StorePath + ".pending");

        // Act
        Assert.Throws<CliFailure>(
            () => this.store.Save("production", Production, "not-a-real-token", "workstation"));

        // Assert
        Assert.Empty(this.secretStore.Entries);
    }

    /// <summary>A withdrawal the store refuses is the operator's to finish, and the failing sign-in is the only thing left that can tell them.</summary>
    [Fact]
    public void Save_AFileWriteThatCannotCompleteAndAStoreThatWillNotLetGo_SaysTheCredentialReachedTheStore()
    {
        // Arrange
        Directory.CreateDirectory(this.StorePath + ".pending");

        this.secretStore.RefuseClearing(
            ProfileSecret.BearerToken("production", "https://mail.example.test:8443"),
            "the collection is locked");

        // Act
        var failure = Assert.Throws<CliFailure>(
            () => this.store.Save("production", Production, "not-a-real-token", "workstation"));

        // Assert
        Assert.Contains("could not be taken back out", failure.Message, StringComparison.Ordinal);
        Assert.Contains("the collection is locked", failure.Message, StringComparison.Ordinal);
        Assert.Single(this.secretStore.Entries);
    }

    /// <summary>Clearing the address a profile is moving off is a removal, so it waits for the record that stops pointing there to be written.</summary>
    /// <remarks>
    /// Run before the write, a sign-in that then failed would have deleted the old address's entries and had the new
    /// address's withdrawn behind it, leaving a file that still names the old address and a store holding nothing under
    /// either key. The credential that was working before the sign-in would be gone because of a sign-in that never
    /// took effect.
    /// </remarks>
    [Fact]
    public void Save_AFileWriteThatCannotCompleteOnAMovedDeployment_LeavesTheOldAddressWhereTheRecordStillPointsAtIt()
    {
        // Arrange: a profile the store holds at one address, and a sign-in at the address it moved to whose file
        // write cannot complete.
        this.store.Save("production", Production, "first-token", "workstation");

        Directory.CreateDirectory(this.StorePath + ".pending");

        // Act
        Assert.Throws<CliFailure>(() => this.store.Save("production", Moved, "moved-token", "workstation"));

        // Assert
        Assert.Equal(
            FakeOperatorSecretStore.KeyOf(ProfileSecret.BearerToken("production", "https://mail.example.test:8443")),
            Assert.Single(this.secretStore.Entries).Key);

        Assert.Equal("first-token", this.store.Resolve("production").Token);
    }

    /// <summary>Where the file already describes a profile the store holds, the entries under that key are the surviving record's credential rather than an orphan.</summary>
    /// <remarks>
    /// The withdrawal exists for a name the file never gained. Running it here would empty the store for a profile the
    /// file still carries and still says is held, which is the one arrangement <c>Resolve</c> cannot open at all — so
    /// the failed sign-in would break the profile that survived it.
    /// </remarks>
    [Fact]
    public void Save_AFileWriteThatCannotCompleteOverAProfileTheStoreHeld_LeavesTheSurvivingProfileOpenable()
    {
        // Arrange
        this.store.Save("production", Production, "first-token", "workstation");

        Directory.CreateDirectory(this.StorePath + ".pending");

        // Act
        Assert.Throws<CliFailure>(
            () => this.store.Save("production", Production, "second-token", "workstation"));

        // Assert
        Assert.Equal("second-token", this.store.Resolve("production").Token);
    }

    /// <summary>A deployment that moves port keeps its profile, so the entries it left at the old address are reachable from nowhere unless this sign-in clears them.</summary>
    /// <remarks>
    /// The name a sign-in defaults to is the address's host without its port, so the same profile is what both
    /// invocations write. Everything afterwards reads the profile's current address — the next placement, and the
    /// <c>logout</c> that clears both halves — so an entry under the address it left is an administrative credential
    /// nothing will ever look at again.
    /// </remarks>
    [Fact]
    public void Save_AProfileWhoseDeploymentMoved_ClearsWhatItLeftAtTheOldAddress()
    {
        // Arrange
        this.store.Save("production", Production, "access-token", "workstation", Session());

        // Act
        this.store.Save("production", Moved, "moved-token", "workstation");

        // Assert
        Assert.Equal(
            "moved-token",
            this.secretStore.Entries[
                FakeOperatorSecretStore.KeyOf(ProfileSecret.BearerToken("production", "https://mail.example.test:9443"))]);
        Assert.Single(this.secretStore.Entries);
        Assert.Equal("moved-token", this.store.Resolve("production").Token);
    }

    /// <summary>Entries the move could not clear are the operator's to remove, and nothing later in the profile's life mentions them.</summary>
    [Fact]
    public void Save_AProfileWhoseDeploymentMovedAndAStoreThatRefuses_ReportsWhatIsLeftAtTheOldAddress()
    {
        // Arrange
        this.store.Save("production", Production, "not-a-real-token", "workstation");
        this.secretStore.RefuseClearing(
            ProfileSecret.BearerToken("production", "https://mail.example.test:8443"),
            "the collection is locked");

        // Act
        var (_, placement) = this.store.Save("production", Moved, "moved-token", "workstation");

        // Assert
        Assert.Equal("the platform's secret store", placement.Store);
        Assert.Equal("the collection is locked", placement.Uncleared);
    }

    /// <summary>The profile gains a sealed token from here, which is what stops every later command from looking at the store for it.</summary>
    [Fact]
    public void Save_AStoreThatHasLockedSinceTheLastSignIn_ReportsTheEntriesItStillHolds()
    {
        // Arrange
        this.store.Save("production", Production, "not-a-real-token", "workstation");
        this.secretStore.Refusal = "the collection is locked";

        // Act
        var (_, placement) = this.store.Save("production", Production, "a-second-token", "workstation");

        // Assert
        Assert.Null(placement.Store);
        Assert.Equal("the collection is locked", placement.Refusal);
        Assert.Equal("the collection is locked", placement.Uncleared);
    }

    /// <summary>Signing a key-pair profile in over one the store held has to take the credential out, or it outlives the profile that named it.</summary>
    [Fact]
    public void Save_AKeyPairProfileOverOneThePlatformHeld_ClearsWhatTheStoreWasHolding()
    {
        // Arrange
        this.store.Save("production", Production, "access-token", "workstation", Session());

        // Act
        var (_, placement) = this.store.Save(
            "production",
            Production,
            string.Empty,
            "workstation",
            keyPair: new StoredKeyPair("/keys/mfctl.pem"));

        // Assert
        Assert.Empty(this.secretStore.Entries);
        Assert.Null(placement.Uncleared);
    }

    /// <summary>A machine that never had a store has nothing to leave behind, so the warning about one would be false on every headless sign-in.</summary>
    [Fact]
    public void Save_AKeyPairProfileOnAMachineWithNoStore_ReportsNothingLeftBehind()
    {
        // Arrange
        this.secretStore.Refusal = "libsecret is not installed here";

        // Act
        var (_, placement) = this.store.Save(
            "production",
            Production,
            string.Empty,
            "workstation",
            keyPair: new StoredKeyPair("/keys/mfctl.pem"));

        // Assert
        Assert.Null(placement.Uncleared);
        Assert.Null(placement.Describe());
    }

    /// <summary>A renewal replaces one value wherever that profile keeps it, and moves nothing between the two places.</summary>
    [Fact]
    public void RenewAccessToken_AProfileThePlatformHolds_ReplacesTheEntryAndLeavesTheFileWithoutASecret()
    {
        // Arrange
        this.store.Save("production", Production, "access-token", "workstation", Session());

        // Act
        var renewedUntil = DateTimeOffset.UnixEpoch.AddDays(2);

        this.store.RenewAccessToken("production", "renewed-token", renewedUntil);

        // Assert
        var profile = this.store.Resolve("production");

        Assert.Equal("renewed-token", profile.Token);
        Assert.Equal("refresh-token", profile.Session?.RefreshToken);
        Assert.Null(this.store.Read().Profiles["production"].Token);

        // The expiry the renewal was given, because a session left reading as spent renews again on every command:
        // the file is the only place it is recorded, and the store holds the token rather than when it dies.
        Assert.Equal(renewedUntil, this.store.Read().Profiles["production"].Session?.AccessTokenExpiresAt);
    }

    /// <summary>An older command sealed an empty token for a key-pair profile, and that value is the only thing keeping a key file alive.</summary>
    /// <remarks>
    /// Nothing this version writes produces the shape — <c>Save</c> writes no token for such a profile — so the branch
    /// that moves it can only be reached from a file an earlier <c>mfctl</c> wrote, which is what this arranges. Left
    /// uncovered, an operator whose only profile is key-pair would keep <c>credentials.key</c> on disk for good and go
    /// on carrying a <c>token</c> member for a profile that holds nothing, and no test would say so.
    /// </remarks>
    [Fact]
    public void Resolve_AKeyPairProfileSealedByAnOlderCommand_DropsItsTokenAndTheKeyFileWithIt()
    {
        // Arrange: the sealed empty token an older command wrote, produced by the same protector so the key file it
        // needs exists exactly as it would have on that machine.
        Directory.CreateDirectory(this.storeDirectory);

        var sealedEmptyToken = new TokenProtector(this.KeyPath)
            .Protect(string.Empty, "https://mail.example.test:8443");

        var written = new StoredCredentials(
            "production",
            new Dictionary<string, StoredCredential>(StringComparer.OrdinalIgnoreCase)
            {
                ["production"] = new(
                    "https://mail.example.test:8443",
                    sealedEmptyToken,
                    "workstation",
                    Session: null,
                    new StoredKeyPair("/keys/mfctl.pem")),
            });

        File.WriteAllText(
            this.StorePath,
            JsonSerializer.Serialize(written, CliJsonContext.Default.StoredCredentials));

        Assert.True(File.Exists(this.KeyPath));

        // Act
        var profile = this.store.Resolve("production");

        // Assert
        Assert.Equal(string.Empty, profile.Token);
        Assert.Equal("/keys/mfctl.pem", profile.KeyPair?.PrivateKeyPath);
        Assert.Null(this.store.Read().Profiles["production"].Token);
        Assert.Empty(this.secretStore.Entries);
        Assert.False(File.Exists(this.KeyPath));
    }

    /// <summary>A move undone half-way leaves a credential the file no longer points at, and the command the move happened under is the only one that will ever know.</summary>
    /// <remarks>
    /// The profile stays sealed, so it opens perfectly well and every later <c>logout</c> passes over it at the guard
    /// for a sealed token — which is what makes saying so here the last chance rather than a courtesy.
    /// </remarks>
    [Fact]
    public void Resolve_AMoveWhoseWithdrawalWasRefused_CarriesWhatIsLeftBehindOutOfTheMove()
    {
        // Arrange: a profile sealed on a machine that had no store, and a store that takes the access token, refuses
        // the refresh token, and will not give the first one back.
        this.CreateStore(secretStore: null)
            .Save("production", Production, "access-token", "workstation", Session());

        this.secretStore.RefuseWriting(
            ProfileSecret.RefreshToken("production", "https://mail.example.test:8443"),
            "the collection is locked");
        this.secretStore.RefuseClearing(
            ProfileSecret.BearerToken("production", "https://mail.example.test:8443"),
            "the collection is locked");

        // Act
        var profile = this.store.Resolve("production");

        // Assert
        Assert.Equal("access-token", profile.Token);
        Assert.Equal("the collection is locked", profile.Uncleared);
        Assert.NotNull(this.store.Read().Profiles["production"].Token);
    }

    /// <summary>The file keys its profiles without regard to case, so a second spelling replaces the value and keeps the key — and the entries have to follow the key.</summary>
    /// <remarks>
    /// Keyed by what was typed instead, the sign-in would file the credential under a spelling no read path ever asks
    /// for: <c>Resolve</c> goes through the stored one. The profile would fail every later command, and the entries the
    /// previous sign-in left under the stored spelling would stay in the keyring with nothing reaching them.
    /// </remarks>
    [Fact]
    public void Save_AProfileSignedInUnderADifferentSpelling_KeepsTheEntriesUnderTheStoredName()
    {
        // Arrange
        this.store.Save("Production", Production, "first-token", "workstation");

        // Act
        this.store.Save("production", Production, "second-token", "workstation");

        // Assert
        Assert.Single(this.secretStore.Entries);
        Assert.Equal(
            "second-token",
            this.secretStore.Entries[
                FakeOperatorSecretStore.KeyOf(ProfileSecret.BearerToken("Production", "https://mail.example.test:8443"))]);
        Assert.Equal("second-token", this.store.Resolve("production").Token);
        Assert.Equal("second-token", this.store.Resolve("Production").Token);
    }

    /// <summary>A renewal whose store has gone away changes nothing at all, rather than moving half a session into the file.</summary>
    /// <remarks>
    /// Advancing the file's expiry while the store still held the old access token would be the worst of the three
    /// outcomes: the next command would read the session as fresh, skip the renewal, present the stale token, and be
    /// refused by the deployment with nothing in the message naming the renewal that silently failed.
    /// </remarks>
    [Fact]
    public void RenewAccessToken_AStoreThatHasGoneAway_LeavesTheProfileExactlyAsItWas()
    {
        // Arrange
        this.store.Save("production", Production, "access-token", "workstation", Session());

        var before = this.store.Read().Profiles["production"];

        this.secretStore.Refusal = "the collection is locked";

        // Act
        this.store.RenewAccessToken("production", "renewed-token", DateTimeOffset.UnixEpoch.AddDays(2));

        // Assert
        this.secretStore.Refusal = null;

        var after = this.store.Read().Profiles["production"];

        Assert.Equal(before.Session?.AccessTokenExpiresAt, after.Session?.AccessTokenExpiresAt);
        Assert.Null(after.Token);
        Assert.Equal("access-token", this.store.Resolve("production").Token);
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
