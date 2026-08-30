// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Generation;
using MimeKit;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Generation;

/// <summary>What a generated description becomes on the wire.</summary>
public sealed class SyntheticMimeComposerTests
{
    private static readonly MailboxAddress Recipient = new("Developer", "developer@example.com");
    private static readonly MailboxAddress SendingAccount = new("Throwaway", "throwaway@example.com");

    [Fact]
    public void Compose_AFabricatedAuthor_NamesTheInventedParticipantAndSubmitsAsTheAccount()
    {
        // Arrange
        var email = Email();

        // Act
        using var message = SyntheticMimeComposer.Compose(
            email,
            Recipient,
            SendingAccount,
            SyntheticAuthorIdentity.Fabricated);

        // Assert
        Assert.Equal(email.Author.Address, Assert.IsType<MailboxAddress>(Assert.Single(message.From)).Address);
        Assert.Equal(SendingAccount.Address, message.Sender?.Address);
        Assert.Empty(message.ReplyTo);
    }

    [Fact]
    public void Compose_AnAccountAuthor_KeepsTheInventedParticipantInReplyTo()
    {
        // Arrange
        var email = Email();

        // Act
        using var message = SyntheticMimeComposer.Compose(
            email,
            Recipient,
            SendingAccount,
            SyntheticAuthorIdentity.SendingAccount);

        // Assert
        Assert.Equal(SendingAccount.Address, Assert.IsType<MailboxAddress>(Assert.Single(message.From)).Address);
        Assert.Null(message.Sender);
        Assert.Equal(email.Author.Address, Assert.IsType<MailboxAddress>(Assert.Single(message.ReplyTo)).Address);
    }

    [Fact]
    public void Compose_Always_AddressesTheNamedRecipientAndCopiesEveryInventedParticipant()
    {
        // Arrange
        var email = Email() with
        {
            CarbonCopies =
            [
                new SyntheticParticipant("Frida Hjelm", "frida.hjelm@blueheron.test"),
                new SyntheticParticipant("Bram Caron", "bram.caron@saltmarsh.test"),
            ],
        };

        // Act
        using var message = SyntheticMimeComposer.Compose(
            email,
            Recipient,
            SendingAccount,
            SyntheticAuthorIdentity.Fabricated);

        // Assert
        Assert.Equal(Recipient.Address, Assert.IsType<MailboxAddress>(Assert.Single(message.To)).Address);
        Assert.Equal(
            ["frida.hjelm@blueheron.test", "bram.caron@saltmarsh.test"],
            message.Cc.OfType<MailboxAddress>().Select(address => address.Address));
    }

    [Fact]
    public void Compose_AThreadedReply_CarriesTheAncestryTheDescriptionHolds()
    {
        // Arrange
        var email = Email() with
        {
            InReplyTo = "parent@harbourline.test",
            References = ["grandparent@harbourline.test", "parent@harbourline.test"],
        };

        // Act
        using var message = SyntheticMimeComposer.Compose(
            email,
            Recipient,
            SendingAccount,
            SyntheticAuthorIdentity.Fabricated);

        // Assert
        Assert.Equal("parent@harbourline.test", message.InReplyTo);
        Assert.Equal(["grandparent@harbourline.test", "parent@harbourline.test"], message.References);
    }

    [Fact]
    public void Compose_APlainTextBody_CarriesOneTextPart()
    {
        // Arrange
        var email = Email();

        // Act
        using var message = SyntheticMimeComposer.Compose(
            email,
            Recipient,
            SendingAccount,
            SyntheticAuthorIdentity.Fabricated);

        // Assert
        var part = Assert.IsType<TextPart>(message.Body);

        Assert.True(part.IsPlain);
        Assert.Equal("The tidal buoy surveys the quay.", part.Text);
    }

    [Fact]
    public void Compose_AnHtmlBody_CarriesOneHtmlPart()
    {
        // Arrange
        var email = Email(SyntheticBodyShape.HtmlOnly);

        // Act
        using var message = SyntheticMimeComposer.Compose(
            email,
            Recipient,
            SendingAccount,
            SyntheticAuthorIdentity.Fabricated);

        // Assert
        Assert.True(Assert.IsType<TextPart>(message.Body).IsHtml);
    }

