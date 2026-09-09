// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation;

/// <summary>Names the markup a client in the wild would have produced for an AI-generated message.</summary>
/// <remarks>
/// <para>
/// A closed enumeration on exactly the terms <see cref="SyntheticMailTopic" /> is one: what a dialect is, is the
/// list of constructs the generation prompt hands to the model, so the name and that list are one value rather than
/// a name here and a lookup table somewhere else.
/// </para>
/// <para>
/// The set exists because a corpus of well-formed markup proves only that the readers cope with markup somebody
/// asked for politely. A message body reaching this repository is read three times over — the tokenizer that derives
/// searchable text, the sanitizer, and the document model the reading pane draws — and each of those meets whatever
/// the sending client emitted. The five members below are what actually arrives: Word's HTML, a campaign built out
/// of nested layout tables, the two web and desktop composers whose quoting conventions every reply carries, and a
/// sender whose markup is not well-formed at all.
/// </para>
/// <para>
/// Each description asks for a compact document rather than a full client export. What exercises a reader is meeting
/// the construct at all, and a hundred lines of <c>mso-</c> declarations would buy nothing but the output ceiling
/// one generation is bounded by.
/// </para>
/// <para>
/// Nothing parses one of these from a command line — the value is drawn from the seed and never named by an
/// invocation — so there is no <c>TryParse</c> beside the members, no name to parse against, and nothing that
/// serializes one. What a listing prints is <see cref="ToString" />. Being a struct, <see langword="default" /> is
/// reachable and is not a dialect; the generator draws only from <see cref="All" />, so the throw below answers a
/// value nothing here produces rather than one a caller has to guard against.
/// </para>
/// </remarks>
internal readonly record struct SyntheticMarkupDialect
{
    private readonly string? name;
    private readonly string? promptDescription;

    private SyntheticMarkupDialect(string name, string promptDescription)
    {
        this.name = name;
        this.promptDescription = promptDescription;
    }

    /// <summary>Gets the markup desktop Outlook produces, which the Word engine writes and most business mail carries.</summary>
    public static SyntheticMarkupDialect WordOutlook { get; } = new(
        "word-outlook",
        """
        the HTML desktop Outlook produces, which the Microsoft Word engine writes. Open the document with an <html> element declaring the xmlns:v, xmlns:o and xmlns:w namespaces; put a <!--[if !mso]><!--> block and a <!--[if gte mso 9]><xml><o:OfficeDocumentSettings/></xml><![endif]--> block in the head beside a <style> block declaring a handful of mso- properties such as mso-style-type, mso-pagination, mso-fareast-font-family and mso-line-height-rule; give every paragraph class="MsoNormal" and end it with <o:p>&nbsp;</o:p>; wrap every run of words in a <span style="font-size:11.0pt;font-family:&quot;Calibri&quot;,sans-serif;color:#1F497D">; use a nested table with valign, cellpadding="0" and cellspacing="0" for anything laid out; separate paragraphs with a <p class="MsoNormal"><o:p>&nbsp;</o:p></p> rather than with spacing; and put the quoted history behind a <div style="border:none;border-top:solid #E1E1E1 1.0pt;padding:3.0pt 0cm 0cm 0cm"> holding <b>From:</b>, <b>Sent:</b>, <b>To:</b> and <b>Subject:</b> lines
        """);

    /// <summary>Gets the markup a marketing campaign carries when a template engine built it out of nested tables.</summary>
    public static SyntheticMarkupDialect CampaignTemplate { get; } = new(
        "campaign-template",
        """
        the HTML a marketing campaign carries when a sending platform built it from a template. Lay the whole message out in three or four levels of nested <table> with border="0", cellpadding="0", cellspacing="0", role="presentation", width and bgcolor attributes and align="center"; open the body with a preheader <div style="display:none;max-height:0;overflow:hidden;mso-hide:all"> repeating the subject; use spacer rows whose only cell holds a single &nbsp; with an explicit height and a line-height of its own; write colours and sizes as <font face="Arial" size="2" color="#333333"> elements as well as inline styles; put the call to action in a table cell styled as a button rather than in a link; and close with an unsubscribe footer of tiny text and a <td align="center" style="font-size:11px;color:#999999;">
        """);

    /// <summary>Gets the markup the Gmail web composer produces, whose quoting convention every reply through it carries.</summary>
    public static SyntheticMarkupDialect GmailComposer { get; } = new(
        "gmail-composer",
        """
        the HTML the Gmail web composer produces. Wrap the whole body in <div dir="ltr"> and nest further <div> elements freely rather than using paragraphs, with <br> where a blank line belongs and <div><br></div> for an empty one; write emphasis as <b>, <i> and <span style="color:rgb(51,51,51);font-family:arial,sans-serif;font-size:small">; and where the message answers another one, append the quoted history as <div class="gmail_quote"> holding <div dir="ltr" class="gmail_attr">On Mon, 14 Apr 2025 at 09:12, Name &lt;name@example.com&gt; wrote:<br></div><blockquote class="gmail_quote" style="margin:0px 0px 0px 0.8ex;border-left:1px solid rgb(204,204,204);padding-left:1ex"> around the quoted body, which itself carries a nested quote one level deeper
        """);

    /// <summary>Gets the markup Apple Mail produces, with its own quoting and forwarding blocks.</summary>
    public static SyntheticMarkupDialect AppleMail { get; } = new(
        "apple-mail",
        """
        the HTML Apple Mail produces. Declare the document with <meta http-equiv="Content-Type" content="text/html; charset=utf-8"> and give the body style="word-wrap:break-word;-webkit-nbsp-mode:space;-webkit-line-break:after-white-space;"; write the message as <div> elements holding <br> rather than as paragraphs, and use <div class="AppleOriginalContents"> and <span style="font-family:-apple-system,Helvetica;font-size:14px;"> around runs of text; quote with <blockquote type="cite" style="margin:0 0 0 40px;border:none;padding:0px;"> nested two levels deep; and where the message forwards another one, open it with <div>Begin forwarded message:</div><br><div style="margin-top:0px;"><b>From:</b><span style="font-family:-apple-system;"> Name &lt;name@example.com&gt;</span><br></div> and the same shape again for Subject, Date and To
        """);

    /// <summary>Gets markup that is not well-formed, which is what a hand-rolled or legacy sender emits.</summary>
    /// <remarks>
    /// The one member whose description asks for defects rather than for a client's habits, and the reason the set
    /// exists at all: every reader here recovers from malformed markup rather than refusing it, and a corpus that
    /// never carried any could not show which of those recoveries is wrong.
    /// </remarks>
    public static SyntheticMarkupDialect LegacyMalformed { get; } = new(
        "legacy-malformed",
        """
        HTML that is not well-formed, of the kind a hand-rolled sender or an old system emits. There is no head and no <html> element; open straight into the content. Leave <p>, <li> and <td> elements unclosed; write at least one stray </div> or </span> that closes nothing; open <b>, <font> or <span> inside one block and close it inside the next; write some attributes without quotes, as in <table border=1 cellpadding=3 width=600>, and give one element the same attribute twice; separate paragraphs with runs of <br> and pad alignment with runs of &nbsp;; write at least one entity without its semicolon, such as &amp or &nbsp followed by a letter; use uppercase tag names such as <TABLE>, <TR> and <FONT> beside the lowercase ones; and end a table whose rows disagree on how many cells they hold, with one <tr> missing its closing tag
        """);

    /// <summary>Gets every dialect a message may be drawn in.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<SyntheticMarkupDialect> All { get; } =
    [
        WordOutlook,
        CampaignTemplate,
        GmailComposer,
        AppleMail,
        LegacyMalformed,
    ];

    /// <summary>Gets the constructs the generation prompt hands to the model.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a dialect.</exception>
    public string PromptDescription => this.promptDescription
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a markup dialect.");

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}
