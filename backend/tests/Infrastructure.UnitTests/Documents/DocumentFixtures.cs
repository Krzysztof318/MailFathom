// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace MailFathom.Infrastructure.UnitTests.Documents;

/// <summary>Composes the documents these tests read, so no binary fixture is committed and every case is readable.</summary>
/// <remarks>
/// The adversarial packages are built here rather than checked in for the reason the ordinary ones are: a zip bomb, a
/// document type declaration reaching for an external entity, and an element tree nested past a walk's depth are each
/// one line of intent, and a reviewer can see what the test is claiming without opening a file in a hex editor.
/// </remarks>
internal static class DocumentFixtures
{
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private const string OfficeNamespace = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string OpenDocumentTextNamespace = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string OpenDocumentTableNamespace = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string OpenDocumentDrawingNamespace = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private const string ManifestNamespace = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    /// <summary>Builds a PDF whose pages carry the lines given for each of them.</summary>
    /// <param name="pages">One entry per page, each the line that page carries, or empty for a page with no text at all.</param>
    /// <returns>The document's octets.</returns>
    public static byte[] Pdf(params string[] pages)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var line in pages)
        {
            var page = builder.AddPage(PageSize.A4);

            if (line.Length > 0)
            {
                page.AddText(line, 12, new PdfPoint(20, page.PageSize.Top - 40), font);
            }
        }

        return builder.Build();
    }

    /// <summary>Builds a word-processing package whose body carries the paragraphs given.</summary>
    /// <param name="paragraphs">The paragraphs, in order.</param>
    /// <returns>The package's octets.</returns>
    public static byte[] WordDocument(params string[] paragraphs)
    {
        var body = string.Concat(paragraphs.Select(paragraph =>
            $"<w:p><w:r><w:t>{Escaped(paragraph)}</w:t></w:r></w:p>"));

        return Package(("word/document.xml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <w:document xmlns:w="{WordprocessingNamespace}"><w:body>{body}</w:body></w:document>
            """));
    }

    /// <summary>Builds a word-processing package holding one part whose markup is given verbatim.</summary>
    /// <param name="documentXml">The markup <c>word/document.xml</c> carries.</param>
    /// <returns>The package's octets.</returns>
    public static byte[] WordDocumentPart(string documentXml) => Package(("word/document.xml", documentXml));

    /// <summary>Builds a presentation package whose slides carry the lines given for each of them.</summary>
    /// <param name="slides">One entry per slide, each the line that slide carries, or empty for a slide with no text.</param>
    /// <returns>The package's octets.</returns>
    public static byte[] Presentation(params string[] slides) => Package(
        [
            .. slides.Select((line, index) => (
                $"ppt/slides/slide{(index + 1).ToString(CultureInfo.InvariantCulture)}.xml",
                $"""
                 <?xml version="1.0" encoding="UTF-8"?>
                 <sld xmlns:a="{DrawingNamespace}"><a:p>{(line.Length == 0 ? string.Empty : $"<a:r><a:t>{Escaped(line)}</a:t></a:r>")}</a:p></sld>
                 """)),
        ]);

    /// <summary>Builds a workbook whose sheets hold the cells given for each of them, through the shared string table.</summary>
    /// <param name="sheets">One entry per sheet, each the cell values that sheet holds in order.</param>
    /// <returns>The package's octets.</returns>
    public static byte[] Workbook(params string[][] sheets)
    {
        var sharedStrings = sheets.SelectMany(sheet => sheet).Distinct(StringComparer.Ordinal).ToList();

        var table = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <sst xmlns="{SpreadsheetNamespace}">{string.Concat(sharedStrings.Select(value => $"<si><t>{Escaped(value)}</t></si>"))}</sst>
            """;

        var parts = sheets.Select((sheet, index) => (
            $"xl/worksheets/sheet{(index + 1).ToString(CultureInfo.InvariantCulture)}.xml",
            $"""
             <?xml version="1.0" encoding="UTF-8"?>
             <worksheet xmlns="{SpreadsheetNamespace}"><sheetData><row>{string.Concat(sheet.Select(value =>
                 $"""<c t="s"><v>{sharedStrings.IndexOf(value).ToString(CultureInfo.InvariantCulture)}</v></c>"""))}</row></sheetData></worksheet>
             """));

        return Package([("xl/sharedStrings.xml", table), .. parts]);
    }

    /// <summary>Builds an OpenDocument text package whose body carries the paragraphs given.</summary>
    /// <param name="paragraphs">The paragraphs, in order.</param>
    /// <returns>The package's octets.</returns>
    public static byte[] OpenDocumentText(params string[] paragraphs) => OpenDocumentContent(
        "text",
        string.Concat(paragraphs.Select(paragraph => $"<text:p>{Escaped(paragraph)}</text:p>")));

    /// <summary>Builds an OpenDocument spreadsheet whose sheets hold the cells given for each of them.</summary>
    /// <param name="sheets">One entry per sheet, each the cell values that sheet holds in order.</param>
    /// <returns>The package's octets.</returns>
    public static byte[] OpenDocumentSpreadsheet(params string[][] sheets) => OpenDocumentContent(
        "spreadsheet",
        string.Concat(sheets.Select(sheet => $"""
            <table:table><table:table-row>{string.Concat(sheet.Select(value =>
                $"<table:table-cell><text:p>{Escaped(value)}</text:p></table:table-cell>"))}</table:table-row></table:table>
            """)));

    /// <summary>Builds an OpenDocument presentation whose pages carry the lines given for each of them.</summary>
    /// <param name="pages">One entry per page, each the line that page carries, or empty for a page with no text.</param>
    /// <returns>The package's octets.</returns>
    public static byte[] OpenDocumentPresentation(params string[] pages) => OpenDocumentContent(
        "presentation",
        string.Concat(pages.Select(line => $"""
            <draw:page>{(line.Length == 0
                ? string.Empty
                : $"<draw:frame><draw:text-box><text:p>{Escaped(line)}</text:p></draw:text-box></draw:frame>")}</draw:page>
            """)));

    /// <summary>Builds an OpenDocument package whose one content part holds the markup given verbatim.</summary>
    /// <param name="contentXml">The markup <c>content.xml</c> carries.</param>
    /// <returns>The package's octets.</returns>
    public static byte[] OpenDocumentContentPart(string contentXml) => Package(("content.xml", contentXml));

    /// <summary>Builds an archive of the named parts, each holding the text given for it.</summary>
    /// <param name="parts">The parts, named as they are inside the package.</param>
    /// <returns>The archive's octets.</returns>
    public static byte[] Package(params (string Name, string Content)[] parts)
    {
        using var written = new MemoryStream();

        using (var archive = new ZipArchive(written, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);

                writer.Write(content);
            }
        }

        return written.ToArray();
    }

    /// <summary>Nests an element tree the given number of levels deep inside a word-processing document.</summary>
    /// <param name="depth">How many levels to nest.</param>
    /// <returns>The package's octets.</returns>
    public static byte[] DeeplyNestedWordDocument(int depth)
    {
        var opened = string.Concat(Enumerable.Repeat("<w:tbl><w:tr><w:tc>", depth));
        var closed = string.Concat(Enumerable.Repeat("</w:tc></w:tr></w:tbl>", depth));

        return WordDocumentPart($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <w:document xmlns:w="{WordprocessingNamespace}"><w:body>{opened}<w:p><w:r><w:t>deep</w:t></w:r></w:p>{closed}</w:body></w:document>
            """);
    }

    /// <summary>Builds a word-processing package whose one part inflates to the given number of characters.</summary>
    /// <param name="characters">How many characters the part's single run holds.</param>
    /// <returns>The package's octets.</returns>
    /// <remarks>
    /// The run is one repeated character, which is what makes the part compress to a fraction of a percent of what it
    /// inflates to — the ordinary construction of a decompression bomb rather than a contrived one.
    /// </remarks>
    public static byte[] InflatingWordDocument(int characters) => WordDocumentPart($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <w:document xmlns:w="{WordprocessingNamespace}"><w:body><w:p><w:r><w:t>{new string('a', characters)}</w:t></w:r></w:p></w:body></w:document>
        """);

    /// <summary>Builds a word-processing package whose part declares a document type reaching for an external file.</summary>
    /// <param name="externalPath">The path the declared entity names.</param>
    /// <returns>The package's octets.</returns>
    public static byte[] WordDocumentDeclaringAnExternalEntity(string externalPath) => WordDocumentPart($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE w:document [<!ENTITY stolen SYSTEM "file://{externalPath}">]>
        <w:document xmlns:w="{WordprocessingNamespace}"><w:body><w:p><w:r><w:t>&stolen;</w:t></w:r></w:p></w:body></w:document>
        """);

    /// <summary>Builds octets an office package was expected in that are an OLE compound file instead.</summary>
    /// <returns>The document's octets.</returns>
    /// <remarks>
    /// What a password-protected Office package actually is: the package encrypted whole and wrapped in a compound
    /// file. Only the eight-octet signature matters to the code under test, so the rest is filler rather than a
    /// compound file anything could open — building a real one would prove nothing further and commit a binary fixture.
    /// </remarks>
    public static byte[] EncryptedOfficePackage() =>
        [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, .. Enumerable.Repeat((byte)0, 512)];

    /// <summary>Builds an OpenDocument package whose manifest declares its parts encrypted.</summary>
    /// <returns>The package's octets.</returns>
    /// <remarks>
    /// A locked OpenDocument file stays an ordinary archive and encrypts the parts inside it, so the content part is
    /// ciphertext and the manifest is the only place the package says so. The content part here is arbitrary octets for
    /// exactly that reason: a reader that missed the manifest would reach it and report a broken document.
    /// </remarks>
    public static byte[] EncryptedOpenDocument() => Package(
        ("META-INF/manifest.xml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <manifest:manifest xmlns:manifest="{ManifestNamespace}">
              <manifest:file-entry manifest:full-path="content.xml">
                <manifest:encryption-data manifest:checksum-type="SHA1/1K" manifest:checksum="Zm9v" />
              </manifest:file-entry>
            </manifest:manifest>
            """),
        ("content.xml", "not xml, because this part is ciphertext"));

    /// <summary>Builds a word-processing package whose part overstates what it compressed to.</summary>
    /// <param name="characters">How many characters the part's single run holds.</param>
    /// <returns>The package's octets.</returns>
    /// <remarks>
    /// The compressed length is a field in the archive's own directory rather than a fact about the data, so a sender
    /// can write whatever widens the per-part ratio denominator. What they cannot write is a length past the end of the
    /// file: <c>ZipArchive</c> refuses that outright with <c>A local file header is corrupt</c>, measured on .NET 10 on
    /// 2026-09-05. So the overstatement here is the largest one a reader will actually accept — just inside the
    /// archive — which is the case the ratio bound has to hold against.
    /// </remarks>
    public static byte[] WordDocumentOverstatingItsCompressedLength(int characters)
    {
        var package = InflatingWordDocument(characters);
        var declared = (uint)Math.Max(0, package.Length - 200);

        foreach (var header in CompressedSizeFieldOffsets(package))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(package.AsSpan(header, sizeof(uint)), declared);
        }

        return package;
    }

    /// <summary>Builds a PDF encrypted under the standard security handler, with a password nothing here holds.</summary>
    /// <returns>The document's octets.</returns>
    /// <remarks>
    /// Assembled by hand because nothing in this repository writes an encrypted PDF and no fixture file is committed.
    /// The owner and user entries are arbitrary: a reader computes what they would have been for the empty password,
    /// finds neither, and reports a document it cannot decrypt — which is exactly the case a mailbox owner meets when
    /// somebody sends them a password-protected contract.
    /// </remarks>
    public static byte[] EncryptedPdf()
    {
        string[] objects =
        [
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n",
            $"4 0 obj\n<< /Filter /Standard /V 1 /R 2 /O <{new string('1', 64)}> /U <{new string('2', 64)}> /P -1 >>\nendobj\n",
        ];

        var document = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        foreach (var declared in objects)
        {
            offsets.Add(document.Length);
            document.Append(declared);
        }

        var startOfCrossReferences = document.Length;

        document.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");

        foreach (var offset in offsets)
        {
            document.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        document.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R /Encrypt 4 0 R /ID [<{new string('3', 32)}> <{new string('4', 32)}>] >>\nstartxref\n{startOfCrossReferences}\n%%EOF\n");

        return Encoding.ASCII.GetBytes(document.ToString());
    }

    /// <summary>Finds where each central-directory record states the compressed length of its part.</summary>
    /// <remarks>
    /// The central directory is what a reader takes <c>CompressedLength</c> from, so it is the field a sender would
    /// overstate and the only one this rewrites. Its records open with <c>PK\x01\x02</c> and carry that length twenty
    /// octets in.
    /// </remarks>
    private static List<int> CompressedSizeFieldOffsets(byte[] package)
    {
        byte[] centralDirectoryRecord = [0x50, 0x4B, 0x01, 0x02];
        var offsets = new List<int>();

        for (var offset = 0; offset + 24 <= package.Length; offset++)
        {
            if (package.AsSpan(offset, centralDirectoryRecord.Length).SequenceEqual(centralDirectoryRecord))
            {
                offsets.Add(offset + 20);
            }
        }

        return offsets;
    }

    /// <summary>Wraps a body in the one content part an OpenDocument package holds everything in.</summary>
    private static byte[] OpenDocumentContent(string bodyElement, string body) => OpenDocumentContentPart($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content xmlns:office="{OfficeNamespace}" xmlns:text="{OpenDocumentTextNamespace}" xmlns:table="{OpenDocumentTableNamespace}" xmlns:draw="{OpenDocumentDrawingNamespace}">
          <office:body><office:{bodyElement}>{body}</office:{bodyElement}></office:body>
        </office:document-content>
        """);

    private static string Escaped(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
