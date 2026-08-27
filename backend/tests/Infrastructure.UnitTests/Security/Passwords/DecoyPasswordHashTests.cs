// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Infrastructure.Security.Passwords;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.Passwords;

/// <summary>Covers that the record an unresolved username is compared against costs what a real one costs and matches nothing.</summary>
public sealed class DecoyPasswordHashTests
{
    /// <summary>A record written by anything but the deployment's own hasher would be refused on the format rather than on the comparison, which is the timing difference it exists to remove.</summary>
    [Fact]
    public void Value_ADeployedHasher_IsARecordThatHasherWroteItself()
    {
        // Arrange
        var passwordHasher = new CountingPasswordHasher();

        // Act
        var decoy = new DecoyPasswordHash(passwordHasher);

        // Assert
        Assert.Equal(1, passwordHasher.HashCount);
        Assert.Equal(CountingPasswordHasher.WrittenRecord, decoy.Value);
    }

    /// <summary>The password behind it is random and never leaves the constructor, so nothing can present the credential it would accept.</summary>
    [Fact]
    public void Value_ARealHasher_MatchesNoPasswordAnybodyCouldPresent()
    {
        // Arrange
        var passwordHasher = new Pbkdf2PasswordHasher();
        var decoy = new DecoyPasswordHash(passwordHasher);

        // Act
        var verification = passwordHasher.Verify(decoy.Value, "correcthorsebattery");

        // Assert
        Assert.Equal(PasswordVerification.Failed, verification);
    }

    /// <summary>Two processes hold different decoys, which is what keeps the record from being a constant an attacker could recognize in a dump.</summary>
    [Fact]
    public void Value_TwoDerivations_AreNotTheSameRecord()
    {
        // Arrange
        var passwordHasher = new Pbkdf2PasswordHasher();

        // Act
        var first = new DecoyPasswordHash(passwordHasher);
        var second = new DecoyPasswordHash(passwordHasher);

        // Assert
        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact]
    public void Constructor_NoHasher_IsRefused() =>
        Assert.Throws<ArgumentNullException>(static () => new DecoyPasswordHash(null!));

    /// <summary>Counts what the decoy asked of a hasher, without spending a real derivation to find out.</summary>
    /// <remarks>Hand-written rather than substituted, because the members take the password as a <see cref="ReadOnlySpan{T}" /> and a dynamic proxy cannot carry a by-ref-like argument through its invocation.</remarks>
    private sealed class CountingPasswordHasher : IPasswordHasher
    {
        internal const string WrittenRecord = "$mf1$derived$";

        internal int HashCount { get; private set; }

        public string Hash(ReadOnlySpan<char> password)
        {
            this.HashCount++;

            return WrittenRecord;
        }

        public PasswordVerification Verify(string storedHash, ReadOnlySpan<char> password) =>
            PasswordVerification.Failed;
    }
}
