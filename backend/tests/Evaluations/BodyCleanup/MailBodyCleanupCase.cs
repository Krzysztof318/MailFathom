// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Cleaning;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Domain.Access;
using MailFathom.Evaluations.AgentConversations;
using MailFathom.Evaluations.Corpus;
using MailFathom.TestSupport;

namespace MailFathom.Evaluations.BodyCleanup;

/// <summary>One corpus body put to the body-cleanup agent, all of which the instruction says a reader keeps.</summary>
/// <remarks>
/// <para>
/// The outline is the one a deployment describes: the message is rendered by the renderer the reading pane is drawn from,
/// and its reduced document is described by <see cref="MailBodyCleaningOutline" />. So the blocks a model is asked about
/// are the blocks a reader would be shown.
/// </para>
/// <para>
/// Every body here is one the instruction answers with a single range keeping everything, and each was chosen for the
/// block that tempts a model to drop it anyway: a quoted decision, which the instruction keeps because dropping history is
/// not this pass's work; a closing signature, which is what the sender wrote; and a postal address that is the invoice's
/// bill-to detail, which the instruction keeps wherever it sits. The committed corpus carries no preheader, menu, or legal
/// footer, so no case here can ask for a block to be dropped.
/// </para>
/// </remarks>
/// <param name="Name">The name the case is filed and reported under.</param>
/// <param name="Position">Which message of the corpus it is, from zero in delivery order.</param>
/// <param name="Tempting">Words the block a model is tempted to drop opens with, which the case's own test finds in the outline.</param>
internal sealed record MailBodyCleanupCase(string Name, int Position, string Tempting)
{
    /// <summary>Gets every case, in the order the report lists them.</summary>
    public static IReadOnlyList<MailBodyCleanupCase> All { get; } =
    [
        new("QuotedDecision", Position: 18, Tempting: "Decision: use the three highest-priority themes"),
        new("QuoteAndSignature", Position: 19, Tempting: "Mara Vale"),
        new("BillToAddress", Position: 84, Tempting: "17 Willowmere Lane"),
    ];

    /// <summary>Gets the corpus message the body belongs to.</summary>
    public CorpusMessage Message => CorpusMessage.At(this.Position);

    /// <summary>Finds a case by the name it is filed under.</summary>
    /// <param name="name">The case's name.</param>
    /// <returns>The case.</returns>
    public static MailBodyCleanupCase Named(string name) =>
        All.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

    /// <summary>Describes the body as the outline a deployment would put to the agent.</summary>
    /// <param name="cancellationToken">Withdraws the rendering.</param>
    /// <returns>The outline.</returns>
    public async Task<CleanableMailBody> DescribeAsync(CancellationToken cancellationToken)
    {
        var read = await CorpusReaders
            .ContentReaderFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead))
            .ReadContentAsync(GetEmailContentRequest.Create([this.Message.Id]) with { IncludeMailDocument = true }, cancellationToken);

        var message = read.Emails[0].Content
            ?? throw new InvalidOperationException($"Corpus message {this.Position} could not be read.");
        var document = message.Body.Document
            ?? throw new InvalidOperationException($"Corpus message {this.Position} rendered no document.");

        return MailBodyCleaningOutline.Describe(document, message.Headers.Subject, MailBodyCleaning.SenderOf(message.Headers));
    }

    /// <inheritdoc />
    public override string ToString() => this.Name;
}
