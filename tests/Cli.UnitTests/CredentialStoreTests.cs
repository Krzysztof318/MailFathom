// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.InteropServices;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the file the command keeps its profiles in, which is the only thing it ever writes.</summary>
public sealed class CredentialStoreTests : IDisposable
{
    private static readonly Uri Production = new("https://mail.example.test:8443");
    private static readonly Uri Staging = new("https://staging.example.test:8443");

    private readonly string storeDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-cli-tests-{Guid.NewGuid():N}");

    private readonly CredentialStore store;

    public CredentialStoreTests() => this.store = new CredentialStore(
        Path.Combine(this.storeDirectory, "credentials.json"),
        new TokenProtector(Path.Combine(this.storeDirectory, "credentials.key")));

    [Fact]
    public void Read_NothingStored_ReportsAnEmptyStoreRatherThanFailing()
    {
        // Act
        var stored = this.store.Read();

        // Assert: a first run has no file, which is not an error state.
        Assert.Empty(stored.Profiles);
        Assert.Null(stored.Default);
    }

    /// <summary>Every command that reaches a deployment starts here, so the message has to name the way out.</summary>
    [Fact]
    public void Resolve_NothingStored_SaysHowToSignIn()
    {
        // Act
        var failure = Assert.Throws<CliFailure>(() => this.store.Resolve(requestedDeployment: null));

        // Assert
        Assert.Contains("Not signed in", failure.Message, StringComparison.Ordinal);
        Assert.Contains("mfctl login --endpoint", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_AProfile_IsResolvedByNameWithItsTokenReadable()
    {
        // Arrange, Act
        this.store.Save("production", Production, "not-a-real-token", "workstation");

        // Assert
        var profile = this.store.Resolve("production");

        Assert.Equal("production", profile.Name);
        Assert.Equal(Production.GetLeftPart(UriPartial.Authority), profile.Endpoint.GetLeftPart(UriPartial.Authority));
        Assert.Equal("not-a-real-token", profile.Token);
        Assert.Equal("workstation", profile.Credential);
    }

    /// <summary>Signing in is a choice of deployment as well as of credential, so the profile just created is the one in use.</summary>
    [Fact]
    public void Save_AProfile_MakesItTheDefault()
    {
        // Arrange, Act
        this.store.Save("production", Production, "production-token", "workstation");
        this.store.Save("staging", Staging, "staging-token", "workstation");

        // Assert
        Assert.Equal("staging", this.store.Resolve(requestedDeployment: null).Name);
    }

    /// <summary>
    /// A workstation administering more than one deployment holds a profile for each. Keying by name is what keeps
    /// signing in to staging from replacing production, and what lets a deployment change address without becoming a
    /// second entry.
    /// </summary>
    [Fact]
    public void Save_TwoProfiles_KeepsOneOutOfTheOther()
    {
        // Arrange, Act
        this.store.Save("production", Production, "production-token", "workstation");
        this.store.Save("staging", Staging, "staging-token", "workstation");

        // Assert
        Assert.Equal("production-token", this.store.Resolve("production").Token);
        Assert.Equal("staging-token", this.store.Resolve("staging").Token);
    }

    [Fact]
    public void Save_TheSameNameTwice_ReplacesTheCredentialRatherThanAddingAProfile()
    {
        // Arrange
        this.store.Save("production", Production, "first-token", "workstation");

        // Act
        this.store.Save("production", Production, "second-token", "workstation");

        // Assert
        Assert.Equal("second-token", this.store.Resolve("production").Token);
        Assert.Single(this.store.Read().Profiles);
    }

    [Fact]
    public void Resolve_AProfileNameInAnotherCase_FindsIt()
    {
        // Arrange
        this.store.Save("production", Production, "not-a-real-token", "workstation");

        // Act, Assert: a name is what an operator types, and typing it is not a spelling test.
        Assert.Equal("production", this.store.Resolve("PRODUCTION").Name);
    }

    /// <summary>The override an operator reaches for when one command has to go somewhere other than the profile in use.</summary>
    [Theory]
    [InlineData("https://mail.example.test:8443")]
    [InlineData("https://mail.example.test:8443/")]
    [InlineData("https://MAIL.example.test:8443")]
    public void Resolve_AnAddressRatherThanAName_FindsTheProfileServingIt(string spelling)
    {
        // Arrange
        this.store.Save("production", Production, "production-token", "workstation");
        this.store.Save("staging", Staging, "staging-token", "workstation");

        // Act
        var profile = this.store.Resolve(spelling);

        // Assert: staging is the default, so finding production proves the address is what selected it.
        Assert.Equal("production", profile.Name);
    }

    [Fact]
    public void Resolve_AnAddressNoProfileServes_SaysHowToSignInToIt()
    {
        // Arrange
        this.store.Save("production", Production, "production-token", "workstation");

        // Act
        var failure = Assert.Throws<CliFailure>(() => this.store.Resolve("https://other.example.test:8443"));

        // Assert
        Assert.Contains("Not signed in to https://other.example.test:8443", failure.Message, StringComparison.Ordinal);
        Assert.Contains("production", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_AnUnknownProfileName_ListsTheOnesThatExist()
    {
        // Arrange
        this.store.Save("production", Production, "production-token", "workstation");
        this.store.Save("staging", Staging, "staging-token", "workstation");

        // Act
        var failure = Assert.Throws<CliFailure>(() => this.store.Resolve("qa"));

        // Assert
        Assert.Contains("no profile named 'qa'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("production, staging", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SwitchTo_AProfile_MakesItTheOneLaterCommandsUse()
    {
        // Arrange
        this.store.Save("production", Production, "production-token", "workstation");
        this.store.Save("staging", Staging, "staging-token", "workstation");

        // Act
        this.store.SwitchTo("production");

        // Assert
        Assert.Equal("production", this.store.Resolve(requestedDeployment: null).Name);
    }

    /// <summary>The name a message prints is the one the profile was created with, not the casing just typed.</summary>
    [Fact]
    public void SwitchTo_ANameInAnotherCase_ReportsTheStoredSpelling()
    {
        // Arrange
        this.store.Save("Production", Production, "production-token", "workstation");

        // Act
        var (name, _) = this.store.SwitchTo("production");

        // Assert
        Assert.Equal("Production", name);
    }

    [Fact]
    public void SwitchTo_AnUnknownProfile_SaysWhichOnesExist()
    {
        // Arrange
        this.store.Save("production", Production, "production-token", "workstation");

        // Act
        var failure = Assert.Throws<CliFailure>(() => this.store.SwitchTo("qa"));

        // Assert
        Assert.Contains("production", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_AStoredProfile_ForgetsItAndReportsThatItDidSo()
    {
        // Arrange
        this.store.Save("production", Production, "not-a-real-token", "workstation");

        // Act
        var removed = this.store.Remove("production");

        // Assert
        Assert.True(removed);
        Assert.Empty(this.store.Read().Profiles);
    }

    /// <summary>
    /// Promoting a neighbour would send the next command to a deployment the operator never selected, which is worse
    /// than making them choose.
    /// </summary>
    [Fact]
    public void Remove_TheProfileInUse_LeavesNoDefaultRatherThanChoosingAnother()
    {
        // Arrange
        this.store.Save("production", Production, "production-token", "workstation");
        this.store.Save("staging", Staging, "staging-token", "workstation");

        // Act
        this.store.Remove("staging");

        // Assert
        var failure = Assert.Throws<CliFailure>(() => this.store.Resolve(requestedDeployment: null));

        Assert.Contains("No default profile is set", failure.Message, StringComparison.Ordinal);
        Assert.Contains("mfctl switch", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_AProfileThatIsNotInUse_LeavesTheDefaultAlone()
    {
        // Arrange
        this.store.Save("staging", Staging, "staging-token", "workstation");
        this.store.Save("production", Production, "production-token", "workstation");

        // Act
        this.store.Remove("staging");

        // Assert
        Assert.Equal("production", this.store.Resolve(requestedDeployment: null).Name);
    }

    [Fact]
    public void Remove_NothingStored_ReportsThatThereWasNothingToForget()
    {
        // Act, Assert
        Assert.False(this.store.Remove("production"));
    }

    /// <summary>Forgetting a profile is what an operator does about a token that no longer opens, so it must not need one.</summary>
    [Fact]
    public void Locate_ATokenThatCannotBeOpened_StillNamesTheProfile()
    {
        // Arrange
        this.store.Save("production", Production, "not-a-real-token", "workstation");
        File.Delete(Path.Combine(this.storeDirectory, "credentials.key"));

        // Act
        var (name, _) = this.store.Locate("production");

        // Assert
        Assert.Equal("production", name);
        Assert.Throws<CliFailure>(() => this.store.Resolve("production"));
    }

    /// <summary>The file is what an operator might copy into a support bundle, so the token must not be in it in the clear.</summary>
    [Fact]
    public void Save_AProfile_WritesNoReadableToken()
    {
        // Arrange, Act
        this.store.Save("production", Production, "a-real-looking-token", "workstation");

        // Assert
        var contents = File.ReadAllText(Path.Combine(this.storeDirectory, "credentials.json"));

        Assert.DoesNotContain("a-real-looking-token", contents, StringComparison.Ordinal);
        Assert.Contains("production", contents, StringComparison.Ordinal);
    }

    /// <summary>
    /// The file holds a bearer credential, so anything else on the machine being able to read it defeats the point of
    /// having asked for one. The mode is set as the file is created rather than afterwards, because a file created
    /// readable and tightened later is readable for the moment in between.
    /// </summary>
    [Fact]
    public void Save_OnAPlatformWithFileModes_LeavesTheStoreAndTheKeyReadableByTheirOwnerAlone()
    {
        // Arrange
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        // Act
        this.store.Save("production", Production, "not-a-real-token", "workstation");

        // Assert
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(Path.Combine(this.storeDirectory, "credentials.json")));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(Path.Combine(this.storeDirectory, "credentials.key")));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(this.storeDirectory));
    }

    [Fact]
    public void Read_AStoreThatIsNotACredentialFile_SaysWhatToDoAboutIt()
    {
        // Arrange
        Directory.CreateDirectory(this.storeDirectory);
        File.WriteAllText(Path.Combine(this.storeDirectory, "credentials.json"), "this is not json");

        // Act
        var failure = Assert.Throws<CliFailure>(this.store.Read);

        // Assert: the message names the file, because removing it is the fix.
        Assert.Contains("credentials.json", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The token is what the file exists to hold, so no diagnostic that formats the record may print it.</summary>
    [Fact]
    public void ToString_AStoredCredential_DoesNotCarryTheToken()
    {
        // Arrange
        var credential = new StoredCredential("https://mail.example.test:8443", "a-real-looking-token", "workstation");

        // Act, Assert
        Assert.DoesNotContain("a-real-looking-token", credential.ToString(), StringComparison.Ordinal);
        Assert.Contains("workstation", credential.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The opened token travels through this record, so it is the one that most needs redacting.</summary>
    [Fact]
    public void ToString_ASignedInProfile_DoesNotCarryTheToken()
    {
        // Arrange
        var profile = new SignedInProfile("production", Production, "a-real-looking-token", "workstation");

        // Act, Assert
        Assert.DoesNotContain("a-real-looking-token", profile.ToString(), StringComparison.Ordinal);
        Assert.Contains("production", profile.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }
}
