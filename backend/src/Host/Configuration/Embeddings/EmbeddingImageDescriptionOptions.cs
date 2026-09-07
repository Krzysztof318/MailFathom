// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Embeddings;

/// <summary>Declares whether this deployment sends image attachments to a chat provider to be described, and how large one may be.</summary>
/// <remarks>
/// <para>
/// Off, and off is what an operator who has not read this gets. Describing an image sends the attachment's octets to
/// the declared chat endpoint, which is a disclosure of mail content as real as sending message text for embedding and
/// frequently a larger one — a photograph of a document discloses the document — and it is the one egress in this
/// system that no content scan covers, because the sensitive-content guard detects regions in a string and there is no
/// such operation for a picture. So the operator's own decision is the whole of the control, and nothing here starts
/// because a release shipped or a chat endpoint was declared for something else.
/// </para>
/// <para>
/// Separate from whether the deployment embeds at all, and separate again from the chat endpoint's own declaration,
/// because the three are different costs: embedding is priced per character over text already stored, describing is a
/// chat call per picture, and neither implies the other. It sits under this section rather than beside the chat
/// endpoint because what it produces is a passage — a description is text derived from an attachment, and what happens
/// to attachment-derived text is decided here.
/// </para>
/// <para>
/// How large a picture may be *in octets* is not declared here. That ceiling is the chat endpoint's
/// <c>MaxRequestImageOctets</c>, because it bounds what one request carries rather than what this feature will look
/// at, and two numbers for one limit would be a pair an operator could set into disagreement.
/// </para>
/// </remarks>
internal sealed class EmbeddingImageDescriptionOptions
{
    /// <summary>The largest pixel grid this deployment sends where none is declared.</summary>
    /// <remarks>
    /// Forty megapixels is well past any camera a person attaches a photograph from and well short of what a
    /// decompression bomb declares, which is the gap the number exists to sit in. An image declaring more is refused on
    /// its header rather than decoded.
    /// </remarks>
    public const long DefaultMaxPixels = 40_000_000;

    /// <summary>The largest pixel grid an operator may declare.</summary>
    /// <remarks>
    /// A billion pixels is twenty-five times the default and past every camera and scanner that exists, so a number
    /// above it is a mistyped one rather than a deployment with unusual attachments. The ceiling is enforced because
    /// the value it bounds is the one thing standing between a hostile header and a provider that decodes it.
    /// </remarks>
    public const long GreatestMaxPixels = 1_000_000_000;

    /// <summary>The fastest rate an operator may declare for description calls.</summary>
    /// <remarks>
    /// The figure the embedding rate ceiling uses, kept the same on purpose: both bound calls to an AI provider, no
    /// published quota approaches it, and two different maxima for one kind of key would be a difference an operator
    /// would have to look up rather than remember.
    /// </remarks>
    public const int GreatestRequestsPerMinute = 100_000;

    /// <summary>Gets or sets whether an image attachment is sent to the chat provider to be described.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the largest pixel grid an image may declare and still be described.</summary>
    /// <remarks>
    /// <para>
    /// The decompression-bomb bound, and the reason it is stated in pixels rather than in octets: a compressed image of
    /// a few kilobytes may declare a grid of billions, and what a decoder then allocates follows the grid rather than
    /// the file. Nothing in this deployment decodes an image, so what this protects is the provider that does — and a
    /// grid this deployment would not have decoded is not one to make somebody else decode either.
    /// </para>
    /// <para>
    /// Bounded by <see cref="FindDeclarationErrors" /> rather than by a range attribute, because
    /// <c>ValidateDataAnnotations</c> validates the bound root's own properties and never descends into a nested block
    /// — so an attribute here would read as a rule and enforce nothing.
    /// </para>
    /// </remarks>
    public long MaxPixels { get; set; } = DefaultMaxPixels;

    /// <summary>Gets or sets the descriptions one budget period may ask a provider for in total, or zero to bound none.</summary>
    /// <remarks>
    /// <para>
    /// Counted in calls rather than in octets or characters, because that is the shape of what describing costs: a
    /// provider prices a picture per request and a photograph's size predicts neither the price nor the tokens the
    /// answer comes back as. It is a ceiling of its own rather than a share of the embedding budget for the same
    /// reason — a chat call and an embedding call are two prices, and one number could not bound both.
    /// </para>
    /// <para>
    /// Reaching it leaves the mail unread and outstanding, so nothing is dropped and the next period reaches it. Zero
    /// declares no ceiling, which is what a deployment with image description switched off is in either way.
    /// </para>
    /// </remarks>
    public long MaxDescriptionsPerPeriod { get; set; }

