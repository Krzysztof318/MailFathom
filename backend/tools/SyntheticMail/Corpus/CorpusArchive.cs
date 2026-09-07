// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Delivery;
using MailFathom.SyntheticMail.Generation;
using MimeKit;

namespace MailFathom.SyntheticMail.Corpus;

/// <summary>The file an exported corpus is: a manifest and one message per file, compressed into one archive.</summary>
/// <remarks>
/// <para>
/// One archive rather than a directory, because a corpus is a thing that gets committed, copied, and handed to a
/// pipeline, and every one of those is easier with one file than with a hundred and one. What is inside it is
/// ordinary: <c>corpus.json</c> and numbered <c>.eml</c> files, so unpacking it gives a directory any mail tool opens
/// and any editor reads.
/// </para>
/// <para>
/// A message is written as its author wrote it — <c>From</c>, <c>Cc</c>, <c>To</c>, the subject, the date, and the
/// ancestry the seed proposed — and carries no trace of the run that exported it: no authenticated sender, no
/// delivery marker, and no mailbox this particular corpus was generated against beyond the invented address on the
/// other side of each exchange. That is what lets one corpus be replayed into any mailbox, which is the whole point
/// of exporting one.
/// </para>
/// <para>
/// The entries carry the dates their messages claim rather than the moment the export ran. A corpus is a thing that
/// sits in a repository, and an archive stamped with when it was written would differ from itself on every export
/// while saying nothing about the mail — and it would record when somebody happened to run the command. What it does
/// not promise is that two exports are the same file: a <c>multipart</c> message takes a boundary MimeKit draws
/// afresh each time, which the seed does not decide and nothing here needs it to.
/// </para>
/// </remarks>
internal static class CorpusArchive
{
    /// <summary>The entry the manifest is written as.</summary>
    internal const string ManifestEntryName = "corpus.json";

    /// <summary>The most a corpus may hold, and the most one of its messages may be.</summary>
    /// <remarks>
    /// A corpus is a file this tool did not necessarily write: it is committed, copied between machines, and handed to
    /// a pipeline, so what a replay parses is somebody else's archive read into this process's memory. The bounds are
    /// what turns a corrupt or hostile one into a refusal naming it rather than into an exhausted machine, and both
    /// are far past anything an export produces — a message drawn with the largest attachment this tool will generate
    /// is under 16 MiB, and the batch it belongs to is bounded at the same 2000 messages the generator is.
    /// </remarks>
    private const int MostMessages = 2000;

    private const long MostBytesPerMessage = 16L * 1024 * 1024;

    /// <summary>Writes one generated batch of exchanges as a corpus.</summary>
    /// <param name="destination">Where the archive is written; the caller owns and closes it.</param>
    /// <param name="invocation">The invocation that produced the batch.</param>
    /// <param name="exchanges">The generated exchanges, each oldest message first.</param>
    /// <param name="mailbox">The address the exchanges are with, which every correspondent's turn is written to.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when the archive could not be written.</exception>
    internal static void Write(
        Stream destination,
        string invocation,
        IReadOnlyList<SyntheticConversation> exchanges,
        MailboxAddress mailbox)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(exchanges);
        ArgumentNullException.ThrowIfNull(mailbox);

        if (exchanges.Count == 0)
        {
            throw new SyntheticMailFailure("There are no exchanges to export, and a corpus holding none is one nothing could replay.");
        }

