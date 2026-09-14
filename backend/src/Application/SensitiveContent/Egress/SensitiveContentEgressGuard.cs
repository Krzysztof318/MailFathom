// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.SensitiveContent.Egress;

/// <summary>The one thing every egress point calls before it hands text to somebody else.</summary>
/// <remarks>
/// <para>
/// One guard rather than a redaction per consumer, for the reason there is one redactor behind it: a placeholder a
/// caller composed itself would drift from the shared one the first time either gained a rule, and a consumer holding
/// the redactor directly would decide for itself what to do with a finding. What a consumer gets here is guarded text
/// and nothing else — the findings stay inside, are counted by category, and are never handed to a caller that might
/// log one.
/// </para>
/// <para>
/// <b>Guard a value, never a composed document.</b> A detected region is replaced wherever it was found, so a scan of a
/// document this system assembled — an XML envelope, a JSON payload, a formatted listing — can report a region that
/// covers a delimiter as well as the text beside it, and replacing that region would destroy the structure while
/// leaving the value's neighbours in it. Every consumer that owns the values therefore guards the field it is about to
/// write and composes afterwards.
/// </para>
/// <para>
/// One consumer cannot: a port handed a conversation somebody else built has no way to tell which turn a mailbox
/// reached, so it guards each turn whole and accepts the structural cost above on a turn that happens to be a document.
/// That is the exception rather than a second rule, and it is bounded by the guarantee that makes it necessary — every
/// text leaves that port scanned, whatever its caller composed.
/// </para>
/// <para>
/// <b>With nothing switched on this guard is inert.</b> It is registered whatever a deployment configured, so no
/// consumer carries a null check or a second code path, and where the mail being published has nothing scanned for,
/// every call returns its argument without constructing a detector, taking a concurrency permit, or touching an
/// instrument. That is what makes an opt-in nobody took cost nothing on any of these paths.
/// </para>
/// <para>
/// <b>Whose mail is being published is settled before any of it is.</b> A deployment serves several mailboxes and each
/// of them has a posture of its own, so the use case names what it resolved once — with <see cref="ActingFor(MailAccountIdentity)" />
/// where it is acting on one account, and with <see cref="ActingFor(MailUserId)" /> where it reads across every account
/// one user is assigned — and every value guarded anywhere inside that flow is read under the posture that names.
/// Guarding outside such a scope while this deployment scans anything is a defect rather than a permissive default, and
/// says so.
/// </para>
/// <para>
/// A read that spans a user's accounts is guarded by the strictest of their postures, because the value being guarded
/// is several layers below the point its account could still be named and a search hands out mail from whichever of
/// them matched. That only ever redacts more than the message's own account asked for, which is the safe direction;
/// where a flow does hold one account it names it, and the message is judged by its own mailbox alone.
/// </para>
/// <para>
/// It is a scope of its own rather than part of the reported operation because the two cover different stretches of one
/// read. The operation is the payload being published and is opened where that payload is assembled; the user is
/// settled far earlier, since a search embeds its query text through a model provider before it has a page to report
/// on. Folding the user into the operation would leave that call with nobody to answer for it.
/// </para>
/// </remarks>
public sealed class SensitiveContentEgressGuard
{
    private readonly ISensitiveContentPostures postures;
    private readonly ISensitiveContentEgressTelemetry telemetry;
    private readonly TimeProvider timeProvider;

    /// <summary>Whose mail this asynchronous flow is reading, where a use case has said.</summary>
    /// <remarks>
    /// Ambient rather than an argument, because what is being read is settled by the use case that resolved the scope
    /// while the values are guarded field by field several calls deeper — a search result's snippets inside the
    /// browser, a query placed in a vector space inside a provider adapter, a retrieved extract inside another.
    /// Threading it through every overload of this contract, and through the ports between, would put a parameter on
    /// each of them to say what the flow already knows.
    /// </remarks>
    private readonly AsyncLocal<MailInScope?> actingFor = new();

    /// <summary>The operation the guarding on this asynchronous flow is being reported as, where one was opened.</summary>
    /// <remarks>
    /// Ambient for the reason the user is: the operation is delimited by the consumer that owns the payload while the
    /// values are guarded several calls deeper, which is the same reason the tracing API itself keeps the current span
    /// this way.
    /// </remarks>
    private readonly AsyncLocal<ISensitiveContentGuardScope?> currentOperation = new();

