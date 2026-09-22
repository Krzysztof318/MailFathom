// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Globalization;
using System.Security;
using System.Text;
using MailFathom.AI.Retrieval;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.AgentConversations;

/// <summary>What one Agent run may do besides answer: read the person's mail, calendar, and tasks, and propose what to do about them.</summary>
/// <remarks>
/// <para>
/// <strong>The tool set is the capability, and it splits in two.</strong> A reading tool answers on the model's own turn,
/// because it discloses nothing the person could not open themselves and costs nothing beyond the run. An act tool never
/// acts: it writes a proposal into the conversation and tells the model so, and only the person accepting that proposal
/// carries it out — through <see cref="AgentProposalAcceptance" />, with exactly the act written here. There is no tool
/// that sends, saves, schedules, or completes anything, so no instruction and no retrieved message can make a run do so.
/// </para>
/// <para>
/// <strong>Each tool reaches only what the person's own grant reaches.</strong> Every read goes through the use case the
/// client's own routes use, so the scope, the permissions, and the egress guard are the ones a person opening the same
/// screen meets; a tool whose grant the person lacks is not offered at all rather than offered and refused.
/// </para>
/// <para>
/// <strong>Citations are minted here, never by the model.</strong> Every message a tool shows is declared as a source the
/// moment it is shown, so what the answer rests on is what the run actually read, and nothing a model writes becomes a
/// citation target.
/// </para>
/// <para>
/// <strong>A derivation already made is shown as it stands.</strong> The state of a conversation is written into the
/// conversation as the block the mail screen draws, in the language it was derived in, rather than handed to the model to
/// restate — so the same thread reads the same way in both places.
/// </para>
/// </remarks>
internal sealed class AgentConversationTools
{
    /// <summary>The most messages of one conversation a thread reading shows the model.</summary>
    internal const int MaximumThreadMessages = 20;

    /// <summary>The most characters of one message's body a thread reading shows the model.</summary>
    internal const int MaximumBodyCharacters = 4_000;

    /// <summary>The most calendar events or tasks one reading shows the model.</summary>
    internal const int MaximumAgendaItems = 50;

    /// <summary>The longest window of the calendar one reading covers.</summary>
    internal static readonly TimeSpan MaximumCalendarWindow = TimeSpan.FromDays(62);

    private readonly AgentAnswerJournal journal;
    private readonly ScopedMailKnowledgeRetrieval retrieval;
    private readonly AgentConversationReaders readers;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IReadOnlyList<MailAccountId> accounts;
    private readonly Dictionary<StoredEmailId, PresentationCitation> cited = [];

    /// <summary>Initializes the tools of one run.</summary>
    /// <param name="journal">Where every status, block, and proposal is written the moment it exists.</param>
    /// <param name="retrieval">The mail this run may search, already bound to the person's scope.</param>
    /// <param name="readers">The use cases every tool reads and proposes through, under the person's own grant.</param>
    /// <param name="egressGuard">Scans every text a tool hands the model.</param>
    /// <param name="accounts">The accounts the person reads, which are the ones a new message may be sent from.</param>
    internal AgentConversationTools(
        AgentAnswerJournal journal,
        ScopedMailKnowledgeRetrieval retrieval,
        AgentConversationReaders readers,
        SensitiveContentEgressGuard egressGuard,
        IReadOnlyList<MailAccountId> accounts)
    {
        this.journal = journal;
        this.retrieval = retrieval;
        this.readers = readers;
        this.egressGuard = egressGuard;
        this.accounts = accounts;
    }

    /// <summary>Gets every source the run has shown the model, in the order it was first shown.</summary>
    internal IReadOnlyList<PresentationCitation> Cited => [.. this.cited.Values];

    /// <summary>Gets whether a proposal is on record from this run, which no later model may be asked to answer beside.</summary>
    internal bool HasProposed { get; private set; }