    /// <summary>Gets or sets the descriptions one budget period may ask for on behalf of any one owner, or zero to bound no owner.</summary>
    /// <remarks>
    /// The same ceiling asked of one person, so a mailbox full of photographs cannot exhaust the window everybody else
    /// is working in.
    /// </remarks>
    public long MaxDescriptionsPerPeriodPerOwner { get; set; }

    /// <summary>Gets or sets how many description requests one minute may carry, or zero to pace none.</summary>
    /// <remarks>
    /// The rate ceiling, which bounds neither cost nor concurrency. What a period may cost is
    /// <see cref="MaxDescriptionsPerPeriod" />, and how many calls may be in flight at once is the
    /// <c>AiProviderInvocation</c> resilience budget, which owns that question for every provider call this deployment
    /// makes. This exists because a chat quota is stated per minute, and being refused for exceeding one costs an
    /// attempt, a retry, and a place in a circuit-breaker window other work is measured in.
    /// </remarks>
    public int MaxRequestsPerMinute { get; set; }

    /// <summary>Reports everything an operator must fix before this block can be used.</summary>
    /// <returns>One message per rule the declaration breaks, empty when it is usable.</returns>
    /// <remarks>
    /// Read while the host is being composed rather than under <c>ValidateOnStart</c>, because composition reads
    /// <see cref="MaxPixels" /> to decide which describer it registers and a rule that ran afterwards would let the one
    /// deployment shape it was written for die on an argument guard instead. The ceilings below are judged here rather
    /// than as range attributes for the reason every key on a nested block is: <c>ValidateDataAnnotations</c> never
    /// descends into one, so an attribute here would publish a constraint nothing applies.
    /// </remarks>
    public IReadOnlyList<string> FindDeclarationErrors() => [.. this.FindEveryDeclarationError()];

    /// <summary>Yields each rule this declaration breaks, which the list above materializes.</summary>
    private IEnumerable<string> FindEveryDeclarationError()
    {
        if (this.MaxPixels is not (> 0 and <= GreatestMaxPixels))
        {
            yield return $"{Key(nameof(this.MaxPixels))} — a grid ceiling is between 1 and {GreatestMaxPixels} pixels. Zero or less would refuse every image while reading as a bound somebody chose, and more than that is past every camera and scanner there is, which leaves whatever a hostile attachment declares to be decoded by the provider.";
        }

        if (this.MaxDescriptionsPerPeriod < 0)
        {
            yield return $"{Key(nameof(this.MaxDescriptionsPerPeriod))} — a period ceiling is zero or positive. Zero declares none at all, which is a supported deployment; a negative one describes no budget.";
        }

        if (this.MaxDescriptionsPerPeriodPerOwner < 0)
        {
            yield return $"{Key(nameof(this.MaxDescriptionsPerPeriodPerOwner))} — a per-owner ceiling is zero or positive. Zero declares no per-owner ceiling, which is what a deployment serving one owner wants; a negative one describes no budget.";
        }

        if (this.MaxDescriptionsPerPeriod != 0 && this.MaxDescriptionsPerPeriodPerOwner > this.MaxDescriptionsPerPeriod)
        {
            yield return $"{Key(nameof(this.MaxDescriptionsPerPeriodPerOwner))} — a per-owner ceiling is at most {nameof(this.MaxDescriptionsPerPeriod)}. Above it, it bounds nothing the deployment's own ceiling has not already bound, which reads as a limit somebody chose and refuses nothing.";
        }

        if (this.MaxRequestsPerMinute is < 0 or > GreatestRequestsPerMinute)
        {
            yield return $"{Key(nameof(this.MaxRequestsPerMinute))} — a rate is between 0 and {GreatestRequestsPerMinute} requests a minute. Zero paces nothing, which is the default; a rate above that is faster than any published chat quota and would pace nothing while reading as a bound somebody chose.";
        }
    }

    /// <summary>Names one key of this block the way an operator writes it, which is how every message above opens.</summary>
    private static string Key(string name) =>
        $"{EmbeddingOptions.SectionName}:{nameof(EmbeddingOptions.ImageDescription)}:{name}";
}