    [Fact]
    public void Compose_AnAlternativeBody_CarriesBothPartsInThatOrder()
    {
        // Arrange
        var email = Email(SyntheticBodyShape.TextAndHtmlAlternative);

        // Act
        using var message = SyntheticMimeComposer.Compose(
            email,
            Recipient,
            SendingAccount,
            SyntheticAuthorIdentity.Fabricated);

        // Assert
        var alternative = Assert.IsType<MultipartAlternative>(message.Body);

        Assert.True(Assert.IsType<TextPart>(alternative[0]).IsPlain);
        Assert.True(Assert.IsType<TextPart>(alternative[1]).IsHtml);
    }

    // The character set is named rather than passed: it is internal, and widening it to let a public test signature
    // carry it would change the production type to suit a test.
    [Theory]
    [InlineData(nameof(SyntheticCharacterSet.Ascii), "us-ascii")]
    [InlineData(nameof(SyntheticCharacterSet.Latin1), "iso-8859-1")]
    [InlineData(nameof(SyntheticCharacterSet.Utf8), "utf-8")]
    public void Compose_ABody_EncodesItInTheCharacterSetTheDescriptionNames(
        string characterSetName,
        string expectedCharset)
    {
        // Arrange
        var characterSet = Enum.Parse<SyntheticCharacterSet>(characterSetName);
        var email = Email() with { Body = Body(SyntheticBodyShape.PlainTextOnly, characterSet) };

        // Act
        using var message = SyntheticMimeComposer.Compose(
            email,
            Recipient,
            SendingAccount,
            SyntheticAuthorIdentity.Fabricated);

        // Assert
        Assert.Equal(
            expectedCharset,
            Assert.IsType<TextPart>(message.Body).ContentType.Charset,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compose_AnAttachment_CarriesExactlyTheBytesTheDescriptionStandsFor()
    {
        // Arrange
        var attachment = new SyntheticEmailAttachment("tide-table.csv", "text", "csv", 321, ContentSeed: 17);
        var email = Email() with { Attachment = attachment };

        // Act
        using var message = SyntheticMimeComposer.Compose(
            email,
            Recipient,
            SendingAccount,
            SyntheticAuthorIdentity.Fabricated);

        // Assert
        var mixed = Assert.IsType<Multipart>(message.Body);
        var part = Assert.IsType<MimePart>(mixed[1]);

        using var carried = new MemoryStream();

        Assert.NotNull(part.Content);
        part.Content.DecodeTo(carried, TestContext.Current.CancellationToken);

        Assert.Equal("tide-table.csv", part.FileName);
        Assert.Equal("text/csv", part.ContentType.MimeType, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(attachment.MaterializeContent().ToArray(), carried.ToArray());
    }

    [Fact]
    public void Compose_ANullArgument_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => SyntheticMimeComposer.Compose(
            null!,
            Recipient,
            SendingAccount,
            SyntheticAuthorIdentity.Fabricated));
    }

    private static SyntheticEmail Email(SyntheticBodyShape shape = SyntheticBodyShape.PlainTextOnly) => new(
        "0000000a.1f2e3d4c@harbourline.test",
        InReplyTo: null,
        References: [],
        new SyntheticParticipant("Ada Almqvist", "ada.almqvist@harbourline.test"),
        CarbonCopies: [],
        "The tidal buoy",
        new DateTimeOffset(2026, 6, 1, 9, 14, 0, TimeSpan.Zero),
        Body(shape, SyntheticCharacterSet.Utf8),
        Attachment: null,
        AiOrigin: null);

    private static SyntheticEmailBody Body(SyntheticBodyShape shape, SyntheticCharacterSet characterSet) => new(
        shape,
        "The tidal buoy surveys the quay.",
        "<html><body><p>The tidal buoy surveys the quay.</p></body></html>",
        characterSet,
        Decoy: null);
}
