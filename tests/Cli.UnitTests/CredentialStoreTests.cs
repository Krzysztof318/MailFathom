// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.InteropServices;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the file the command keeps a credential in, which is the only thing it ever writes.</summary>
public sealed class CredentialStoreTests : IDisposable
{
    private static readonly Uri Production = new("https://mail.example.test:8443");
    private static readonly Uri Staging = new("https://staging.example.test:8443");

    private readonly string storeDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-cli-tests-{Guid.NewGuid():N}");

    private readonly CredentialStore store;

    public CredentialStoreTests() =>
        this.store = new CredentialStore(Path.Combine(this.storeDirectory, "credentials.json"));

    [Fact]
    public void Find_NothingStored_ReportsNoCredentialRatherThanFailing()
    {
        // Act, Assert: a first run has no file, which is not an error state.
        Assert.Null(this.store.Find(Production));
    }

    [Fact]
    public void Save_ACredential_IsReadBackForTheSameEndpoint()
    {
        // Arrange
        var credential = new StoredCredential("not-a-real-token", "workstation");

        // Act
        this.store.Save(Production, credential);

        // Assert
        Assert.Equal(credential, this.store.Find(Production));
    }

    /// <summary>
    /// A workstation administering more than one deployment holds a credential for each. Keying by endpoint is what
    /// keeps signing in to staging from silently replacing the production credential.
    /// </summary>
    [Fact]
    public void Save_TwoEndpoints_KeepsOneCredentialOutOfTheOthers()
    {
        // Arrange, Act
        this.store.Save(Production, new StoredCredential("production-token", "production"));
        this.store.Save(Staging, new StoredCredential("staging-token", "staging"));

        // Assert
        Assert.Equal("production-token", this.store.Find(Production)!.Token);
        Assert.Equal("staging-token", this.store.Find(Staging)!.Token);
    }

    [Theory]
    [InlineData("https://mail.example.test:8443/")]
    [InlineData("https://MAIL.example.test:8443")]
    public void Find_TheSameEndpointSpelledDifferently_FindsTheStoredCredential(string spelling)
    {
        // Arrange
        this.store.Save(Production, new StoredCredential("not-a-real-token", "workstation"));

        // Act, Assert: a trailing slash and a differently cased host name are one deployment, not three.
        Assert.NotNull(this.store.Find(new Uri(spelling)));
    }

    [Fact]
    public void Remove_AStoredCredential_ForgetsItAndReportsThatItDidSo()
    {
        // Arrange
        this.store.Save(Production, new StoredCredential("not-a-real-token", "workstation"));

        // Act
        var removed = this.store.Remove(Production);

        // Assert
        Assert.True(removed);
        Assert.Null(this.store.Find(Production));
    }

    [Fact]
    public void Remove_NothingStored_ReportsThatThereWasNothingToForget()
    {
        // Act, Assert
        Assert.False(this.store.Remove(Production));
    }

    /// <summary>
    /// The file holds a bearer credential, so anything else on the machine being able to read it defeats the point of
    /// having asked for one. The mode is set as the file is created rather than afterwards, because a file created
    /// readable and tightened later is readable for the moment in between.
    /// </summary>
    [Fact]
    public void Save_OnAPlatformWithFileModes_LeavesTheStoreReadableByItsOwnerAlone()
    {
        // Arrange
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var storePath = Path.Combine(this.storeDirectory, "credentials.json");

        // Act
        this.store.Save(Production, new StoredCredential("not-a-real-token", "workstation"));

        // Assert
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(storePath));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(this.storeDirectory));
    }

    [Fact]
    public void Find_AStoreThatIsNotACredentialFile_SaysWhatToDoAboutIt()
    {
        // Arrange
        Directory.CreateDirectory(this.storeDirectory);
        File.WriteAllText(Path.Combine(this.storeDirectory, "credentials.json"), "this is not json");

        // Act
        var failure = Assert.Throws<CliFailure>(() => this.store.Find(Production));

        // Assert: the message names the file, because removing it is the fix.
        Assert.Contains("credentials.json", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The token is what the file exists to hold, so no diagnostic that formats the record may print it.</summary>
    [Fact]
    public void ToString_AStoredCredential_DoesNotCarryTheToken()
    {
        // Arrange
        var credential = new StoredCredential("a-real-looking-token", "workstation");

        // Act, Assert
        Assert.DoesNotContain("a-real-looking-token", credential.ToString(), StringComparison.Ordinal);
        Assert.Contains("workstation", credential.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }
}
