// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.IO.Compression;
using System.Text;
using MailFathom.SyntheticMail.Corpus;
using MailFathom.SyntheticMail.Delivery;
using MailFathom.SyntheticMail.Generation;
using MailFathom.SyntheticMail.UnitTests.TestDoubles;
using MimeKit;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Corpus;

/// <summary>What a corpus carries between the run that generated it and the run that delivers it.</summary>
public sealed class CorpusArchiveTests
{
    private static readonly DateTimeOffset SentAt = new(2026, 8, 8, 11, 30, 0, TimeSpan.Zero);

    private static readonly MailboxAddress Mailbox = new("Owner", "owner@example.test");

    [Fact]
    public void Write_AnExchange_WritesOneMessagePerTurnAndTheInvocationBesideThem()
    {
        // Arrange
        using var destination = new MemoryStream();

        // Act
        CorpusArchive.Write(destination, "owner@example.test --seed 42", [Conversation(2), Conversation(3, thread: 1)], Mailbox);

        // Assert
        destination.Position = 0;
        using var archive = new ZipArchive(destination, ZipArchiveMode.Read);

        Assert.Equal(
            ["0001.eml", "0002.eml", "0003.eml", "0004.eml", "0005.eml", CorpusArchive.ManifestEntryName],
            archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        Assert.Contains("owner@example.test --seed 42", ReadEntry(archive, CorpusArchive.ManifestEntryName), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_AnExchange_AddressesEachTurnToTheOtherSideOfIt()
    {
        // Arrange
        using var destination = new MemoryStream();

        // Act
        CorpusArchive.Write(destination, "owner@example.test", [Conversation(2)], Mailbox);

        // Assert
        // The exchange opens with the correspondent writing to the mailbox and the mailbox answering them, so a corpus
        // opened in a mail client reads as correspondence rather than as a run of inbound mail.
        var corpus = Read(destination);
        var turns = Assert.Single(corpus.Exchanges);

        using var opening = turns[0].Compose();
        using var reply = turns[1].Compose();

        Assert.Equal("correspondent@quietfjord.test", Assert.Single(opening.From.Mailboxes).Address);
        Assert.Equal("owner@example.test", Assert.Single(opening.To.Mailboxes).Address);
        Assert.Equal("owner@example.test", Assert.Single(reply.From.Mailboxes).Address);
        Assert.Equal("correspondent@quietfjord.test", Assert.Single(reply.To.Mailboxes).Address);
    }

    [Fact]
    public void Write_AnExchange_CarriesNothingAboutTheRunThatWroteIt()
    {
        // Arrange
        using var destination = new MemoryStream();

        // Act
        CorpusArchive.Write(destination, "owner@example.test", [Conversation(2)], Mailbox);

        // Assert
        // A corpus is replayed under an account that did not exist when it was written, so the headers a submission
        // adds are the delivering run's to write rather than the corpus's to carry.
        var turns = Assert.Single(Read(destination).Exchanges);

        using var opening = turns[0].Compose();

        Assert.Null(opening.Sender);
        Assert.Empty(opening.ReplyTo.Mailboxes);
        Assert.Null(opening.Headers[SyntheticDeliveryMarker.HeaderName]);
    }

    [Fact]
    public void Read_ACorpusItWrote_ReportsTheInvocationAndEveryTurnInOrder()
    {
        // Arrange
        using var destination = new MemoryStream();

        CorpusArchive.Write(destination, "owner@example.test --seed 42", [Conversation(2), Conversation(3, thread: 1)], Mailbox);

        // Act
        var corpus = Read(destination);

        // Assert
        Assert.Equal("owner@example.test --seed 42", corpus.Invocation);
        Assert.Equal([2, 3], corpus.Exchanges.Select(exchange => exchange.Count));
        Assert.Equal("0.0@quietfjord.test", corpus.Exchanges[0][0].MessageId);
        Assert.Equal("1.2@quietfjord.test", corpus.Exchanges[1][2].MessageId);
    }

    [Fact]
    public void Read_ATurn_BuildsItsMessageAfreshEachTime()
    {
        // Arrange
        using var destination = new MemoryStream();

        CorpusArchive.Write(destination, "owner@example.test", [Conversation(2)], Mailbox);

        var turn = Read(destination).Exchanges[0][0];

        // Act
        using var first = turn.Compose();
        using var second = turn.Compose();

        // Assert
        // Delivery addresses, threads, and disposes what it is given, so a second attempt has to start from the corpus
        // rather than from what the first one left behind.
        Assert.NotSame(first, second);
        Assert.Equal(first.MessageId, second.MessageId);
    }

    [Fact]
    public void Read_ACorpusNothingHereGenerated_DeliversWhateverMailIsInIt()
    {
        // Arrange
        using var source = HandWrittenCorpus.Build(
            """{ "invocation": "written by hand", "exchanges": [["a.eml", "b.eml"]] }""",
            ("a.eml", HandWrittenCorpus.Message("one@invented.test", "Ferry timetable", "ada@invented.test", "owner@example.test")),
            ("b.eml", HandWrittenCorpus.Message("two@invented.test", "Re: Ferry timetable", "owner@example.test", "ada@invented.test")));

        // Act
        var corpus = CorpusArchive.Read(source);

        // Assert
        // Nothing about replay depends on this repository's generator: a corpus is messages and a manifest, and a
        // second one written by anything that produces those is delivered the same way.
        Assert.Equal("written by hand", corpus.Invocation);

        var turns = Assert.Single(corpus.Exchanges);

        Assert.Equal(["one@invented.test", "two@invented.test"], turns.Select(turn => turn.MessageId));
        Assert.Equal("ada@invented.test", turns[0].Author.Address);
        Assert.Equal("Ferry timetable", turns[0].Subject);
    }

    [Fact]
    public void Read_AnArchiveWithNoManifest_IsRefusedSayingSo()
    {
        // Arrange
        using var source = HandWrittenCorpus.Build(
            manifest: null,
            ("a.eml", HandWrittenCorpus.Message("one@invented.test", "Ferry timetable", "ada@invented.test", "owner@example.test")));

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => CorpusArchive.Read(source));

        // Assert
        Assert.Contains(CorpusArchive.ManifestEntryName, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_AManifestNamingAMessageTheArchiveDoesNotHold_IsRefusedNamingIt()
    {
        // Arrange
        using var source = HandWrittenCorpus.Build(
            """{ "invocation": "written by hand", "exchanges": [["a.eml", "missing.eml"]] }""",
            ("a.eml", HandWrittenCorpus.Message("one@invented.test", "Ferry timetable", "ada@invented.test", "owner@example.test")));

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => CorpusArchive.Read(source));

        // Assert
        Assert.Contains("missing.eml", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_AMessageWithNoAuthor_IsRefusedNamingIt()
    {
        // Arrange
        using var source = HandWrittenCorpus.Build(
            """{ "invocation": "written by hand", "exchanges": [["a.eml"]] }""",
            ("a.eml", "Message-Id: <one@invented.test>\r\nSubject: Ferry timetable\r\nTo: owner@example.test\r\n\r\nNothing.\r\n"));

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => CorpusArchive.Read(source));

        // Assert
        // The author decides who a submitted copy is from, so a message without one is a corpus that cannot be
        // delivered rather than one delivered as somebody else.
        Assert.Contains("a.eml", failure.Message, StringComparison.Ordinal);
        Assert.Contains("author", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_AManifestHoldingNoExchanges_IsRefusedSayingThereIsNothingToReplay()
    {
        // Arrange
        using var source = HandWrittenCorpus.Build("""{ "invocation": "written by hand", "exchanges": [] }""");

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => CorpusArchive.Read(source));

        // Assert
        Assert.Contains("nothing to replay", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_SomethingThatIsNotAnArchive_IsRefusedSayingSo()
    {
        // Arrange
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("not an archive"));

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => CorpusArchive.Read(source));

        // Assert
        Assert.Contains("could not be read", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_AManifestNamingAnExchangeThatIsNull_IsRefusedAsAnExchangeWithNothingInIt()
    {
        // Arrange
        using var source = HandWrittenCorpus.Build("""{ "invocation": "written by hand", "exchanges": [null] }""");

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => CorpusArchive.Read(source));

        // Assert
        // JSON accepts a null where an exchange belongs, and a corpus is read from a file this tool did not
        // necessarily write.
        Assert.Contains("no messages in it", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_AManifestNamingMoreMessagesThanACorpusMayHold_IsRefusedNamingTheBound()
    {
        // Arrange
        var named = string.Join(", ", Enumerable.Range(0, 2001).Select(turn => $"\"{turn}.eml\""));

        using var source = HandWrittenCorpus.Build($$"""{ "invocation": "written by hand", "exchanges": [[{{named}}]] }""");

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => CorpusArchive.Read(source));

        // Assert
        // The bound is checked before an entry is opened, so a manifest naming a million messages costs a count
        // rather than a million lookups.
        Assert.Contains("2001 messages", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_NoExchanges_IsRefusedRatherThanWritingACorpusNothingCouldReplay()
    {
        // Arrange
        using var destination = new MemoryStream();

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(
            () => CorpusArchive.Write(destination, "owner@example.test", [], Mailbox));

        // Assert
        Assert.Contains("no exchanges to export", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_AMessageDatedBeforeAnArchiveCanCarryIt_IsRefusedNamingTheDateAndTheRange()
    {
        // Arrange
        using var destination = new MemoryStream();

        var conversation = Conversation(2) with { };
        var dated = new SyntheticConversation(
            conversation.Correspondent,
            [.. conversation.Messages.Select(message => message with { SentAt = new DateTimeOffset(1975, 4, 2, 9, 0, 0, TimeSpan.Zero) })]);

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(
            () => CorpusArchive.Write(destination, "owner@example.test", [dated], Mailbox));

        // Assert
        // A zip timestamp is a DOS date and reaches from 1980 to 2107, while '--until' and '--days' together draw a
        // corpus dated wherever a DateOnly reaches. The setter's own exception names neither the date nor the option.
        Assert.Contains("1975-04-02", failure.Message, StringComparison.Ordinal);
        Assert.Contains("--until", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_AnEntryThatDecompressesPastTheBound_IsRefusedBeforeItIsAllHeld()
    {
        // Arrange
        // Compressed the entry is a few kilobytes, which is what makes the bound something to measure on what comes
        // out of the stream rather than on the size the archive states about itself.
        using var source = HandWrittenCorpus.Build(
            """{ "invocation": "written by hand", "exchanges": [["a.eml"]] }""",
            ("a.eml", new string('\0', (16 * 1024 * 1024) + 1)));

        // Act
        var failure = Assert.Throws<SyntheticMailFailure>(() => CorpusArchive.Read(source));

        // Assert
        Assert.Contains("decompresses past", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ANullArgument_IsRefused()
    {
        // Arrange
        using var destination = new MemoryStream();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => CorpusArchive.Write(null!, "owner@example.test", [Conversation(2)], Mailbox));
        Assert.Throws<ArgumentNullException>(() => CorpusArchive.Write(destination, null!, [Conversation(2)], Mailbox));
        Assert.Throws<ArgumentNullException>(() => CorpusArchive.Write(destination, "owner@example.test", null!, Mailbox));
        Assert.Throws<ArgumentNullException>(() => CorpusArchive.Write(destination, "owner@example.test", [Conversation(2)], null!));
        Assert.Throws<ArgumentNullException>(() => CorpusArchive.Read(null!));
    }

    private static ExportedCorpus Read(MemoryStream written)
    {
        using var source = new MemoryStream(written.ToArray(), writable: false);

        return CorpusArchive.Read(source);
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using var contents = archive.GetEntry(name)!.Open();
        using var reader = new StreamReader(contents, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    /// <summary>Builds one exchange by hand, so a test states the shape it is asserting rather than drawing it.</summary>
    private static SyntheticConversation Conversation(int turns, int thread = 0)
    {
        var correspondent = new SyntheticParticipant("Correspondent", "correspondent@quietfjord.test");
        var mailbox = new SyntheticParticipant("Owner", "owner@example.test");

        return new SyntheticConversation(
            correspondent,
            [
                .. Enumerable.Range(0, turns).Select(turn => new SyntheticEmail(
                    $"{thread}.{turn}@quietfjord.test",
                    null,
                    [],
                    SyntheticConversation.SideOf(turn) == SyntheticThreadSide.Correspondent ? correspondent : mailbox,
                    [],
                    turn == 0 ? "The lock gate" : "Re: The lock gate",
                    SentAt.AddDays(turn),
                    new SyntheticEmailBody(
                        SyntheticBodyShape.PlainTextOnly,
                        "Whatever it says.",
                        "<p>Whatever it says.</p>",
                        SyntheticCharacterSet.Ascii,
                        null),
                    null,
                    null)),
            ]);
    }
}
