// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Emails.Search;

namespace MailFathom.Host.Configuration.Answering;

/// <summary>Declares how much mail one question may read, how much a run may spend, and how much every run of a period may add up to.</summary>
/// <remarks>
/// <para>
/// A configuration root of its own beside <c>Chat</c> rather than a block inside it, because the two answer different
/// questions. <c>Chat</c> says which endpoint this deployment generates text with and what one <em>call</em> to it may
/// carry; this says what answering a question is allowed to cost, and it is the section an operator reads to find out
/// how much of their mailbox can leave the process. Removing the chat endpoint stops answering entirely and leaves
/// these ceilings describing something that no longer happens, which is the honest reading rather than a reason to nest
/// them.
/// </para>
/// <para>
/// Every member has a usable default and the whole section is optional, so an absent section is a deployment answering
/// under the conservative ceilings this type states rather than one answering without any. That is the opposite of how
/// the provider sections behave, and deliberately: an absent provider is a capability nobody asked for, while an absent
/// ceiling would be an unbounded bill nobody asked for either.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailAnsweringOptions : IValidatableObject
{
    /// <summary>The configuration section this declaration is bound from.</summary>
    public const string SectionName = "MailAnswering";

    /// <summary>Gets or sets the greatest number of passages one lookup may hand over.</summary>
    /// <remarks>
    /// <para>
    /// How many messages a single lookup can draw on. Capped by what one search can rank, because a retrieval is
    /// answered from a search window and asking for more passages than that window holds would state a bound no run
    /// could reach.
    /// </para>
    /// <para>
    /// The default is the default window <c>search_emails</c> returns, so a run reaches as many messages per lookup as
    /// searching for the same thing would. What bounds how much of a mailbox one question can reach is
    /// <see cref="MaxRetrievedCharactersPerRun" /> below, applied across every lookup a model makes.
    /// </para>
    /// </remarks>
    [Range(1, EmailSearchResultLimit.MaximumValue)]
    public int MaxPassagesPerRetrieval { get; set; } = EmailSearchResultLimit.DefaultValue;

    /// <summary>Gets or sets the greatest number of characters one passage may carry.</summary>
    /// <remarks>How much of any single message a lookup can draw out. Separate from the count above because one enormous extract and a spread across several messages say different things about a mailbox, and a single total would let them satisfy the same ceiling.</remarks>
    [Range(1, 100_000)]
    public int MaxCharactersPerPassage { get; set; } = 1_200;

    /// <summary>Gets or sets the greatest number of characters of retrieved mail one run may send.</summary>
    /// <remarks>
    /// The privacy ceiling, and the one that bounds a run rather than a lookup: a model decides how many lookups to
    /// make, so nothing about the two settings above says how much of a mailbox one question can reach. Reaching it
    /// cuts rather than refuses — the run answers from what it has and the response says the mailbox was not read in
    /// full.
    /// </remarks>
    [Range(1, 10_000_000)]
    public int MaxRetrievedCharactersPerRun { get; set; } = 20_000;

    /// <summary>Gets or sets the greatest number of provider calls one run may make.</summary>
    /// <remarks>The ceiling that holds whatever the provider reports. A run is a tool loop and its length is the model's decision, so this is what ends one that keeps asking; unlike the token ceiling beside it, it needs nothing from the provider's answer to be enforceable.</remarks>
    [Range(1, 1_000)]
    public int MaxProviderCallsPerRun { get; set; } = 8;

    /// <summary>Gets or sets the greatest number of tokens, sent and received together, one run may consume.</summary>
    /// <remarks>The cost ceiling, stated in the unit a provider bills by. Checked before each call against what the calls before it reported, so the call that crosses it is paid for — what a call will cost is not knowable until it has been answered.</remarks>
    [Range(1, 100_000_000)]
    public long MaxTokensPerRun { get; set; } = 80_000;

    /// <summary>Gets or sets the greatest number of characters one answer may carry.</summary>
    /// <remarks>What a single response publishes rather than what a run spends. It cuts rather than refuses, because an answer larger than a limit has already been generated and paid for; the response says it was cut.</remarks>
    [Range(1, 1_000_000)]
    public int MaxAnswerCharacters { get; set; } = 20_000;

    /// <summary>Gets or sets the greatest number of emails one answer may cite.</summary>
    [Range(1, 1_000)]
    public int MaxCitations { get; set; } = 20;

    /// <summary>Gets or sets how long one period lasts before what was spent in it is forgotten.</summary>
    /// <remarks>
    /// An hour by default rather than a day, because a ceiling an operator only meets once a day is one they meet after
    /// the spend has happened. The window tumbles rather than slides, so a client that spends the whole allowance at
    /// the end of one period and again at the start of the next has spent twice it across an interval of the same
    /// length.
    /// </remarks>
    public TimeSpan AggregatePeriod { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Gets or sets the greatest number of runs one period may admit.</summary>
    /// <remarks>The ceiling on how enthusiastic a client may be. Nothing about the MCP surface stops one from asking a hundred questions in a minute, and without this a per-run ceiling bounds each of those hundred and none of the total.</remarks>
    [Range(1, 1_000_000)]
    public int MaxRunsPerPeriod { get; set; } = 30;

    /// <summary>Gets or sets the greatest number of tokens the runs of one period may consume between them.</summary>
    [Range(1, 10_000_000_000)]
    public long MaxTokensPerPeriod { get; set; } = 300_000;

    /// <summary>Reports every reason this declaration could not be mapped, by reading the declaration alone.</summary>
    /// <returns>One message per rule this declaration breaks, which is empty for a usable one.</returns>
    /// <remarks>
    /// The composition root maps this section into value objects while the builder is being composed, which is before
    /// the container exists and therefore before <c>ValidateOnStart</c> could have run. Without this the first thing to
    /// notice a typo would be a <see cref="ArgumentOutOfRangeException" /> out of a <c>Create</c> method, which reaches
    /// an operator as a framework stack trace rather than as the aggregated report every other section produces. It
    /// runs the attributes and <see cref="Validate" /> together, so the same rules answer whichever path reaches them.
    /// </remarks>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        List<ValidationResult> results = [];

        Validator.TryValidateObject(this, new ValidationContext(this), results, validateAllProperties: true);

        return [.. results.Select(result => result.ErrorMessage ?? string.Empty)];
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // The one pair a reader can set into contradiction without either value being wrong on its own: a run allowed
        // fewer characters than a single passage carries would drop every passage of every lookup and answer from
        // nothing, which is a working deployment that answers nothing rather than a failure anybody would trace.
        if (this.MaxRetrievedCharactersPerRun < this.MaxCharactersPerPassage)
        {
            yield return new ValidationResult(
                $"MailAnswering declares MaxRetrievedCharactersPerRun of {this.MaxRetrievedCharactersPerRun}, below the MaxCharactersPerPassage of {this.MaxCharactersPerPassage}, so no lookup could hand over even one passage.",
                [nameof(this.MaxRetrievedCharactersPerRun)]);
        }

        if (this.AggregatePeriod <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                "MailAnswering declares an AggregatePeriod that is not positive, so no period could ever elapse and the first questions to spend the allowance would be the last this instance answered.",
                [nameof(this.AggregatePeriod)]);
        }
    }
}
