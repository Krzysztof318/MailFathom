// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.DataEncryption;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.DataEncryption;

/// <summary>Covers what a binding refuses to be, and that two different bindings never compose the same associated data.</summary>
public sealed class DataEncryptionBindingTests
{
    [Fact]
    public void Create_TheUnspecifiedPurpose_IsRefused() =>
        Assert.Throws<ArgumentException>(() => DataEncryptionBinding.Create(default, "primary"));

    [Fact]
    public void Create_AnEmptySubject_IsRefused() =>
        Assert.Throws<ArgumentException>(
            () => DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, string.Empty));

    [Fact]
    public void Create_ASubjectCarryingTheSeparator_IsRefused()
    {
        // Arrange — a subject able to carry the separator could compose the associated data of a different binding,
        // which is the one way the authentication could be talked out of its guarantee.
        var subject = "primary\u001Fmailbox-refresh-token";

        // Assert
        Assert.Throws<ArgumentException>(
            () => DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, subject));
    }

    [Fact]
    public void ComposeAssociatedData_TheSameBindingAndKey_IsStable()
    {
        // Arrange — a value sealed by one build has to open in the next, so the composition is asserted rather than
        // assumed to stay put.
        var binding = DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "primary");

        // Assert
        Assert.Equal(binding.ComposeAssociatedData("2026-08"), binding.ComposeAssociatedData("2026-08"));
    }

    [Fact]
    public void ComposeAssociatedData_ADifferentSubject_ComposesSomethingElse()
    {
        // Arrange
        var primary = DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "primary");
        var secondary = DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "secondary");

        // Assert
        Assert.NotEqual(primary.ComposeAssociatedData("2026-08"), secondary.ComposeAssociatedData("2026-08"));
    }

    [Fact]
    public void ComposeAssociatedData_ADifferentKey_ComposesSomethingElse()
    {
        // Arrange
        var binding = DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "primary");

        // Assert
        Assert.NotEqual(binding.ComposeAssociatedData("2026-08"), binding.ComposeAssociatedData("2026-02"));
    }

    [Fact]
    public void ToString_ABinding_NamesThePurposeAndTheSubjectAndNothingElse()
    {
        // Arrange — both parts are MailFathom's own configured names, so a binding is safe to report in a diagnostic.
        var binding = DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "primary");

        // Assert
        Assert.Equal("mailbox-refresh-token/primary", binding.ToString());
    }
}
