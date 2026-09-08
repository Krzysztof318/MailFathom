// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ClientModel;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using MailFathom.SyntheticMail.Configuration;
using OpenAI;
using OpenAI.Chat;

namespace MailFathom.SyntheticMail.Generation.AiContent;

/// <summary>Answers content from the OpenAI endpoint one run is configured with.</summary>
/// <remarks>
/// <para>
/// The whole of the provider-specific reach: the client library, its options, its exceptions, and the key stop in
/// this type, so a message the generator builds around an answer never learns which provider answered it. The
/// construction is the one ADR 0011 records for the service — the OpenAI wire protocol, a base address, and a key —
/// built here directly because a development tool composes from its own files rather than from a service's
/// dependency injection, and no project under <c>backend/src/</c> is referenced.
/// </para>
/// <para>
/// The client library's own retry policy answers the transient failures, and nothing outside it retries on top:
/// one layer of bounded attempts with backoff, for the reason the repository's single-layer rule gives. A refusal
/// the retry policy will not repeat — a refused key, an unknown model — reaches the run at once, because repeating
/// it buys the same answer.
/// </para>
/// <para>
/// The prompt and the answer never reach a log. The prompt names a language, a topic, and the opening of the
/// synthetic message being answered, and the answer is message content; a log line would be a third copy of material
/// this tool exists to keep out of a developer's real mail.
/// </para>
/// </remarks>
internal sealed partial class OpenAiEmailContentSource : IAiEmailContentSource
{
    /// <summary>The most a single email's content may cost in output tokens.</summary>
    /// <remarks>
    /// A ceiling rather than an expectation: the bound is what keeps one stuck generation from pricing a whole batch
    /// like a document. It has to carry the message twice over, because an answer holds the body as text and the same
    /// content again as the markup real mail carries, and a message in a language whose words cost more tokens than
    /// English pays that twice as well. Measured against real answers, a rich HTML message runs to about two thousand
    /// output tokens, so the earlier bound cut one in the middle — and a cut answer arrives as an incomplete JSON
    /// object, which reads as a model that cannot write the answer rather than as a ceiling that stopped it. A
    /// message written as a real client's markup costs more again, a dialect that wraps every run of words in a span
    /// most of all, which is why every description in <see cref="SyntheticMarkupDialect" /> asks for a compact
    /// document rather than a full client export.
    /// </remarks>
    private const int MaximumOutputTokens = 6000;

    /// <summary>The longest attached file this source will ask for, whatever size the seed drew.</summary>
    /// <remarks>
    /// The attachment shares the output ceiling above with the message written twice over, so a batch generated with a
    /// megabyte attachment ceiling would ask for a file no answer could hold and every message would come back cut off
    /// at the ceiling — which reads as a model that cannot write the answer rather than as a size nobody could have
    /// meant. A few thousand characters is a plausible spreadsheet or note and leaves the message its share. A run
    /// wanting larger files wants them filled with drawn bytes, which is what the default mode does.
    /// </remarks>
    private const int MostAttachmentCharacters = 4000;

    /// <summary>The shortest file worth asking for, below which the part is left as the bytes the seed draws.</summary>
    /// <remarks>
    /// An attachment's size is drawn anywhere between one byte and the batch's ceiling, so a corpus generated with a
    /// four-kilobyte ceiling draws plenty of files of a few dozen bytes. A file that small holds no row anybody could
    /// search for and costs a request to write, and asking for one anyway would either produce a truncated line or
    /// break the ceiling the invocation named. Below this the drawn printable bytes are what the part carries, which
    /// is what every text attachment carried before this source wrote any of them.
    /// </remarks>
    private const int LeastAttachmentCharacters = 200;

    /// <summary>The constructs an answered document is refused for carrying.</summary>
    /// <remarks>
    /// The endpoint is one a developer named rather than one this tool chose, and what comes back is delivered to a
    /// real mailbox — so an answer is checked for the constructs that execute rather than render. Refused rather than
    /// stripped: a reduction would leave a message nobody asked for and hide that the endpoint answered with
    /// something it was told not to, and a run that stops names the move for a developer who can change the model.
    /// The check is a substring scan because these are the literal spellings a document has to contain to carry them
    /// at all; markup that merely mentions one in text is escaped and does not. It is only half the refusal:
    /// <see cref="InlineEventHandler" /> answers the constructs an attribute name carries, which no fixed list of
    /// element spellings reaches.
    /// </remarks>
    private static readonly string[] RefusedHtmlConstructs =
    [
        "<script",
        "<iframe",
        "<object",
        "<embed",
        "javascript:",
    ];