        try
        {
            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            var names = new List<IReadOnlyList<string>>(exchanges.Count);
            var written = 0;

            foreach (var exchange in exchanges)
            {
                var correspondent = new MailboxAddress(
                    exchange.Correspondent.DisplayName,
                    exchange.Correspondent.Address);

                var turns = new List<string>(exchange.Messages.Count);

                for (var turn = 0; turn < exchange.Messages.Count; turn++)
                {
                    var writtenTo = SyntheticConversation.SideOf(turn) == SyntheticThreadSide.Correspondent
                        ? mailbox
                        : correspondent;

                    turns.Add(WriteMessage(archive, exchange.Messages[turn], writtenTo, ++written));
                }

                names.Add(turns);
            }

            WriteManifest(archive, new CorpusManifest(invocation, names), NewestDateIn(exchanges));
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or NotSupportedException)
        {
            throw new SyntheticMailFailure($"The corpus could not be written: {failure.Message}", failure);
        }
    }

    /// <summary>Reads one corpus back, without generating anything.</summary>
    /// <param name="source">The archive to read; the caller owns and closes it.</param>
    /// <returns>The invocation that produced it and every turn it holds, in delivery order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source" /> is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when the archive is not one, is missing its manifest, names a message it does not hold, or holds one that is not a message.</exception>
    internal static ExportedCorpus Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            var manifest = ReadManifest(archive);

            if (manifest.Invocation is not { Length: > 0 } invocation)
            {
                throw new SyntheticMailFailure($"'{ManifestEntryName}' names no invocation, so the corpus says nothing about what produced it.");
            }

            if (manifest.Exchanges is not { Count: > 0 } exchanges)
            {
                throw new SyntheticMailFailure($"'{ManifestEntryName}' holds no exchanges, so there is nothing to replay.");
            }

            var messages = exchanges.Sum(turns => turns?.Count ?? 0);

            if (messages > MostMessages)
            {
                throw new SyntheticMailFailure($"'{ManifestEntryName}' names {messages} messages, past the {MostMessages} a corpus may hold.");
            }

            return new ExportedCorpus(invocation, [.. exchanges.Select(turns => ReadExchange(archive, turns))]);
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            throw new SyntheticMailFailure($"The corpus could not be read: {failure.Message}", failure);
        }
    }

    private static string WriteMessage(
        ZipArchive archive,
        SyntheticEmail email,
        MailboxAddress writtenTo,
        int ordinal)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"{ordinal:0000}.eml");
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);

        entry.LastWriteTime = email.SentAt;

        using var message = SyntheticMimeComposer.ComposeAuthored(email);

        SyntheticMimeComposer.AddressAsCorrespondence(
            message,
            new MailboxAddress(email.Author.DisplayName, email.Author.Address),
            writtenTo);

        using var contents = entry.Open();

        // The wire form rather than whatever the exporting machine calls a line ending, so one corpus is one archive
        // wherever it was generated.
        message.WriteTo(DosLineEndings, contents);

        return name;
    }

    private static void WriteManifest(ZipArchive archive, CorpusManifest manifest, DateTimeOffset writtenAt)
    {
        var entry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);

        entry.LastWriteTime = writtenAt;

        using var contents = entry.Open();

        // Indented through the writer rather than through the context's options, because that context is shared with
        // the credential files this tool only ever reads and a manifest is the one thing it writes for a person.
        using var writer = new Utf8JsonWriter(contents, new JsonWriterOptions { Indented = true });

        JsonSerializer.Serialize(writer, manifest, SyntheticMailJsonContext.Default.CorpusManifest);
    }

    private static CorpusManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry(ManifestEntryName)
            ?? throw new SyntheticMailFailure($"The archive holds no '{ManifestEntryName}', so it is not an exported corpus.");

        using var contents = entry.Open();

        try
        {
            return JsonSerializer.Deserialize(contents, SyntheticMailJsonContext.Default.CorpusManifest)
                ?? throw new SyntheticMailFailure($"'{ManifestEntryName}' holds no corpus.");
        }
        catch (JsonException failure)
        {
            throw new SyntheticMailFailure($"'{ManifestEntryName}' could not be read as JSON: {failure.Message}", failure);
        }
    }

    private static IReadOnlyList<DeliverableTurn> ReadExchange(ZipArchive archive, IReadOnlyList<string>? turns)
    {
        // A hand-written manifest may say null where an exchange belongs, which JSON accepts and this reads as the
        // empty exchange it is rather than as a reference to follow.
        if (turns is not { Count: > 0 })
        {
            throw new SyntheticMailFailure($"'{ManifestEntryName}' holds an exchange with no messages in it.");
        }

        return [.. turns.Select(name => ReadTurn(archive, name ?? string.Empty))];
    }

    private static DeliverableTurn ReadTurn(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name)
            ?? throw new SyntheticMailFailure($"'{ManifestEntryName}' names '{name}', which the archive does not hold.");

        var contents = ReadEntry(entry);
        var message = Parse(contents, name);

        using (message)
        {
            var author = message.From.Mailboxes.FirstOrDefault()
                ?? throw new SyntheticMailFailure($"'{name}' carries no author, so nothing says who its half of the exchange is from.");

            if (message.MessageId is not { Length: > 0 } messageId)
            {
                throw new SyntheticMailFailure($"'{name}' carries no Message-Id, so a reply to it could not be recognized.");
            }

            // Parsed once for what the delivery reports and threads by, and parsed again per attempt so that what
            // delivery addresses, threads, and disposes is its own copy rather than the one held here.
            return new DeliverableTurn(author, messageId, message.Subject ?? string.Empty, () => Parse(contents, name));
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > MostBytesPerMessage)
        {
            throw new SyntheticMailFailure($"'{entry.FullName}' is {entry.Length} bytes, past the {MostBytesPerMessage} a message may be.");
        }

        using var contents = entry.Open();
        using var buffer = new MemoryStream();

        contents.CopyTo(buffer);

        return buffer.ToArray();
    }

    private static MimeMessage Parse(byte[] contents, string name)
    {
        try
        {
            using var buffer = new MemoryStream(contents, writable: false);

            return MimeMessage.Load(buffer);
        }
        catch (Exception failure) when (failure is FormatException or IOException)
        {
            throw new SyntheticMailFailure($"'{name}' is not a mail message: {failure.Message}", failure);
        }
    }

    private static DateTimeOffset NewestDateIn(IReadOnlyList<SyntheticConversation> exchanges) =>
        exchanges.SelectMany(exchange => exchange.Messages).Max(message => message.SentAt);

    /// <summary>The line ending an exported message is written with, which is the one RFC 5322 states.</summary>
    private static FormatOptions DosLineEndings { get; } = BuildDosLineEndings();

    private static FormatOptions BuildDosLineEndings()
    {
        var options = FormatOptions.Default.Clone();

        options.NewLineFormat = NewLineFormat.Dos;

        return options;
    }
}
