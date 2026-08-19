// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using MailFathom.Host.Configuration.Mail;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>
/// Covers how large a message this deployment is willing to compose: that the defaults are usable correspondence
/// bounds, and that a combination which could never compose anything is refused at startup.
/// </summary>
public sealed class MailDeliveryOptionsTests
{
    /// <summary>An operator who writes no section gets bounds that send ordinary correspondence and refuse a mailing list.</summary>
    [Fact]
    public void Defaults_UnconfiguredSection_AreUsableCorrespondenceBounds()
    {
        // Act
        var options = new MailDeliveryOptions();

        // Assert
        Assert.InRange(options.MaxRecipientCount, 1, OutgoingEmailRequest.MaximumRecipientCount);
        Assert.True(options.MaxAttachmentBytes < options.MaxMessageBytes);
        Assert.Empty(Validate(options));
    }

    /// <summary>
    /// A per-file bound above the whole-message bound describes a file that could never be sent, and the refusal an
    /// operator would meet is the one about the message rather than the one they configured.
    /// </summary>
    [Fact]
    public void Validate_AttachmentBoundAboveTheMessageBound_IsRefused()
    {
        // Arrange
        var options = new MailDeliveryOptions { MaxAttachmentBytes = 32 * 1024 * 1024, MaxMessageBytes = 1024 * 1024 };

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Contains(nameof(MailDeliveryOptions.MaxAttachmentBytes), result.MemberNames);
    }

    /// <summary>A deployment that attaches nothing is not judged against a bound it can never reach.</summary>
    [Fact]
    public void Validate_DeploymentAttachingNoFiles_IgnoresThePerFileBound()
    {
        // Arrange
        var options = new MailDeliveryOptions
        {
            MaxAttachmentCount = 0,
            MaxAttachmentBytes = 32 * 1024 * 1024,
            MaxMessageBytes = 1024 * 1024,
        };

        // Act and assert
        Assert.Empty(Validate(options));
    }

    /// <summary>More recipients than an outgoing record can hold would compose a message no send could be written for.</summary>
    [Fact]
    public void Validate_MoreRecipientsThanARecordHolds_IsRefused()
    {
        // Arrange
        var options = new MailDeliveryOptions
        {
            MaxRecipientCount = OutgoingEmailRequest.MaximumRecipientCount + 1,
        };

        // Act
        var results = ValidateWithDataAnnotations(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Contains(nameof(MailDeliveryOptions.MaxRecipientCount), result.MemberNames);
    }

    /// <summary>A body bound outside what the reference publishes is refused rather than composed against.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(10_000_001)]
    public void Validate_BodyLengthOutsideTheDocumentedRange_IsRefused(int maxBodyCharacters)
    {
        // Arrange
        var options = new MailDeliveryOptions { MaxBodyCharacters = maxBodyCharacters };

        // Act
        var results = ValidateWithDataAnnotations(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(MailDeliveryOptions.MaxBodyCharacters)));
    }