    /// <summary>Matches an inline event handler, which executes exactly as a script element does.</summary>
    /// <remarks>
    /// A pattern rather than another entry in the list above, because a handler is spelled as an attribute name and
    /// there are dozens of them: <c>onerror</c> on an image that fails to load runs without the reader touching
    /// anything, so a list that named a few would refuse the ones somebody thought of and deliver the rest. What it
    /// matches is what a document has to contain to carry one at all — a separator, <c>on</c>, a name, and an
    /// assignment — and it cannot backtrack, so no answer can price the check.
    /// </remarks>
    [GeneratedRegex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineEventHandler();

    /// <summary>How long one generation may take before the run reports it as timed out.</summary>
    /// <remarks>
    /// An email is a small generation, so two minutes is the line between slow and stuck. Applied to the client
    /// rather than left at the library's default, so the bound this tool promises is the one it holds.
    /// </remarks>
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(2);

    private readonly AiProviderConfiguration configuration;
    private readonly ChatClient chatClient;

    /// <summary>Initializes a source over the endpoint, model, and key one run was configured with.</summary>
    /// <param name="configuration">The checked provider configuration the source reaches through.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    public OpenAiEmailContentSource(AiProviderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new OpenAIClientOptions
        {
            NetworkTimeout = NetworkTimeout,
        };

        if (configuration.Endpoint is { } endpoint)
        {
            options.Endpoint = endpoint;
        }

        this.configuration = configuration;
        var client = new OpenAIClient(new ApiKeyCredential(configuration.ApiKey), options);
        this.chatClient = client.GetChatClient(configuration.Model);
    }

    /// <inheritdoc />
    public async Task<AiEmailContent> GenerateAsync(AiEmailContentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var attachmentBound = AttachmentBoundOf(request);
        var text = await this.RequestContentAsync(request, attachmentBound, cancellationToken);

        return ParseContent(text, attachmentBound);
    }

