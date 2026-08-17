// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules;

/// <summary>Covers the operator's reading of the rule set, which is a report of the deployment's own state.</summary>
public sealed class MailRuleSetReaderTests
{
    private readonly IMailRuleSetSource ruleSets = Substitute.For<IMailRuleSetSource>();

    [Fact]
    public void Read_ACallerGrantedTheAdministrativeRead_IsServedTheSetInForce()
    {
        // Arrange
        var inForce = EmptyRuleSet();
        this.ruleSets.Current.Returns(inForce);
        var reader = this.ReaderFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var read = reader.Read();

        // Assert
        Assert.Same(inForce, read);
    }

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint that passed no filter meets the same refusal.</summary>
    [Fact]
    public void Read_ACallerGrantedOnlyTheAuditRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var reader = this.ReaderFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminAuditRead));

        // Act
        var refusal = Assert.Throws<PrincipalNotAuthorizedException>(reader.Read);

        // Assert
        Assert.Equal(MailFathomPermission.AdminRead, refusal.RequiredPermission);
        _ = this.ruleSets.DidNotReceive().Current;
    }

    private static MailRuleSet EmptyRuleSet() => MailRuleSet.Create(
        [],
        MailRuleSetRevision.Create([]),
        MailRuleConditionBounds.Default);

    private MailRuleSetReader ReaderFor(AccessAuthorization authorization) => new(this.ruleSets, authorization);
}
