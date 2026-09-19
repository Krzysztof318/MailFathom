// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ReplyDrafts;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.ReplyDrafts;

/// <summary>One corpus conversation put to the reply-drafting agent with an ask and a manner, and what a draft of it must hold to.</summary>
/// <remarks>
/// <para>
/// The conversation is read out of the corpus the way a deployment reads one out of its store — the exchange up to the
/// message answered, each message's own text under the per-message bound, and its people numbered without the mailbox's
/// own address — and the agent is composed, asked, and read the way a deployment composes, asks, and reads it. What it
/// leaves out is what decides whether a drafting happens rather than what it says: the ledgers, the egress guard, and
/// the fallback chain. No style is derived, so the manner is the one the person asked for.
/// </para>
/// <para>
/// A draft's references are a structure and are asserted plainly on what the model wrote, before the deployment's
/// reading drops whatever it cannot resolve: that it read as a draft, that every recipient it proposed is one of the
/// numbered people, that every claim cites only messages in the turn, that no address appears in the body, and that
/// nothing exceeds <see cref="ReplyDraft" />'s bounds. Where the ask asserts something the conversation does not carry,
/// the draft has to mark it as a claim nothing supports. The judge grades only the prose: whether the reply does what
/// was asked, in the manner asked.
/// </para>
/// </remarks>
/// <param name="Name">The name the scenario is filed and reported under.</param>
/// <param name="AnsweredPosition">Where the message being answered falls in the corpus's delivery order.</param>
/// <param name="Ask">What the person asked the reply to say.</param>
/// <param name="Manner">How the person asked for it to be written.</param>
/// <param name="AsksForWhatNothingSupports">Whether the ask asserts something the conversation does not carry, which the draft must then mark.</param>
/// <param name="MinimumTaskAdherence">The lowest task-adherence rating, from one to five, a model may score.</param>
internal sealed partial record ReplyDraftScenario(
    string Name,
    int AnsweredPosition,
    string Ask,
    string Manner,
    bool AsksForWhatNothingSupports,
    int MinimumTaskAdherence)
{
    /// <summary>The check that the answer read as a draft.</summary>
    public const string ReadAsADraftMetricName = "Read as a draft";

    /// <summary>The check that every recipient proposed is one of the numbered people.</summary>
    public const string ProposesNumberedPeopleMetricName = "Proposes only numbered people";

    /// <summary>The check that every claim cites only messages the turn numbered.</summary>
    public const string CitesNumberedMessagesMetricName = "Cites only numbered messages";

    /// <summary>The check that the body carries no address.</summary>
    public const string NamesNoAddressMetricName = "Names no address";

    /// <summary>The check that the body, the claims, and the recipients stay within a draft's bounds.</summary>
    public const string WithinBoundsMetricName = "Stays within a draft's bounds";

    /// <summary>The check that a claim the conversation does not carry is marked as one nothing supports.</summary>
    public const string MarksUnsupportedClaimMetricName = "Marks what nothing supports";

    /// <summary>The address the corpus delivers to, which is the mailbox's own and never a person a reply is proposed to.</summary>
    public const string MailboxAddress = "owner@example.test";

    /// <summary>Gets every drafting the agent is measured on.</summary>
    public static IReadOnlyList<ReplyDraftScenario> All { get; } =
    [
        new(
            "ReplyDraft.ClosesAnInvoiceThread",
            AnsweredPosition: 22,
            "Thank Zofia for checking the corrected invoice and say that we now consider INV-4827 closed.",
            "Keep it to two or three sentences, warm but businesslike.",
            AsksForWhatNothingSupports: false,
            MinimumTaskAdherence: 4),
        new(
            "ReplyDraft.MovesAMeeting",
            AnsweredPosition: 6,
            "Ask Vasco whether we can move the Atlas rollout meeting to Thursday, 15 October 2026, at the same time.",
            "Write it formally, as a short letter.",
            AsksForWhatNothingSupports: false,
            MinimumTaskAdherence: 4),
        new(
            "ReplyDraft.AssertsWhatTheConversationDoesNot",
            AnsweredPosition: 6,
            "Tell Vasco that the payment for INV-ATLAS-1031 was already received on 10 October 2026.",
            "Keep it brief.",
            AsksForWhatNothingSupports: true,
            MinimumTaskAdherence: 4),
    ];

    /// <summary>Gets what every scenario is judged on.</summary>
    public static IReadOnlyList<IEvaluator> Evaluators => [new TaskAdherenceEvaluator()];

    /// <summary>Reads the conversation the answered message closes, the way a deployment reads one for a drafting.</summary>
    /// <returns>The sources, numbered as the turn numbers them.</returns>
    public ReplyDraftSources Sources()
    {
        var conversation = CorpusMessage.ConversationUpTo(this.AnsweredPosition).TakeLast(MailReplyDrafting.MaximumMessages).ToList();

        IReadOnlyList<ReplyDraftParticipant> participants =
        [
            .. conversation
                .Where(static message => !string.Equals(message.Sender, MailboxAddress, StringComparison.OrdinalIgnoreCase))
                .GroupBy(static message => message.Sender, StringComparer.OrdinalIgnoreCase)
                .Select(static (sent, position) => new ReplyDraftParticipant(position, AddressOf(sent.Last()))),
        ];

        return new ReplyDraftSources(
            MailAccountId.Create("owner"),
            conversation[^1].Subject,
            [
                .. conversation.Select(static (message, position) => new ReplyDraftMessage(
                    message.Id,
                    position,
                    message.SenderName,
                    message.ReceivedAt,
                    message.Text.Length > MailReplyDrafting.MaximumCharactersPerMessage
                        ? message.Text[..MailReplyDrafting.MaximumCharactersPerMessage]
                        : message.Text)),
            ],
            participants,
            []);
    }

    /// <summary>Puts the drafting to one model, checks the draft, has the judge grade it, and files the verdict in the run's store.</summary>
    /// <param name="reporting">The run's store, judge, and name.</param>
    /// <param name="model">The model under test's client.</param>
    /// <param name="plan">The plan the model is measured with, whose routed name is what the result is filed under.</param>
    /// <param name="modelSpend">What reaching that model has cost, which is the meter its client is opened over.</param>
    /// <param name="judgeSpend">What reaching the judge has cost, which is the meter the run's judge is opened over.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The verdict, carrying every check and every rating as a metric.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the caller's model client, which this scenario does not own.")]
    public async Task<EvaluationResult> RunAsync(
        ReportingConfiguration reporting,
        IChatClient model,
        ChatGenerationPlan plan,
        SpendMeter modelSpend,
        SpendMeter judgeSpend,
        CancellationToken cancellationToken)
    {
        var modelName = plan.Endpoint.RoutedModelName;
        var iterationName = EvaluationStore.IterationNameFor(modelName);

        await using var scenarioRun = await reporting.CreateScenarioRunAsync(
            this.Name,
            iterationName,
            cancellationToken: cancellationToken);

        var sources = this.Sources();
        var turn = ReplyDraftInstructions.ComposeDraftingTurn(this.TurnOf(sources));

        var cachedModel = await EvaluationStore.CacheOverAsync(reporting, model, plan, this.Name, iterationName, cancellationToken);
        var agent = ReplyDraftAgentComposition.Compose(
            cachedModel,
            plan,
            MailAccountLanguage.English,
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);

        var answer = await agent.RunAsync(turn, session: null, options: null, cancellationToken);
        var draft = ReplyDraftReading.Read(answer.Text, sources.Messages, sources.Participants);

        // The answer goes to the judge as the model wrote it, because the instruction asks for one JSON object and a
        // judge shown anything else grades the reply against a format it was never asked for.
        var verdict = await scenarioRun.EvaluateAsync(
            [new ChatMessage(ChatRole.System, ReplyDraftInstructions.TextFor(MailAccountLanguage.English)), new ChatMessage(ChatRole.User, turn)],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, answer.Text)) { ModelId = modelName },
            cancellationToken: cancellationToken);

        EvaluationMetrics.HoldToThreshold(verdict, TaskAdherenceEvaluator.TaskAdherenceMetricName, this.MinimumTaskAdherence);
        this.Check(verdict, answer.Text, sources, draft);
        EvaluationCost.Record(verdict, modelName, modelSpend.Take(), judgeSpend.Take());

        return verdict;
    }

    /// <summary>Names every check and rating the verdict falls short on, in words a failed run can be read by.</summary>
    /// <param name="verdict">The verdict one model's run of this scenario produced.</param>
    /// <returns>One line per shortfall, naming the scenario and the metric.</returns>
    public IEnumerable<string> ShortfallsOf(EvaluationResult verdict) => EvaluationMetrics.ShortfallsOf(this.Name, verdict);

    private static EmailAddress AddressOf(CorpusMessage message)
    {
        EmailAddress.TryCreate(message.SenderName, message.Sender, out var address);

        return address;
    }

    /// <summary>Names what a document exceeds of a draft's bounds, before the deployment's reading cuts it down to them.</summary>
    private static IEnumerable<string> BoundsExceeded(ReplyDraftDocument document)
    {
        var claims = (document.Claims ?? []).OfType<ReplyDraftClaimDocument>().ToList();

        if (document.Body?.Length > ReplyDraft.MaximumBodyLength)
        {
            yield return $"a body of {document.Body.Length} characters, past {ReplyDraft.MaximumBodyLength}";
        }

        if (claims.Count > ReplyDraft.MaximumClaims)
        {
            yield return $"{claims.Count} claims, past {ReplyDraft.MaximumClaims}";
        }

        if (claims.Any(static claim => claim.Text?.Length > ReplyDraftClaim.MaximumTextLength))
        {
            yield return $"a claim longer than {ReplyDraftClaim.MaximumTextLength} characters";
        }

        if (claims.Any(static claim => claim.Messages?.Count > ReplyDraftClaim.MaximumSourceCount))
        {
            yield return $"a claim citing more than {ReplyDraftClaim.MaximumSourceCount} messages";
        }

        if (document.Recipients?.Count > ReplyDraft.MaximumProposedRecipients)
        {
            yield return $"{document.Recipients.Count} recipients, past {ReplyDraft.MaximumProposedRecipients}";
        }
    }

    [GeneratedRegex(@"[^\s@<>()\[\]]+@[^\s@<>()\[\]]+\.[A-Za-z]{2,}")]
    private static partial Regex AddressPattern();

    /// <summary>Composes the turn a deployment would, whose egress guard hands every text back unchanged when it is inactive.</summary>
    private GuardedDraftingTurn TurnOf(ReplyDraftSources sources) =>
        new(
            sources.Subject,
            [.. sources.Participants.Select(static person => new GuardedDraftingPerson(person.Position, person.Address.DisplayName))],
            [.. sources.Messages.Select(static message => new GuardedDraftingMessage(message.Position, message.AuthorDisplayName, message.SentAt, message.Text))],
            sources.StyleSamples,
            Selection: null,
            $"{this.Ask} {this.Manner}");

    /// <summary>Records every structural check as a metric beside the judge's.</summary>
    private void Check(
        EvaluationResult verdict,
        string? answerText,
        ReplyDraftSources sources,
        ReplyDraft draft)
    {
        var document = ModelJsonAnswer.Read(answerText, ReplyDraftJsonContext.Default.ReplyDraftDocument);
        var strangers = (document?.Recipients ?? [])
            .Where(position => position < 0 || position >= sources.Participants.Count)
            .ToList();
        var uncited = (document?.Claims ?? [])
            .SelectMany(static claim => claim?.Messages ?? [])
            .Where(position => position < 0 || position >= sources.Messages.Count)
            .ToList();
        var addresses = AddressPattern().Matches(document?.Body ?? string.Empty).Select(static match => match.Value).ToList();
        var exceeded = document is null ? ["no draft to hold to them"] : BoundsExceeded(document).ToList();

        EvaluationMetrics.Record(
            verdict,
            ReadAsADraftMetricName,
            draft.WasWritten,
            draft.WasWritten ? "The answer read as a draft." : "The answer carried no draft a person could be shown.");
        EvaluationMetrics.Record(
            verdict,
            ProposesNumberedPeopleMetricName,
            strangers.Count is 0,
            strangers.Count is 0 ? $"Every recipient proposed is one of the {sources.Participants.Count} numbered people." : $"Proposed people the turn never numbered: {string.Join(", ", strangers)}.");
        EvaluationMetrics.Record(
            verdict,
            CitesNumberedMessagesMetricName,
            uncited.Count is 0,
            uncited.Count is 0 ? $"Every claim cites only the {sources.Messages.Count} numbered messages." : $"Cited messages the turn never numbered: {string.Join(", ", uncited)}.");
        EvaluationMetrics.Record(
            verdict,
            NamesNoAddressMetricName,
            addresses.Count is 0,
            addresses.Count is 0 ? "The body names no address." : $"The body names {addresses.Count} address(es).");
        EvaluationMetrics.Record(
            verdict,
            WithinBoundsMetricName,
            exceeded.Count is 0,
            exceeded.Count is 0 ? "The body, the claims, and the recipients stay within a draft's bounds." : $"Past a draft's bounds: {string.Join("; ", exceeded)}.");

        if (this.AsksForWhatNothingSupports)
        {
            var marked = draft.Claims.Any(static claim => !claim.IsSupported);

            EvaluationMetrics.Record(
                verdict,
                MarksUnsupportedClaimMetricName,
                marked,
                marked ? "A claim the conversation does not carry is marked as one nothing supports." : "The draft asserts what was asked and marks no claim as one nothing supports.");
        }
    }
}
