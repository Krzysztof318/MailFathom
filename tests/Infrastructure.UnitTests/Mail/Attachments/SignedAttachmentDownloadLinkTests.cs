// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Common;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Mail.Attachments;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Attachments;

/// <summary>Covers the capability an attachment link carries: what it authorizes, and everything that must not.</summary>
/// <remarks>
/// The two halves are tested together because a signature is only meaningful as a round trip: a mint nothing verifies
/// and a verification nothing minted would both pass on their own while agreeing about nothing.
/// </remarks>
public sealed class SignedAttachmentDownloadLinkTests
{
    private const string ActiveKeyId = "active";
    private const string RotatedKeyId = "rotated";

    private static readonly Uri AddressPrefix = new("https://mail.example.test/attachments/");
    private static readonly DateTimeOffset MintedAt = new(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>A minted link addresses the deployment's declared address and redeems back to what it was minted for.</summary>
    [Fact]
    public async Task IssueAsync_AttachmentOfAnEmail_MintsALinkThatRedeemsToThatSameAttachment()
    {
        // Arrange
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var (issuer, ticketReader, _) = CreateLinks();

        // Act
        var links = await issuer.IssueAsync(storedEmailId, attachmentCount: 3, TestContext.Current.CancellationToken);
        var ticket = await ticketReader.RedeemAsync(
            CapabilityOf(links[2]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, links.Count);
        Assert.All(links, link => Assert.StartsWith(AddressPrefix.AbsoluteUri, link.Address.AbsoluteUri, StringComparison.Ordinal));
        Assert.NotNull(ticket);
        Assert.Equal(storedEmailId, ticket.StoredEmailId);
        Assert.Equal(2, ticket.AttachmentPosition);
    }

    /// <summary>The expiry a link publishes is the one it is judged against, and it comes from the injected clock.</summary>
    [Fact]
    public async Task IssueAsync_MintedLink_ExpiresTheConfiguredLifetimeAfterTheInjectedClockReadsIt()
    {
        // Arrange
        var (issuer, _, clock) = CreateLinks();

        // Act
        var link = Assert.Single(await issuer.IssueAsync(
            StoredEmailId.Create(Guid.CreateVersion7()),
            attachmentCount: 1,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(clock.GetUtcNow() + Lifetime, link.ExpiresAt);
    }

    /// <summary>
    /// The window is the whole of a link's revocation model, so a capability presented after it must be refused. The
    /// default and a configured lifetime are both covered, because the value comes from configuration and a deployment
    /// that narrows it is exactly the one relying on this.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(1)]
    public async Task RedeemAsync_CapabilityPresentedAfterItsWindow_IsRefused(int lifetimeMinutes)
    {
        // Arrange
        var lifetime = TimeSpan.FromMinutes(lifetimeMinutes);
        var (issuer, ticketReader, clock) = CreateLinks(lifetime: lifetime);
        var link = Assert.Single(await issuer.IssueAsync(
            StoredEmailId.Create(Guid.CreateVersion7()),
            attachmentCount: 1,
            TestContext.Current.CancellationToken));

        // Act
        clock.Advance(lifetime - TimeSpan.FromSeconds(1));
        var justInsideTheWindow = await ticketReader.RedeemAsync(
            CapabilityOf(link),
            TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromSeconds(1));
        var atTheInstantItExpires = await ticketReader.RedeemAsync(
            CapabilityOf(link),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(justInsideTheWindow);
        Assert.Null(atTheInstantItExpires);
    }

    /// <summary>
    /// A capability whose payload was edited must not verify, whichever field was edited: the tag covers the whole
    /// payload, so changing the email, the position, or the expiry is one forgery rather than three cases.
    /// </summary>
    [Fact]
    public async Task RedeemAsync_TamperedCapability_IsRefused()
    {
        // Arrange
        var (issuer, ticketReader, _) = CreateLinks();
        var link = Assert.Single(await issuer.IssueAsync(
            StoredEmailId.Create(Guid.CreateVersion7()),
            attachmentCount: 1,
            TestContext.Current.CancellationToken));
        var capability = CapabilityOf(link);

        // Act — one character of the payload and one of the tag, which are the two halves a forger can reach. Both are
        // taken from the interior of their half rather than its end: the final character of a base64url run carries
        // unused low bits, so editing that one can decode back to the same octets and would prove nothing.
        var separator = capability.IndexOf('.', StringComparison.Ordinal);
        var editedPayload = await ticketReader.RedeemAsync(
            WithCharacterFlipped(capability, separator / 2),
            TestContext.Current.CancellationToken);
        var editedTag = await ticketReader.RedeemAsync(
            WithCharacterFlipped(capability, separator + 1),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(editedPayload);
        Assert.Null(editedTag);
    }

    /// <summary>Text that is not a capability at all is refused before anything is indexed into it.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-capability")]
    [InlineData(".")]
    [InlineData("....")]
    [InlineData("AAAA.")]
    [InlineData(".AAAA")]
    public async Task RedeemAsync_TextThatIsNotACapability_IsRefused(string presented)
    {
        // Arrange
        var (_, ticketReader, _) = CreateLinks();

        // Act
        var ticket = await ticketReader.RedeemAsync(presented, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(ticket);
    }

    /// <summary>A capability longer than anything this system mints is refused before the decode scans it.</summary>
    [Fact]
    public async Task RedeemAsync_CapabilityBeyondTheLengthBound_IsRefusedWithoutBeingDecoded()
    {
        // Arrange
        var (_, ticketReader, _) = CreateLinks();

        // Act
        var ticket = await ticketReader.RedeemAsync(
            new string('A', 4096) + "." + new string('A', 4096),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(ticket);
    }

    /// <summary>
    /// Two links for the same attachment are different values. Without that a capability would be a pure function of
    /// what it names, so a holder could tell that two of them point at one file and a reissue within the same second
    /// would be byte-identical to the previous one.
    /// </summary>
    [Fact]
    public async Task IssueAsync_TheSameAttachmentTwice_MintsUnrelatedCapabilities()
    {
        // Arrange
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var (issuer, _, _) = CreateLinks();

        // Act
        var first = Assert.Single(await issuer.IssueAsync(storedEmailId, 1, TestContext.Current.CancellationToken));
        var second = Assert.Single(await issuer.IssueAsync(storedEmailId, 1, TestContext.Current.CancellationToken));

        // Assert
        Assert.NotEqual(first.Address, second.Address);
    }

    /// <summary>A capability naming a key this deployment does not hold is refused rather than raised.</summary>
    /// <remarks>
    /// The two are not the same outcome: a forgery naming an invented key reaches an unauthenticated route, so raising
    /// there would turn a refusal into an error response and a log line an attacker chose the contents of.
    /// </remarks>
    [Fact]
    public async Task RedeemAsync_CapabilityNamingAKeyThisRingDoesNotHold_IsRefused()
    {
        // Arrange — minted by a deployment whose ring holds the rotated key, presented to one whose ring does not.
        var (rotatedIssuer, _, _) = CreateLinks(activeKeyId: RotatedKeyId);
        var link = Assert.Single(await rotatedIssuer.IssueAsync(
            StoredEmailId.Create(Guid.CreateVersion7()),
            1,
            TestContext.Current.CancellationToken));

        var narrowedRing = new DataEncryptionKeyRing(
            () => new DataEncryptionKeyRingSettings(
                ActiveKeyId,
                [new DataEncryptionKeyReference(ActiveKeyId, Reference(ActiveKeyId))]),
            CreateResolver());
        var ticketReader = new SignedAttachmentDownloadTicketReader(narrowedRing, new FakeTimeProvider(MintedAt));

        // Act
        var ticket = await ticketReader.RedeemAsync(CapabilityOf(link), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(ticket);
    }

    /// <summary>
    /// A rotation must not invalidate the links a deployment has just handed out, which is why the capability names the
    /// key it was signed with rather than being verified against whichever key is active when it comes back.
    /// </summary>
    [Fact]
    public async Task RedeemAsync_CapabilityMintedBeforeARotation_StillVerifiesForTheRestOfItsLifetime()
    {
        // Arrange
        var (issuer, _, _) = CreateLinks();
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var link = Assert.Single(await issuer.IssueAsync(storedEmailId, 1, TestContext.Current.CancellationToken));

        // Act — the ring has rotated: the active key is now the other one, and both stay configured.
        var (_, ticketReaderAfterRotation, _) = CreateLinks(activeKeyId: RotatedKeyId);
        var ticket = await ticketReaderAfterRotation.RedeemAsync(
            CapabilityOf(link),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(ticket);
        Assert.Equal(storedEmailId, ticket.StoredEmailId);
    }

    /// <summary>A signing key derived under one purpose must not verify what another purpose signed.</summary>
    /// <remarks>
    /// This is what makes sharing one ring across several things safe, and it is asserted here rather than assumed
    /// because the whole separation is one info label that a rename would carry along silently.
    /// </remarks>
    [Fact]
    public void DeriveKeyFor_TwoPurposes_ProducesUnrelatedKeys()
    {
        // Arrange
        using var key = DataEncryptionKey.Decode(
            ActiveKeyId,
            ResolvedSecret.FromText(Convert.ToBase64String(KeyOf(0x11))),
            out _)!;
        var signing = new byte[32];
        var sealing = new byte[32];

        // Act
        key.DeriveKeyFor(DataEncryptionPurpose.AttachmentDownloadLink, signing);
        key.DeriveKeyFor(DataEncryptionPurpose.MailboxRefreshToken, sealing);

        // Assert
        Assert.NotEqual(sealing, signing);
    }

    /// <summary>A deployment declaring no address, and one configuring no ring, both issue nothing.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task CanIssueLinks_WithoutBothAnAddressAndAKeyRing_ReportsFalseAndMintsNothing(
        bool declaresAddress,
        bool configuresKeyRing)
    {
        // Arrange
        var keyRing = new DataEncryptionKeyRing(
            () => configuresKeyRing
                ? new DataEncryptionKeyRingSettings(
                    ActiveKeyId,
                    [new DataEncryptionKeyReference(ActiveKeyId, Reference(ActiveKeyId))])
                : new DataEncryptionKeyRingSettings(string.Empty, []),
            CreateResolver());
        var issuer = new SignedAttachmentDownloadLinkIssuer(
            keyRing,
            new AttachmentDownloadSettings(declaresAddress ? AddressPrefix : null, Lifetime),
            new FakeTimeProvider(MintedAt));

        // Act
        var links = await issuer.IssueAsync(
            StoredEmailId.Create(Guid.CreateVersion7()),
            attachmentCount: 2,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(issuer.CanIssueLinks);
        Assert.Empty(links);
    }

    /// <summary>A message carrying nothing mints nothing, so an ordinary read never resolves key material.</summary>
    [Fact]
    public async Task IssueAsync_MessageCarryingNoAttachment_MintsNothing()
    {
        // Arrange
        var (issuer, _, _) = CreateLinks();

        // Act
        var links = await issuer.IssueAsync(
            StoredEmailId.Create(Guid.CreateVersion7()),
            attachmentCount: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(issuer.CanIssueLinks);
        Assert.Empty(links);
    }

    [Fact]
    public async Task IssueAsync_NegativeAttachmentCount_IsRefused()
    {
        // Arrange
        var (issuer, _, _) = CreateLinks();

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => issuer.IssueAsync(
            StoredEmailId.Create(Guid.CreateVersion7()),
            attachmentCount: -1,
            TestContext.Current.CancellationToken));
    }

    private static (SignedAttachmentDownloadLinkIssuer Issuer, SignedAttachmentDownloadTicketReader TicketReader, FakeTimeProvider Clock)
        CreateLinks(string activeKeyId = ActiveKeyId, TimeSpan? lifetime = null)
    {
        var clock = new FakeTimeProvider(MintedAt);
        var keyRing = new DataEncryptionKeyRing(
            () => new DataEncryptionKeyRingSettings(
                activeKeyId,
                [
                    new DataEncryptionKeyReference(ActiveKeyId, Reference(ActiveKeyId)),
                    new DataEncryptionKeyReference(RotatedKeyId, Reference(RotatedKeyId)),
                ]),
            CreateResolver());

        return (
            new SignedAttachmentDownloadLinkIssuer(
                keyRing,
                new AttachmentDownloadSettings(AddressPrefix, lifetime ?? Lifetime),
                clock),
            new SignedAttachmentDownloadTicketReader(keyRing, clock),
            clock);
    }

    private static ProvisionedMaterialResolver CreateResolver()
    {
        var resolver = new ProvisionedMaterialResolver();
        resolver.ProvisionText($"plaintext:{ActiveKeyId}", Convert.ToBase64String(KeyOf(0x11)));
        resolver.ProvisionText($"plaintext:{RotatedKeyId}", Convert.ToBase64String(KeyOf(0x22)));

        return resolver;
    }

    private static ConfiguredSecret Reference(string keyId) =>
        new() { Name = $"data-key-{keyId}", SecretReference = $"plaintext:{keyId}" };

    private static byte[] KeyOf(byte fill) => [.. Enumerable.Repeat(fill, AesGcmEnvelope.KeySizeInBytes)];

    /// <summary>Reads the capability out of a minted address, which is its last path segment.</summary>
    private static string CapabilityOf(AttachmentDownloadLink link) => link.Address.Segments[^1];

    /// <summary>Edits one character of a capability into a different one from the same alphabet.</summary>
    private static string WithCharacterFlipped(string capability, int position) =>
        string.Concat(
            capability.AsSpan(0, position),
            capability[position] == 'A' ? "B" : "A",
            capability.AsSpan(position + 1));
}
