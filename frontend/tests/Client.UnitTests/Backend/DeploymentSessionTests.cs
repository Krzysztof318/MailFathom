// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;

namespace MailFathom.Client.UnitTests.Backend;

/// <summary>The document the deployment answers with, and what the client is allowed to read out of it.</summary>
public sealed class DeploymentSessionTests
{
    /// <summary>The grant is read by the published name, which is the only thing either end agreed on.</summary>
    [Fact]
    public void Grants_APermissionTheDeploymentNamed_IsRead()
    {
        // Arrange
        var session = SessionGranting("mailfathom.mail.read", "mailfathom.mail.ask");

        // Act, Assert
        Assert.True(session.Grants("mailfathom.mail.read"));
        Assert.True(session.Grants("mailfathom.mail.ask"));
    }

    /// <summary>A grant that does not name something is a grant that does not carry it, rather than one to guess about.</summary>
    [Fact]
    public void Grants_APermissionTheDeploymentDidNotName_IsNotRead()
    {
        // Arrange
        var session = SessionGranting("mailfathom.mail.read");

        // Act, Assert
        Assert.False(session.Grants("mailfathom.mail.send"));
    }

    /// <summary>
    /// These are protocol tokens rather than words, so the comparison is ordinal: a client whose grant depended on the
    /// language it was being read in would offer different things to two people on one credential.
    /// </summary>
    [Fact]
    public void Grants_ANameDifferingOnlyInCase_IsNotTheSameGrant()
    {
        // Arrange
        var session = SessionGranting("mailfathom.mail.read");

        // Act, Assert
        Assert.False(session.Grants("MailFathom.Mail.Read"));
    }

    /// <summary>A caller the deployment granted nothing reads as one, which is the accurate answer rather than a failure.</summary>
    [Fact]
    public void Grants_ACallerGrantedNothing_CarriesNothing()
    {
        // Arrange
        var session = SessionGranting();

        // Act, Assert
        Assert.False(session.Grants("mailfathom.mail.read"));
    }

    /// <summary>
    /// A document that named no grant at all is not something a serializer reports, so it arrives here as a missing
    /// list. Reading it as a caller granted nothing is the safe half of the two possible mistakes.
    /// </summary>
    [Fact]
    public void Grants_ADocumentNamingNoGrantAtAll_ReadsAsACallerGrantedNothing()
    {
        // Arrange
        var session = new DeploymentSession("MailFathom", "0.8.0", null!);

        // Act, Assert
        Assert.False(session.Grants("mailfathom.mail.read"));
    }

    /// <summary>Asking about nothing is a caller's mistake rather than an answer.</summary>
    [Fact]
    public void Grants_ABlankName_IsRefused()
    {
        // Arrange
        var session = SessionGranting("mailfathom.mail.read");

        // Act, Assert
        Assert.Throws<ArgumentException>(() => session.Grants("  "));
    }

    /// <summary>
    /// The route names no credential — not the material, and not the deployment's own configured name for whatever
    /// authenticated — so this end models none either. A client carrying such a field would be carrying something it
    /// never receives, and the field would be the first thing a screen or a log put a caller's identity into.
    /// </summary>
    [Fact]
    public void DeploymentSession_TheWireContract_ModelsNothingThatIdentifiesACredential()
    {
        // Arrange
        var contract = typeof(DeploymentSession);

        // Act
        var named = contract
            .GetProperties()
            .Select(property => property.Name)
            .Where(name => name.Contains("credential", StringComparison.OrdinalIgnoreCase)
                || name.Contains("token", StringComparison.OrdinalIgnoreCase)
                || name.Contains("subject", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Assert
        Assert.Empty(named);
    }

    private static DeploymentSession SessionGranting(params string[] permissions) =>
        new("MailFathom", "0.8.0", permissions);
}