    /// <summary>Attaching nothing is a deployment's choice; attaching a negative number of files is not a bound.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_AttachmentCountOutsideTheDocumentedRange_IsRefused(int maxAttachmentCount)
    {
        // Arrange
        var options = new MailDeliveryOptions { MaxAttachmentCount = maxAttachmentCount };

        // Act
        var results = ValidateWithDataAnnotations(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(MailDeliveryOptions.MaxAttachmentCount)));
    }

    /// <summary>A per-file bound outside what the reference publishes is refused wherever the whole-message bound sits.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(104_857_601L)]
    public void Validate_AttachmentSizeOutsideTheDocumentedRange_IsRefused(long maxAttachmentBytes)
    {
        // Arrange
        var options = new MailDeliveryOptions
        {
            MaxAttachmentBytes = maxAttachmentBytes,
            MaxMessageBytes = 200L * 1024 * 1024,
        };

        // Act
        var results = ValidateWithDataAnnotations(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(MailDeliveryOptions.MaxAttachmentBytes)));
    }

    /// <summary>A whole-message bound outside what the reference publishes is refused rather than composed against.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(209_715_201L)]
    public void Validate_MessageSizeOutsideTheDocumentedRange_IsRefused(long maxMessageBytes)
    {
        // Arrange
        var options = new MailDeliveryOptions { MaxAttachmentCount = 0, MaxMessageBytes = maxMessageBytes };

        // Act
        var results = ValidateWithDataAnnotations(options);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(MailDeliveryOptions.MaxMessageBytes)));
    }

    /// <summary>
    /// An attempt that could still be transmitting when its lease runs out is a second attempt taking a message the
    /// first may already have sent, so the ordering is refused at startup rather than met in a mailbox.
    /// </summary>
    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 11)]
    public void Validate_AttemptTimeoutReachingTheLeaseDuration_IsRefused(int leaseMinutes, int timeoutMinutes)
    {
        // Arrange
        var options = new MailDeliveryOptions
        {
            LeaseDuration = TimeSpan.FromMinutes(leaseMinutes),
            AttemptTimeout = TimeSpan.FromMinutes(timeoutMinutes),
        };

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Contains(nameof(MailDeliveryOptions.AttemptTimeout), result.MemberNames);
    }

    /// <summary>A ceiling below the delay it caps would shorten every retry rather than bounding the growth.</summary>
    [Fact]
    public void Validate_RetryCeilingBelowItsBaseDelay_IsRefused()
    {
        // Arrange
        var options = new MailDeliveryOptions
        {
            RetryBaseDelay = TimeSpan.FromMinutes(10),
            RetryMaxDelay = TimeSpan.FromMinutes(1),
        };

        // Act
        var results = Validate(options);

        // Assert
        var result = Assert.Single(results);
        Assert.Contains(nameof(MailDeliveryOptions.RetryMaxDelay), result.MemberNames);
    }

    /// <summary>The defaults deliver: every outbox bound is inside its documented range and the two orderings hold.</summary>
    [Fact]
    public void Defaults_UnconfiguredSection_AreOutboxBoundsThatDeliver()
    {
        // Act
        var options = new MailDeliveryOptions();

        // Assert
        Assert.True(options.AttemptTimeout < options.LeaseDuration);
        Assert.True(options.RetryBaseDelay <= options.RetryMaxDelay);
        Assert.True(options.MaxDeliveriesPerPass > 0);
        Assert.True(options.MaxAttempts > 0);
        Assert.True(options.SignalQueueCapacity > 0);
        Assert.Empty(ValidateWithDataAnnotations(options));
    }

    /// <summary>A section that names nobody and no ceiling is the default posture: everybody may be written to, and nothing is counted.</summary>
    [Fact]
    public void Defaults_UnconfiguredSection_RestrictNobodyAndCountNothing()
    {
        // Act
        var options = new MailDeliveryOptions();

        // Assert
        Assert.False(options.RecipientPolicy.ToPolicy().RestrictsRecipients);
        Assert.True(options.SendCeilings.ToCeilings().IsUnbounded);
        Assert.Empty(Validate(options));
    }

    /// <summary>The four lists become one policy, and a denied entry outranks an allowed one that names the same mailbox.</summary>
    [Fact]
    public void RecipientPolicy_ListsAnOperatorWrote_BecomeThePolicyEveryRecipientIsJudgedAgainst()
    {
        // Arrange
        var options = new MailDeliveryOptions
        {
            RecipientPolicy = new OutgoingRecipientPolicyOptions
            {
                AllowedDomains = ["example.test"],
                DeniedAddresses = ["bruno@example.test"],
            },
        };

        // Act
        var policy = options.RecipientPolicy.ToPolicy();

        // Assert
        Assert.Empty(Validate(options));
        Assert.Null(policy.Judge(Mailbox("anna@example.test")));
        Assert.Equal(OutgoingRecipientRefusalReason.DeniedByPolicy, policy.Judge(Mailbox("bruno@example.test")));
        Assert.Equal(
            OutgoingRecipientRefusalReason.OutsideAllowedRecipients,
            policy.Judge(Mailbox("chris@elsewhere.test")));
    }

    /// <summary>An entry that names nobody is a restriction an operator believes they wrote, so startup refuses it.</summary>
    [Theory]
    [InlineData("not a domain..test", null)]
    [InlineData(null, "not an address")]
    public void Validate_RecipientPolicyEntryNamingNobody_IsRefused(string? domain, string? address)
    {
        // Arrange
        var options = new MailDeliveryOptions
        {
            RecipientPolicy = new OutgoingRecipientPolicyOptions
            {
                AllowedDomains = domain is null ? null : [domain],
                DeniedAddresses = address is null ? null : [address],
            },
        };

        // Act
        var results = Validate(options);

        // Assert
        var refusal = Assert.Single(results);
        Assert.Contains("RecipientPolicy", refusal.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(domain ?? address!, refusal.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>A per-account ceiling above the deployment's own could never bind, so it is refused rather than ignored.</summary>
    [Theory]
    [InlineData(10, 4, 0, 0, "MaxMessagesPerAccount")]
    [InlineData(0, 0, 10, 4, "MaxRecipientsPerAccount")]
    public void Validate_AccountCeilingAboveTheDeploymentsOwn_IsRefused(
        long accountMessages,
        long deploymentMessages,
        long accountRecipients,
        long deploymentRecipients,
        string expectedSetting)
    {
        // Arrange
        var options = new MailDeliveryOptions
        {
            SendCeilings = new OutgoingMailCeilingOptions
            {
                MaxMessagesPerAccount = accountMessages,
                MaxMessagesPerDeployment = deploymentMessages,
                MaxRecipientsPerAccount = accountRecipients,
                MaxRecipientsPerDeployment = deploymentRecipients,
            },
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Contains(expectedSetting, Assert.Single(results).ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>The ceilings an operator wrote are the ones a send is weighed against, over the window they named.</summary>
    [Fact]
    public void SendCeilings_CeilingsAnOperatorWrote_BecomeTheCeilingsASendIsWeighedAgainst()
    {
        // Arrange
        var options = new MailDeliveryOptions
        {
            SendCeilings = new OutgoingMailCeilingOptions
            {
                Period = TimeSpan.FromHours(1),
                MaxMessagesPerAccount = 2,
                MaxMessagesPerDeployment = 5,
            },
        };

        // Act
        var ceilings = options.SendCeilings.ToCeilings();

        // Assert
        Assert.Empty(Validate(options));
        Assert.False(ceilings.IsUnbounded);
        Assert.Equal(TimeSpan.FromHours(1), ceilings.Period);
        Assert.Equal(
            OutgoingMailCeiling.AccountMessages,
            ceilings.FindReachedCeiling(new OutgoingMailUsage(2, 2, 2, 2), recipientCount: 1));
    }

    /// <summary>A window this deployment does not count over is refused, and so is a ceiling written as a negative number.</summary>
    [Theory]
    [InlineData(1, 0, "Period")]
    [InlineData(3600, -1, "MaxMessagesPerAccount")]
    public void Validate_CeilingBlockNamingAWindowOrACountThisDeploymentCannotApply_IsRefused(
        int periodSeconds,
        long maxMessagesPerAccount,
        string expectedSetting)
    {
        // Arrange
        var options = new MailDeliveryOptions
        {
            SendCeilings = new OutgoingMailCeilingOptions
            {
                Period = TimeSpan.FromSeconds(periodSeconds),
                MaxMessagesPerAccount = maxMessagesPerAccount,
            },
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Contains(expectedSetting, Assert.Single(results).ErrorMessage, StringComparison.Ordinal);
    }

    private static EmailAddress Mailbox(string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var mailbox));

        return mailbox;
    }

    private static IReadOnlyList<ValidationResult> Validate(MailDeliveryOptions options) =>
        [.. options.Validate(new ValidationContext(options))];

    private static List<ValidationResult> ValidateWithDataAnnotations(MailDeliveryOptions options)
    {
        List<ValidationResult> results = [];
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }
}