    /// <summary>Creates the tools the person's grant allows.</summary>
    /// <returns>The reading tools where the grant reads mail, and the proposing tools where it also drafts and sends.</returns>
    internal IReadOnlyList<AITool> Create()
    {
        List<AITool> tools = [];

        if (this.readers.Authorization.Permits(MailFathomPermission.MailRead))
        {
            tools.Add(new ReportingFunction(this.retrieval.CreateSearchTool(), this.journal, AgentActivity.SearchingMail));
            tools.Add(AIFunctionFactory.Create(this.ReadThreadAsync, "read_thread", "Reads the whole email conversation a message belongs to: every message's sender, date, subject, and body. Pass the id of any message in it."));
            tools.Add(AIFunctionFactory.Create(this.ShowThreadStateAsync, "show_thread_state", "Shows the person where the conversation a message belongs to stands — what was agreed, what is still open, what somebody owes — exactly as it was already derived. The reading is placed in the conversation for them; do not restate it."));
            tools.Add(AIFunctionFactory.Create(this.ReadCalendarAsync, "read_calendar", "Reads the person's calendar between two instants, earliest first."));
            tools.Add(AIFunctionFactory.Create(this.ReadTasksAsync, "read_tasks", "Reads the person's task list, soonest due first, including what their mail proposed as a task."));
        }

        if (this.readers.Authorization.Permits(MailFathomPermission.MailDraftsWrite) && this.readers.Authorization.Permits(MailFathomPermission.MailSend))
        {
            tools.Add(AIFunctionFactory.Create(this.ProposeMessageAsync, "propose_message", "Proposes a new email for the person to review. Nothing is sent: the person sees the draft and decides."));

            if (this.readers.Authorization.Permits(MailFathomPermission.MailRead))
            {
                tools.Add(AIFunctionFactory.Create(this.ProposeReplyAsync, "propose_reply", "Proposes an answer to a message — a reply, a reply to all, or a forward — for the person to review. Nothing is sent: the person sees the draft and decides."));
            }
        }

        return tools;
    }

    /// <summary>Declares every message the search showed the model as a source, which is what an answer resting on a search cites.</summary>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>A task that completes once every source is declared.</returns>
    internal async Task DeclareSearchedAsync(CancellationToken cancellationToken)
    {
        foreach (var passage in this.retrieval.Report.Passages.DistinctBy(static passage => passage.StoredEmailId))
        {
            await this.CiteAsync(passage.StoredEmailId, passage.Subject, cancellationToken);
        }
    }

    [Description("Reads one email conversation.")]
    private async Task<string> ReadThreadAsync(
        [Description("The id of any message in the conversation, or of the conversation itself.")] string messageId,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.ReadingThread, cancellationToken);

        if (await this.ReadConversationAsync(messageId, cancellationToken) is not { Count: > 0 } messages)
        {
            return "No message with that id is readable here.";
        }

        var text = new StringBuilder();

        foreach (var message in messages.Take(MaximumThreadMessages))
        {
            var citation = await this.CiteAsync(message.StoredEmailId, message.Headers.Subject, cancellationToken);
            var body = message.Body.PlainText.Text;

            // Every part a message's author wrote is escaped, so no subject or body can close the element it sits in
            // and write a turn of its own after it.
            text.Append(CultureInfo.InvariantCulture, $"<message id=\"{message.StoredEmailId}\" source=\"{citation.Id.Value}\" from=\"{SecurityElement.Escape(SenderOf(message)?.Address)}\" sent=\"{message.Headers.SentAt:O}\">\n")
                .Append(CultureInfo.InvariantCulture, $"<subject>{SecurityElement.Escape(message.Headers.Subject)}</subject>\n")
                .Append(SecurityElement.Escape(body.Length <= MaximumBodyCharacters ? body : MailTextBounds.TruncateAtTextElementBoundary(body, MaximumBodyCharacters)))
                .Append("\n</message>\n");
        }