    /// <summary>Resolves how long a file this request may ask for, and nothing when it asks for none.</summary>
    /// <param name="request">What the message is being written from.</param>
    /// <returns>The longest file to ask for, and zero to ask for none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    internal static int AttachmentBoundOf(AiEmailContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.AttachmentFileName is null || request.AttachmentCharacterBound < LeastAttachmentCharacters
            ? 0
            : Math.Min(request.AttachmentCharacterBound, MostAttachmentCharacters);
    }

    /// <summary>Sends one request and returns exactly what the provider answered, in text.</summary>
    private async Task<string> RequestContentAsync(
        AiEmailContentRequest request,
        int attachmentBound,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await this.chatClient.CompleteChatAsync(
                BuildMessages(request, attachmentBound),
                BuildOptions(),
                cancellationToken);
            var completion = result.Value;

            // A refusal is the model declining rather than the call failing, so it is reported as its own line
            // instead of being read as a transport problem nobody can fix by waiting.
            if (completion.Refusal is { Length: > 0 })
            {
                throw new SyntheticMailFailure("The model refused to write the message. Retry, or name a different topic or language.");
            }

            // A truncated answer is an incomplete JSON object, and reporting it as a malformed one sends a developer
            // to the model when the ceiling above is what cut it: the same prompt reaches it again on every retry.
            if (completion.FinishReason == ChatFinishReason.Length)
            {
                throw new SyntheticMailFailure(
                    $"The model's answer was cut off at the {MaximumOutputTokens}-token output ceiling, so it is not a complete message. Retry, or name a model that writes a shorter one.");
            }

            var text = string.Concat(completion.Content.Select(part => part.Text));

            return string.IsNullOrWhiteSpace(text)
                ? throw new SyntheticMailFailure("The model answered with no text. Retry, or name a different model in the AI provider file.")
                : text;
        }
        catch (Exception failure) when (failure is ClientResultException or HttpRequestException or TimeoutException or SocketException or IOException)
        {
            throw ToFailure(failure, this.configuration);
        }
    }

    /// <summary>Turns whatever the provider raised into the one line a developer can act on.</summary>
    /// <param name="failure">The failure the call produced.</param>
    /// <param name="configuration">What the failures name: the model, and where the run reached for it.</param>
    /// <returns>The failure, with a message naming the move.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The verdict is read from the failure's type and its HTTP status alone, for the reason the service's provider
    /// classification gives: a provider error body quotes the request, and the request here is a prompt. Nothing from
    /// the body is carried into the failure this produces.
    /// </remarks>
    internal static SyntheticMailFailure ToFailure(Exception failure, AiProviderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(configuration);

        var message = failure switch
        {
            ClientResultException refusal => RefusalMessage(refusal.Status, configuration.Model),
            HttpRequestException => "The endpoint could not be reached: check 'endpoint' in the AI provider file and the network.",
            TimeoutException => "The endpoint timed out answering: try again, or name a faster model in the AI provider file.",
            SocketException or IOException => "The endpoint could not be reached: check 'endpoint' in the AI provider file and the network.",
            _ => $"The call to the endpoint failed: {failure.Message}",
        };

        return new SyntheticMailFailure(message, failure);
    }

    /// <summary>Reads a provider refusal from its status alone, which is the whole of the evidence.</summary>
    private static string RefusalMessage(int status, string model) => status switch
    {
        0 => "The endpoint could not be reached: check 'endpoint' in the AI provider file and the network.",
        401 or 403 => "The endpoint refused the API key: check 'apiKey' in the AI provider file, and that the key may use the named model.",
        404 => $"The endpoint does not serve model '{model}': check 'model' and 'endpoint' in the AI provider file.",
        429 => "The endpoint is rate-limiting the key: wait, or use a key with a higher limit.",
        408 or 504 => "The endpoint timed out answering: try again, or name a faster model in the AI provider file.",
        _ when status >= 500 => "The endpoint failed the request: the provider is failing its own requests, so try again.",
        _ => $"The endpoint refused the request (HTTP {status}).",
    };

    /// <summary>Reads the model's JSON answer into the content it carries, refusing an answer that is not one.</summary>
    /// <param name="text">The answer, as the provider sent it.</param>
    /// <param name="attachmentBound">The longest attached file the request asked for, and zero when it asked for none.</param>
    /// <returns>The subject, the body, and the attached file when one was asked for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when the answer is not the JSON object it was asked for, or carries nothing in it.</exception>
    internal static AiEmailContent ParseContent(string text, int attachmentBound = 0)
    {
        ArgumentNullException.ThrowIfNull(text);

        AiEmailContent? content;

        try
        {
            content = JsonSerializer.Deserialize(JsonObjectIn(text), SyntheticMailJsonContext.Default.AiEmailContent);
        }
        catch (JsonException)
        {
            throw new SyntheticMailFailure(
                "The model's answer was not the JSON object it was asked for. Retry; if it persists, name a different model in the AI provider file.");
        }

        // The endpoint is one a developer named rather than one the tool chose, and a named endpoint is not a trusted
        // one: a line break in the subject would end the composed header early and turn what follows into headers the
        // model never signed. The service reduces a stored subject the same way where it composes a reply, for the
        // same reason, and a body keeps the whitespace that is its structure and nothing else of the control set.
        var subject = ReadableSubject(content?.Subject);
        var body = ReadableBody(content?.Body);
        var html = ReadableBody(content?.Html);

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(html))
        {
            throw new SyntheticMailFailure(
                "The model's answer carried no subject, body, or html. Retry; if it persists, name a different model in the AI provider file.");
        }

        var refused = RefusedHtmlConstructs.FirstOrDefault(
            construct => html.Contains(construct, StringComparison.OrdinalIgnoreCase));

        if (refused is not null)
        {
            throw new SyntheticMailFailure(
                $"The model's answer carried '{refused}' in its html, which this tool will not deliver to a mailbox. Retry; if it persists, name a different model in the AI provider file.");
        }

        if (InlineEventHandler().IsMatch(html))
        {
            throw new SyntheticMailFailure(
                "The model's answer carried an inline event handler in its html, which this tool will not deliver to a mailbox. Retry; if it persists, name a different model in the AI provider file.");
        }

        return new AiEmailContent(subject, body, html, ReadableAttachment(content?.Attachment, attachmentBound));
    }

    /// <summary>Reduces the answered file to what the attachment may carry, and refuses an answer that omitted it.</summary>
    /// <remarks>
    /// Refused rather than delivered without the file, on the same terms as a missing body: the envelope the seed drew
    /// says this message encloses one, and a corpus quietly missing the attachments it reports is one whose listing
    /// lies. The bound is enforced here as well as asked for, because what a model was told to write and what it wrote
    /// are two things. The cut is taken at the last line ending inside the bound so a file ends on a whole row, and at
    /// the last space where an answer holds no line ending inside it — an unbroken paragraph in a <c>.txt</c> note has
    /// no row to end on, and ending it on a whole word is the most a cut can preserve there.
    /// </remarks>
    private static string? ReadableAttachment(string? attachment, int bound)
    {
        if (bound <= 0)
        {
            return null;
        }

        var contents = ReadableBody(attachment);

        if (contents.Length == 0)
        {
            throw new SyntheticMailFailure(
                "The model's answer carried no attachment, which the message it was asked for encloses. Retry; if it persists, name a different model in the AI provider file.");
        }

        if (contents.Length <= bound)
        {
            return contents;
        }

        var row = contents.LastIndexOf('\n', bound - 1);

        if (row > 0)
        {
            return contents[..row];
        }

        var word = contents.LastIndexOf(' ', bound - 1);

        return word > 0 ? contents[..word] : contents[..bound];
    }

    /// <summary>Reduces an answer to the JSON object in it, which is the whole of it unless the model wrapped one.</summary>
    /// <remarks>
    /// The request asks for a single JSON object and states the response format, and a model that honours both answers
    /// with one and nothing else. An endpoint a developer named is not required to: a fenced code block or a sentence
    /// in front of the object is the common way one is spelled instead, and refusing that reports a formatting habit
    /// as a model that cannot write the answer. An answer carrying no object at all is left as it was, so what is
    /// refused is refused for the reason it deserves.
    /// </remarks>
    private static string JsonObjectIn(string text)
    {
        var opening = text.IndexOf('{');
        var closing = text.LastIndexOf('}');

        return opening >= 0 && closing > opening ? text[opening..(closing + 1)] : text;
    }

    /// <summary>Reduces the answered subject to what a composed header can carry.</summary>
    private static string ReadableSubject(string? subject) =>
        string.IsNullOrWhiteSpace(subject)
            ? string.Empty
            : new string([.. subject.Where(static character => !char.IsControl(character))]).Trim();

    /// <summary>Reduces the answered body to what a MIME text part can carry.</summary>
    private static string ReadableBody(string? body) =>
        string.IsNullOrWhiteSpace(body)
            ? string.Empty
            : new string([.. body.Where(static character => !char.IsControl(character) || char.IsWhiteSpace(character))]).Trim();

    private static List<ChatMessage> BuildMessages(AiEmailContentRequest request, int attachmentBound)
    {
        var user = new List<string>
        {
            $"Language: {request.LanguageCode}",
            $"Topic: {request.Topic.PromptDescription}",
            $"Markup: {request.MarkupDialect.PromptDescription}",
            $"Write as {request.AuthorName}.",
        };

        if (request.AttachmentFileName is { } fileName && attachmentBound > 0)
        {
            user.Add(
                $"This message encloses a file named \"{fileName}\". Add an \"attachment\" key holding its contents as text, at most {attachmentBound} characters, written in the format its extension names and saying what the message says it encloses. Refer to it in the body the way somebody attaching a file does.");
        }

        if (request.ParentSubject is { } subject)
        {
            user.Add(
                $"This message replies in a thread whose subject is \"{subject}\"; write the body as a reply that continues that conversation, not as a new one.");
        }

        if (request.ParentOpening is { } opening)
        {
            user.Add($"The message being replied to opens as follows. Answer what it actually says.\n{opening}");
        }

        return
        [
            ChatMessage.CreateSystemMessage(SystemPrompt),
            ChatMessage.CreateUserMessage(string.Join("\n", user)),
        ];
    }

    private static ChatCompletionOptions BuildOptions() => new()
    {
        ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
        MaxOutputTokenCount = MaximumOutputTokens,
    };

    /// <summary>The fixed contract every generation is asked under.</summary>
    private const string SystemPrompt = """
        You write the content of one realistic email for a synthetic test corpus. Answer with a single JSON object and nothing else, carrying these three keys, and a fourth only when the request asks for an enclosed file.

        "subject": one subject line.

        "body": the message as plain text, with paragraphs separated by blank lines, opening with a natural greeting and closing with a short signature.

        "html": the same message as an HTML document, written as the markup the request's "Markup" line describes. That line names the constructs a real mail client of one kind emits, and several of them are deprecated, proprietary, redundant, or not well-formed. Write them as described rather than tidying them into clean semantic markup: a well-formed document is the one thing this answer must not be, because the corpus exists to be read by software that has to survive what real senders emit. Keep it compact — a few of each named construct is what makes the document that dialect, and a full client export would be cut off by the answer's own length. Say the same things the plain-text body says, in the same order and in the same language. Never include a script, an iframe, an object, an embed, a javascript: URL, an inline event-handler attribute such as onclick or onerror, a remote image, or a tracking pixel; nothing the "Markup" line asks for overrides this sentence.

        "attachment": present only when the request names an enclosed file, holding that file's contents as text. Write it in the format the file's extension names — a .csv as a header row and data rows, a .txt as the notes or the log the name describes — and make it say what the message says it encloses, with the same invented names, figures, and dates. Nothing else goes in this key: no explanation, no code fence, no repeat of the body.

        The email is fiction: every name, company, number, and reference in it is invented, and it must not contain any real personal data, any credential, or anything that identifies a real person or organization. Write exclusively in the language the request names.
        """;
}