    /// <summary>Initializes the guard of a deployment, whether or not any user it serves is scanned for.</summary>
    /// <param name="postures">Answers what each account's mail is scanned under.</param>
    /// <param name="telemetry">Reports what each guarded call found and what it cost.</param>
    /// <param name="timeProvider">Measures what the scan added to the operation being guarded.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public SensitiveContentEgressGuard(
        ISensitiveContentPostures postures,
        ISensitiveContentEgressTelemetry telemetry,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(postures);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.postures = postures;
        this.telemetry = telemetry;
        this.timeProvider = timeProvider;
    }

    /// <summary>Gets whether anything is scanned for on this flow.</summary>
    /// <remarks>
    /// What this flow is acting for where it has said, and anything at all where it has not. Read by a consumer
    /// deciding whether work only a scan makes necessary is worth doing — never as permission to hand text on
    /// unguarded, which is what calling the guard already does when nothing scans the mail in scope. Inside a use case
    /// this is that one answer, so a reader serving a mailbox nothing scans does exactly what it did before this
    /// feature existed however much the deployment scans elsewhere.
    /// </remarks>
    public bool IsActive => this.PostureInScope()?.IsActive ?? this.postures.IsActiveForAnyAccount;

    /// <summary>States that everything guarded on this flow from here on comes out of one account.</summary>
    /// <param name="account">The account the use case is acting on, whose posture every value is read under.</param>
    /// <returns>The scope, which restores whatever the flow was acting for when it is disposed.</returns>
    /// <remarks>
    /// Opened by a use case that holds one mailbox rather than a user's whole mail — a synchronization pass, a
    /// derivation enqueued for one account — so the text is judged by the settings written on the account it came out
    /// of and by nothing another of that user's mailboxes asked for.
    /// </remarks>
    public IDisposable ActingFor(MailAccountIdentity account) =>
        this.Enter(new MailInScope(account.User, account.Id));

    /// <summary>States whose mail everything guarded on this flow from here on belongs to.</summary>
    /// <param name="user">The user the use case resolved, across whose accounts every value is read.</param>
    /// <returns>The scope, which restores whatever the flow was acting for when it is disposed.</returns>
    /// <remarks>
    /// <para>
    /// Opened by the use case, immediately after it has established whose mail it may read and before it reads any.
    /// Everything below it — a page assembled, a query placed in a vector space, an extract written into a prompt, an
    /// answer published — is that user's, however many layers separate it from here.
    /// </para>
    /// <para>
    /// The posture is the strictest over the accounts they are assigned, because such a read hands out mail from
    /// whichever of them matched and the account is no longer in hand where the value is guarded. A use case that does
    /// hold one account names it instead.
    /// </para>
    /// <para>
    /// Entering one twice for the same user is an ordinary nesting rather than a mistake: a reader that assembles a
    /// conversation inside a content read opens its own, and restoring the previous scope rather than clearing it is
    /// what keeps the enclosing read acting for what it resolved.
    /// </para>
    /// <para>
    /// The scope is published as <see cref="IDisposable" /> rather than as a type of its own, for the reason
    /// <c>BeginScope</c> is: what a caller does with it is dispose it at the end of the method that opened it.
    /// </para>
    /// </remarks>
    public IDisposable ActingFor(MailUserId user) => this.Enter(new MailInScope(user, Account: null));

    /// <summary>Opens the report of one guarded operation, and reports every text guarded inside it as part of it.</summary>
    /// <param name="egressPoint">Where the texts this operation guards are going.</param>
    /// <param name="cancellationToken">The caller's token, which separates an operation a shutdown stopped from one that broke.</param>
    /// <returns>The operation, which the caller tells it finished and then disposes.</returns>
    /// <remarks>
    /// <para>
    /// The operation is the payload a consumer is about to publish — one message's content, one page of a listing, one
    /// window of results — rather than each field of it. That is what a caller waits on, and what a percentile over
    /// individual values cannot say.
    /// </para>
    /// <para>
    /// A flow nothing scans opens nothing, so an opt-in nobody took stays as free here as every other path through this
    /// guard.
    /// </para>
    /// </remarks>
    public EgressOperation BeginGuardedOperation(
        SensitiveContentEgressPoint egressPoint,
        CancellationToken cancellationToken)
    {
        if (!this.IsActive)
        {
            return EgressOperation.Inert;
        }

        var previous = this.currentOperation.Value;
        var scope = this.telemetry.BeginGuardedOperation(
            egressPoint,
            this.actingFor.Value?.User ?? default,
            cancellationToken);

        this.currentOperation.Value = scope;

        return new EgressOperation(this, scope, previous);
    }

    /// <summary>Guards one text about to cross out of this deployment.</summary>
    /// <param name="egressPoint">Where the text is going.</param>
    /// <param name="text">The text to guard, which must be a value rather than a document composed around one.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The text with every detected region replaced, or the text itself where nothing is scanned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the text carries, which refuses the egress.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public Task<string> GuardAsync(
        SensitiveContentEgressPoint egressPoint,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        return this.ActiveRedactor() is { } active
            ? this.RedactAsync(active, egressPoint, text, cancellationToken)
            : Task.FromResult(text);
    }

    /// <summary>Guards one text and reports what the analyzed ceiling kept out of it.</summary>
    /// <param name="egressPoint">Where the text is going.</param>
    /// <param name="text">The text to guard, which must be a value rather than a document composed around one.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The guarded text and how many characters lay beyond what one scan analyzes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the text carries, which refuses the egress.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    /// <remarks>
    /// For the consumer that publishes how complete what it hands over is. The ceiling is the one bound in this feature
    /// that never raises a failure — text beyond it is dropped rather than passed on unscanned — so a consumer that
    /// could not see the drop would report a message as whole that a scan had ended early.
    /// </remarks>
    public Task<GuardedText> GuardWithOmissionAsync(
        SensitiveContentEgressPoint egressPoint,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        return this.ActiveRedactor() is { } active
            ? this.RedactReportingOmissionAsync(active, egressPoint, text, cancellationToken)
            : Task.FromResult(new GuardedText(text, OmittedCharacterCount: 0));
    }

    /// <summary>Guards a text that a message need not carry at all.</summary>
    /// <param name="egressPoint">Where the text is going.</param>
    /// <param name="text">The text to guard, or <see langword="null" /> where the message carried none.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The guarded text, or <see langword="null" /> where there was none.</returns>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the text carries, which refuses the egress.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    /// <remarks>
    /// Absence is carried through rather than turned into an empty string, because a subject nobody wrote and a subject
    /// redacted to nothing are different facts and a reader acts differently on each.
    /// </remarks>
    public async Task<string?> GuardOptionalAsync(
        SensitiveContentEgressPoint egressPoint,
        string? text,
        CancellationToken cancellationToken)
    {
        if (text is null || this.ActiveRedactor() is not { } active)
        {
            return text;
        }

        return await this.RedactAsync(active, egressPoint, text, cancellationToken);
    }

    /// <summary>Guards every text of one publication about to cross out of this deployment.</summary>
    /// <param name="egressPoint">Where the texts are going.</param>
    /// <param name="texts">The texts to guard, in the order they are published.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The guarded texts, in the same order and the same number.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="texts" /> is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what one of them carries, which refuses the whole egress.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    /// <remarks>
    /// Each text is scanned on its own rather than joined into one pass, because a joined scan would let a detection
    /// straddle the join and redact across two publications that have nothing to do with each other. The concurrency
    /// bound the redactor holds is what keeps a wide publication from opening a connection per text.
    /// </remarks>
    public Task<IReadOnlyList<string>> GuardAllAsync(
        SensitiveContentEgressPoint egressPoint,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return texts.Count > 0 && this.ActiveRedactor() is { } active
            ? this.RedactAllAsync(active, egressPoint, texts, cancellationToken)
            : Task.FromResult(texts);
    }

    private async Task<IReadOnlyList<string>> RedactAllAsync(
        SensitiveContentRedactor active,
        SensitiveContentEgressPoint egressPoint,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        var guarded = new List<string>(texts.Count);

        foreach (var text in texts)
        {
            guarded.Add(await this.RedactAsync(active, egressPoint, text, cancellationToken));
        }

        return guarded;
    }

    private async Task<string> RedactAsync(
        SensitiveContentRedactor active,
        SensitiveContentEgressPoint egressPoint,
        string text,
        CancellationToken cancellationToken)
    {
        var redacted = await this.RedactAndReportAsync(active, egressPoint, text, cancellationToken);

        return redacted.Text;
    }

    private async Task<GuardedText> RedactReportingOmissionAsync(
        SensitiveContentRedactor active,
        SensitiveContentEgressPoint egressPoint,
        string text,
        CancellationToken cancellationToken)
    {
        var redacted = await this.RedactAndReportAsync(active, egressPoint, text, cancellationToken);

        return new GuardedText(redacted.Text, redacted.OmittedCharacterCount);
    }

    /// <summary>Runs the shared redaction and reports what it found, or reports the refusal and re-raises it.</summary>
    /// <remarks>
    /// The refusal reaches the caller unchanged. It already names the scanner and no text, and translating it here would
    /// cost the error code an operator reads the failure by while adding nothing this layer knows.
    /// </remarks>
    private async Task<RedactedText> RedactAndReportAsync(
        SensitiveContentRedactor active,
        SensitiveContentEgressPoint egressPoint,
        string text,
        CancellationToken cancellationToken)
    {
        var startedAt = this.timeProvider.GetTimestamp();

        try
        {
            var redacted = await active.RedactAsync(text, cancellationToken);

            this.telemetry.RecordGuarded(egressPoint, redacted, this.timeProvider.GetElapsedTime(startedAt));
            this.currentOperation.Value?.TextGuarded();

            return redacted;
        }
        catch (SensitiveContentScannerUnavailableException refusal)
        {
            this.telemetry.RecordRefused(egressPoint, refusal.Scanner);
            this.currentOperation.Value?.Refused();

            throw;
        }
    }

    /// <summary>Finds the posture the mail this flow is acting for is read under, or nothing where it named none.</summary>
    /// <remarks>
    /// One account is read under its own posture and a user's whole mail under the strictest over their accounts,
    /// which is the difference the two scopes exist to state.
    /// </remarks>
    private SensitiveContentPosture? PostureInScope() => this.actingFor.Value switch
    {
        { Account: { } account } => this.postures.ForAccount(account),
        { User: var user } => this.postures.AcrossAccountsOf(user),
        null => null,
    };

    /// <summary>Finds the redaction the mail this flow is acting for is read under, if any is.</summary>
    /// <remarks>
    /// Guarding with nothing in scope is refused rather than answered, and refused only where this deployment scans
    /// something: a path publishing mail without naming whose it is would otherwise read whichever posture happened to
    /// be composed first, which is one mailbox's text judged by another's rules. Where nothing is scanned for anywhere
    /// there is no posture to get wrong, so such a path costs nothing and stays as free as it was before any of this
    /// existed.
    /// </remarks>
    private SensitiveContentRedactor? ActiveRedactor()
    {
        if (this.PostureInScope() is { } posture)
        {
            return posture.Redactor;
        }

        return this.postures.IsActiveForAnyAccount
            ? throw new InvalidOperationException(
                "Text reached the sensitive-content egress guard on a flow acting for nobody's mail, so there is no "
                + "scanning posture to read. Every use case that publishes mail states the account or the user it "
                + "resolved before it reads any.")
            : null;
    }

    /// <summary>Makes one scope current and hands back what restores the previous one.</summary>
    private ActingScope Enter(MailInScope scope)
    {
        var previous = this.actingFor.Value;

        this.actingFor.Value = scope;

        return new ActingScope(this, previous);
    }

    /// <summary>What the mail guarded on this flow belongs to.</summary>
    /// <param name="User">The user the use case resolved, which is what a guarded operation is reported against.</param>
    /// <param name="Account">The one account the flow is acting on, or <see langword="null" /> where it reads across the user's own.</param>
    private readonly record struct MailInScope(MailUserId User, MailAccountId? Account);

    /// <summary>Keeps one scope current for as long as the use case that resolved it is reading that mail.</summary>
    /// <remarks>
    /// The previous scope is restored rather than cleared, so a read nested inside another leaves the enclosing one
    /// acting for what it resolved.
    /// </remarks>
    private sealed class ActingScope(SensitiveContentEgressGuard guard, MailInScope? previous) : IDisposable
    {
        public void Dispose() => guard.actingFor.Value = previous;
    }

    /// <summary>Keeps one guarded operation current for as long as its consumer is guarding into it.</summary>
    /// <remarks>
    /// The previous operation is restored rather than cleared, so a consumer that guards a payload while assembling
    /// another one leaves the outer report intact instead of ending it early. A flow nothing scans receives
    /// <see cref="Inert" />, which reports nothing and costs one shared instance.
    /// </remarks>
    public sealed class EgressOperation : IDisposable
    {
        private readonly SensitiveContentEgressGuard? guard;
        private readonly ISensitiveContentGuardScope? scope;
        private readonly ISensitiveContentGuardScope? previous;

        private bool closed;

        private EgressOperation()
        {
        }

        internal EgressOperation(
            SensitiveContentEgressGuard guard,
            ISensitiveContentGuardScope scope,
            ISensitiveContentGuardScope? previous)
        {
            this.guard = guard;
            this.scope = scope;
            this.previous = previous;
        }

        /// <summary>Gets the operation of a deployment that scans nothing.</summary>
        internal static EgressOperation Inert { get; } = new();

        /// <summary>Records that the consumer guarded everything the payload was going to publish.</summary>
        /// <remarks>
        /// Called before the payload is returned rather than by disposal, because every way a scan ends badly — a
        /// refusal, a cancelled shutdown, a scanner that faulted — leaves through the same <c>using</c> as a scan that
        /// worked. Only the consumer knows which of the two happened.
        /// </remarks>
        public void Completed() => this.scope?.Completed();

        /// <inheritdoc />
        public void Dispose()
        {
            if (this.closed || this.guard is null || this.scope is null)
            {
                return;
            }

            this.closed = true;
            this.guard.currentOperation.Value = this.previous;
            this.scope.Dispose();
        }
    }
}
