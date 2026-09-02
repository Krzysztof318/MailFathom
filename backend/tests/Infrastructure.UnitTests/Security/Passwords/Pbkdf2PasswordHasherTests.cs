// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.Access.Credentials;
using MailFathom.Infrastructure.Security.Passwords;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.Passwords;

/// <summary>Covers what a stored password verifies against, and when a record is reported as behind the current policy.</summary>
public sealed class Pbkdf2PasswordHasherTests
{
    private const string Password = "correcthorsebatterystaple";

    [Fact]
    public void Verify_ThePasswordThatWasHashed_Succeeds()
    {
        // Arrange
        var hasher = new Pbkdf2PasswordHasher();
        var stored = hasher.Hash(Password);

        // Act
        var verification = hasher.Verify(stored, Password);

        // Assert
        Assert.Equal(PasswordVerification.Succeeded, verification);
    }

    [Theory]
    [InlineData("correcthorsebatterystapl")]
    [InlineData("correcthorsebatterystaples")]
    [InlineData("Correcthorsebatterystaple")]
    [InlineData("")]
    public void Verify_AnythingButThatPassword_Fails(string presented)
    {
        // Arrange
        var hasher = new Pbkdf2PasswordHasher();
        var stored = hasher.Hash(Password);

        // Act
        var verification = hasher.Verify(stored, presented);

        // Assert
        Assert.Equal(PasswordVerification.Failed, verification);
    }

    /// <summary>A fresh salt per call is what stops a database dump answering which owners chose the same password.</summary>
    [Fact]
    public void Hash_OnePasswordTwice_ProducesTwoRecordsAndBothVerify()
    {
        // Arrange
        var hasher = new Pbkdf2PasswordHasher();

        // Act
        var first = hasher.Hash(Password);
        var second = hasher.Hash(Password);

        // Assert
        Assert.NotEqual(first, second);
        Assert.Equal(PasswordVerification.Succeeded, hasher.Verify(first, Password));
        Assert.Equal(PasswordVerification.Succeeded, hasher.Verify(second, Password));
    }

    [Fact]
    public void Hash_APassword_IsStoredAsNeitherThePlaintextNorAnythingCarryingIt()
    {
        // Arrange
        var hasher = new Pbkdf2PasswordHasher();

        // Act
        var stored = hasher.Hash(Password);

        // Assert
        Assert.DoesNotContain(Password, stored, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith($"${PasswordHashRecord.FormatVersion}${PasswordHashRecord.AlgorithmName}$", stored, StringComparison.Ordinal);
    }

    /// <summary>Raising the iteration count is what makes an existing record report itself as behind while the plaintext is still in hand.</summary>
    [Fact]
    public void Verify_ARecordWrittenUnderFewerIterations_SucceedsAndAsksToBeRehashed()
    {
        // Arrange
        var hasher = new Pbkdf2PasswordHasher();
        var behind = RecordUnder(Pbkdf2PasswordHasher.CurrentIterations / 2);

        // Act
        var verification = hasher.Verify(behind, Password);

        // Assert
        Assert.Equal(PasswordVerification.SucceededAndShouldBeRehashed, verification);
    }

    /// <summary>A deployment rolled back to an earlier release must not quietly weaken a record a later one wrote.</summary>
    [Fact]
    public void Verify_ARecordWrittenUnderMoreIterations_SucceedsWithoutAskingToBeRehashed()
    {
        // Arrange
        var hasher = new Pbkdf2PasswordHasher();
        var ahead = RecordUnder(Pbkdf2PasswordHasher.CurrentIterations + 1_000);

        // Act
        var verification = hasher.Verify(ahead, Password);

        // Assert
        Assert.Equal(PasswordVerification.Succeeded, verification);
    }

    /// <summary>An unreadable record is refused exactly as a wrong password is, so nothing tells the two apart.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("$mf2$pbkdf2-sha512$i=210000$c2FsdA==$a2V5")]
    [InlineData("$mf1$argon2id$i=210000$c2FsdA==$a2V5")]
    [InlineData("$mf1$pbkdf2-sha512$210000$c2FsdA==$a2V5")]
    [InlineData("$mf1$pbkdf2-sha512$i=0$c2FsdA==$a2V5")]
    [InlineData("$mf1$pbkdf2-sha512$i=-1$c2FsdA==$a2V5")]
    [InlineData("$mf1$pbkdf2-sha512$i=210000$not base64$a2V5")]
    [InlineData("$mf1$pbkdf2-sha512$i=210000$c2FsdA==")]
    public void Verify_AStoredValueThisReleaseCannotRead_FailsRatherThanRaising(string stored)
    {
        // Arrange
        var hasher = new Pbkdf2PasswordHasher();

        // Act
        var verification = hasher.Verify(stored, Password);

        // Assert
        Assert.Equal(PasswordVerification.Failed, verification);
    }

    [Fact]
    public void Verify_NoStoredValueAtAll_ThrowsBecauseThatIsACallerDefectRatherThanARefusedCredential()
    {
        // Arrange
        var hasher = new Pbkdf2PasswordHasher();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => hasher.Verify(null!, Password));
    }

    /// <summary>The decoy exists to cost what the cheapest record a deployment may still hold costs, so it is written at the oldest accepted count rather than at today's.</summary>
    [Fact]
    public void HashDecoy_ADeployedHasher_WritesTheRecordAtTheOldestAcceptedIterationCount()
    {
        // Arrange
        var hasher = new Pbkdf2PasswordHasher();

        // Act
        var decoy = hasher.HashDecoy();

        // Assert
        Assert.True(PasswordHashRecord.TryParse(decoy, out var record));
        Assert.Equal(Pbkdf2PasswordHasher.OldestAcceptedIterations, record.Iterations);
    }

    /// <summary>Its password is random and never leaves the call, so nothing can present the credential it would accept.</summary>
    [Fact]
    public void HashDecoy_TwoRecords_MatchNoPasswordAndDifferFromEachOther()
    {
        // Arrange
        var hasher = new Pbkdf2PasswordHasher();

        // Act
        var first = hasher.HashDecoy();
        var second = hasher.HashDecoy();

        // Assert
        Assert.NotEqual(first, second);
        Assert.Equal(PasswordVerification.Failed, hasher.Verify(first, Password));
    }

    /// <summary>Writes the same password under other work parameters, which is what a record from another release looks like.</summary>
    private static string RecordUnder(int iterations)
    {
        var salt = new byte[Pbkdf2PasswordHasher.SaltLength];
        var derivedKey = new byte[Pbkdf2PasswordHasher.DerivedKeyLength];

        Rfc2898DeriveBytes.Pbkdf2(Password, salt, derivedKey, iterations, HashAlgorithmName.SHA512);

        return new PasswordHashRecord(iterations, salt, derivedKey).ToString();
    }
}