        return await this.egressGuard.GuardAsync(SensitiveContentEgressPoint.ChatPrompt, text.ToString(), cancellationToken);
    }

    [Description("Shows the person the state of one conversation.")]
    private async Task<string> ShowThreadStateAsync(
        [Description("The id of any message in the conversation, or of the conversation itself.")] string messageId,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.ReadingThread, cancellationToken);

        if (await this.ReadConversationAsync(messageId, cancellationToken) is not { Count: > 0 } messages
            || messages[0].Thread is not { } thread
            || await this.readers.StateBrowser.ReadStateAsync(thread.ThreadId, cancellationToken) is not { } state
            || await this.ThreadStateBlockOfAsync(state, messages, cancellationToken) is not { } block)
        {
            return "This conversation has no reading yet. Answer from its messages instead.";
        }

        await this.WriteAsync(this.journal.ComposeAsync(block, cancellationToken));

        return "The person now sees this conversation's state as it was derived. Do not restate or translate it; refer to it.";
    }

    [Description("Reads the person's calendar.")]
    private async Task<string> ReadCalendarAsync(
        [Description("Where the window opens, as an ISO 8601 instant with an offset.")] string from,
        [Description("Where the window closes, as an ISO 8601 instant with an offset.")] string until,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.ReadingCalendar, cancellationToken);

        if (!TryParseInstant(from, out var opens)
            || !TryParseInstant(until, out var closes)
            || closes <= opens
            || closes - opens > MaximumCalendarWindow)
        {
            return $"Give two ISO 8601 instants with an offset, the second later than the first and at most {MaximumCalendarWindow.TotalDays} days after it.";
        }

        var events = await this.readers.Calendar.ReadWindowAsync(opens, closes, origin: null, MaximumAgendaItems, cancellationToken) ?? [];
        var lines = events.Select(static entry => string.Create(
            CultureInfo.InvariantCulture,
            $"- {entry.Start:O}{(entry.End is { } end ? $" – {end:O}" : string.Empty)}{(entry.IsAllDay ? " (all day)" : string.Empty)}: {entry.Title.Value}"));

        return events.Count is 0
            ? "Nothing is on the calendar in that window."
            : await this.egressGuard.GuardAsync(SensitiveContentEgressPoint.ChatPrompt, string.Join('\n', lines), cancellationToken);
    }

    /// <summary>Reads an instant only where it states its offset, so none is silently read against this server's own time zone.</summary>
    private static bool TryParseInstant(string text, out DateTimeOffset instant)
    {
        instant = default;

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stated)
            && stated.Kind is not DateTimeKind.Unspecified
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant);
    }

    [Description("Reads the person's tasks.")]
    private async Task<string> ReadTasksAsync(CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.ReadingTasks, cancellationToken);

        List<PersonalTask> read = [];

        foreach (var origin in Enum.GetValues<PersonalTaskOrigin>())
        {
            read.AddRange((await this.readers.Tasks.ReadPageAsync(origin, MaximumAgendaItems, cursor: null, cancellationToken))?.Tasks ?? []);
        }

        var lines = read.Select(static task => string.Create(
            CultureInfo.InvariantCulture,
            $"- {(task.IsCompleted ? "[done] " : string.Empty)}{(task.Origin is PersonalTaskOrigin.Proposed ? "[proposed by mail] " : string.Empty)}{task.Title}{(task.DueOn is { } due ? $" (due {due:yyyy-MM-dd})" : string.Empty)}"));

        return read.Count is 0
            ? "The task list is empty."
            : await this.egressGuard.GuardAsync(SensitiveContentEgressPoint.ChatPrompt, string.Join('\n', lines), cancellationToken);
    }

    [Description("Proposes a new email.")]
    private async Task<string> ProposeMessageAsync(
        [Description("The account it is sent from, one of the account names the turn lists.")] string account,
        [Description("Every recipient's email address.")] string[] recipients,
        [Description("The subject.")] string subject,
        [Description("The body, as plain text, in the language its recipients read.")] string body,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.PreparingProposal, cancellationToken);

        var sender = this.accounts.Where(known => string.Equals(known.Value, account, StringComparison.Ordinal)).ToArray();

        if (sender.Length is 0)
        {
            return $"Name one of these accounts: {string.Join(", ", this.accounts.Select(static known => known.Value))}.";
        }

        if (AddressesOf(recipients) is not { } addressed
            || !PresentationText.TryCreate(subject, out var titled)
            || !PresentationText.TryCreate(body, out var written))
        {
            return "Give at least one valid address, at most twenty, a subject, and a body.";
        }

        var block = new DraftBlock(PresentationEvidence.Unsupported(PresentationFreshness.Unknown), addressed, titled, written, DraftDisposition.Composed);

        await this.WriteAsync(this.journal.ProposeAsync(block, new AgentMessageSending(sender[0].Value, addressed, titled, written), cancellationToken));
        this.HasProposed = true;

        return "Proposed. Nothing was sent; the person reviews the draft and decides.";
    }

    [Description("Proposes an answer to a message.")]
    private async Task<string> ProposeReplyAsync(
        [Description("The id of the message being answered.")] string messageId,
        [Description("reply, replyToAll, or forward.")] string act,
        [Description("The body, as plain text, in the language the conversation is written in.")] string body,
        [Description("For a forward, every recipient's email address; for a reply, leave empty to answer whoever the message came from.")] string[]? recipients,
        CancellationToken cancellationToken)
    {
        await this.ReportAsync(AgentActivity.PreparingProposal, cancellationToken);

        if (!Guid.TryParse(messageId, out var answered)
            || !Enum.TryParse<AuthoredResponseAct>(act, ignoreCase: true, out var responseAct)
            || !Enum.IsDefined(responseAct)
            || !PresentationText.TryCreate(body, out var written))
        {
            return "Give a message id, one of reply, replyToAll, or forward, and a body.";
        }

        IReadOnlyList<EmailAddress> named = [];

        if (recipients is { Length: > 0 })
        {
            if (AddressesOf(recipients) is not { } parsed)
            {
                return "Give valid recipient addresses, at most twenty.";
            }

            named = parsed;
        }

        var answeredId = StoredEmailId.Create(answered);
        var authored = await this.readers.ResponseAuthoring.AuthorAsync(
            new AuthoredResponseRequest
            {
                AnsweredEmailId = answeredId,
                Act = responseAct,
                PlainTextBody = written.Value,
                Recipients = [.. named.Select(static address => NamedRecipient.AtAddress(OutgoingRecipientRole.To, address.Address, address.DisplayName))],
            },
            cancellationToken);

        if (authored.Email is not { } email
            || AddressesOf([.. email.Recipients.Select(static recipient => recipient.Address)]) is not { } addressed
            || !PresentationText.TryCreate(email.Subject, out var titled))
        {
            return "That message cannot be answered that way from here.";
        }

        var citation = await this.CiteAsync(answeredId, email.Subject, cancellationToken);
        var block = new DraftBlock(
            new PresentationEvidence(PresentationSupport.Supported, [citation.Id], PresentationFreshness.Unknown),
            addressed,
            titled,
            written,
            DraftDisposition.Composed);

        await this.WriteAsync(this.journal.ProposeAsync(block, new AgentResponseSending(answeredId, responseAct, addressed, written), cancellationToken));
        this.HasProposed = true;

        return "Proposed. Nothing was sent; the person reviews the draft and decides.";
    }

    private static List<EmailAddress>? AddressesOf(IReadOnlyList<string> written)
    {
        List<EmailAddress> addresses = [];

        foreach (var address in written)
        {
            if (!EmailAddress.TryCreate(null, address, out var parsed))
            {
                return null;
            }

            addresses.Add(parsed);
        }

        var distinct = addresses.DistinctBy(static address => address.Address, StringComparer.OrdinalIgnoreCase).ToList();

        return distinct.Count is > 0 and <= DraftBlock.MaxRecipients ? distinct : null;
    }

    private static EmailAddress? SenderOf(ReadEmailContent message) =>
        message.Headers.Participants.FirstOrDefault(static participant => participant.Role is EmailAddressRole.From)?.Address;

    private async Task<IReadOnlyList<ReadEmailContent>?> ReadConversationAsync(string messageId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(messageId, out var parsed))
        {
            return null;
        }

        var opened = await this.readers.ContentReader.ReadContentAsync(
            GetEmailContentRequest.Create([StoredEmailId.Create(parsed)]),
            cancellationToken);

        var message = opened.Emails.Count is 0 ? null : opened.Emails[0].Content;

        if (message is { Thread: null })
        {
            return [message];
        }

        // The id may name the conversation itself, which is how the turn names the one the person is looking at.
        var thread = message?.Thread?.ThreadId ?? EmailThreadId.Create(parsed);
        var conversation = await this.readers.ContentReader.ReadContentAsync(
            GetEmailContentRequest.CreateForThread(thread),
            cancellationToken);

        return [.. conversation.Emails.Select(static outcome => outcome.Content).OfType<ReadEmailContent>()];
    }

    private async Task<ThreadStateBlock?> ThreadStateBlockOfAsync(
        EmailThreadState state,
        IReadOnlyList<ReadEmailContent> messages,
        CancellationToken cancellationToken)
    {
        var participants = messages
            .Select(SenderOf)
            .OfType<EmailAddress>()
            .DistinctBy(static address => address.Address, StringComparer.OrdinalIgnoreCase)
            .Take(ThreadStateBlock.MaxParticipants)
            .Select(static address => new ThreadParticipant(
                PresentationText.Create(string.IsNullOrWhiteSpace(address.DisplayName) ? address.Address : address.DisplayName),
                address))
            .ToList();
        var subjects = messages.ToDictionary(static message => message.StoredEmailId, static message => message.Headers.Subject);
        List<ThreadStatement> agreements = [];
        List<ThreadStatement> openQuestions = [];
        List<ThreadCommitment> commitments = [];
        HashSet<PresentationCitationId> sources = [];

        foreach (var entry in state.Entries)
        {
            List<PresentationCitationId> cites = [];

            foreach (var source in entry.Sources)
            {
                cites.Add((await this.CiteAsync(source, subjects.GetValueOrDefault(source), cancellationToken)).Id);
            }

            sources.UnionWith(cites);

            switch (entry.Aspect)
            {
                case ThreadStateAspect.Agreement:
                    agreements.Add(new ThreadStatement(PresentationText.Create(entry.Text), cites));
                    break;
                case ThreadStateAspect.OpenQuestion:
                    openQuestions.Add(new ThreadStatement(PresentationText.Create(entry.Text), cites));
                    break;
                case ThreadStateAspect.Commitment:
                    commitments.Add(new ThreadCommitment(
                        PresentationText.Create(entry.Text),
                        entry.OwedBy is { } owedBy ? new ThreadParticipant(PresentationText.Create(owedBy), address: null) : null,
                        entry.DueAt,
                        cites));
                    break;
                default:
                    break;
            }
        }

        if (participants.Count is 0 || agreements.Count + openQuestions.Count + commitments.Count is 0)
        {
            return null;
        }

        var freshness = state.IsCurrent
            ? PresentationFreshness.CurrentAt(state.DerivedAt)
            : PresentationFreshness.StaleSince(state.DerivedAt);
        var evidence = sources.Count is 0
            ? PresentationEvidence.Unsupported(freshness)
            : new PresentationEvidence(PresentationSupport.Supported, [.. sources.Take(PresentationEvidence.MaxCitations)], freshness);

        return new ThreadStateBlock(evidence, participants, agreements, openQuestions, commitments);
    }

    private async Task<PresentationCitation> CiteAsync(StoredEmailId message, string? subject, CancellationToken cancellationToken)
    {
        if (this.cited.TryGetValue(message, out var known))
        {
            return known;
        }

        var citation = new PresentationCitation(
            PresentationCitationId.Create(string.Create(CultureInfo.InvariantCulture, $"m{this.cited.Count + 1}")),
            new EmailCitationTarget(message),
            PresentationText.TryCreate(subject, out var titled) ? titled : PresentationText.Create(message.ToString()),
            PresentationSourceMedium.Written);

        this.cited[message] = citation;
        await this.WriteAsync(this.journal.DeclareAsync(citation, cancellationToken));

        return citation;
    }

    private Task ReportAsync(AgentActivity activity, CancellationToken cancellationToken) =>
        this.WriteAsync(this.journal.ReportAsync(activity, cancellationToken));

    /// <summary>Ends the run where the store refused a write, which is a stop the person recorded.</summary>
    private async Task WriteAsync(Task<bool> write)
    {
        if (!await write)
        {
            this.journal.Stopping.ThrowIfCancellationRequested();
        }
    }

    /// <summary>A tool that says what the run is doing before it does it.</summary>
    private sealed class ReportingFunction(AIFunction inner, AgentAnswerJournal journal, AgentActivity activity) : DelegatingAIFunction(inner)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            if (!await journal.ReportAsync(activity, cancellationToken))
            {
                journal.Stopping.ThrowIfCancellationRequested();
            }

            return await base.InvokeCoreAsync(arguments, cancellationToken);
        }
    }
}
