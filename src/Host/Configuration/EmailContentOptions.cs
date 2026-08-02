// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MailFathom.Host.Configuration;

/// <summary>Configures what one read of message bodies may return.</summary>
/// <remarks>
/// Two bounds, because a call names several emails and the count of them is bounded in code while the volume they return
/// is bounded here. Neither replaces the other: without the per-email bound one enormous message would spend a whole
/// call's budget, and without the budget ten messages would each return the per-email bound in full.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class EmailContentOptions : IValidatableObject
{
    /// <summary>Gets or sets the maximum number of characters one body representation returns.</summary>
    /// <remarks>
    /// <para>
    /// The range is about what a response can usefully carry rather than about what can be stored. The lower bound
    /// keeps a configured value from making every message look truncated, and the upper bound is where a single body
    /// stops fitting in the context of anything that would read it: a million characters is several times the largest
    /// context an MCP client can be expected to hold, so a body above it would be discarded by the caller rather than
    /// read.
    /// </para>
    /// <para>
    /// It is not the bound that protects this process. A body can only be as large as the raw MIME it was stored
    /// under, which <c>MailSynchronization:MaxRawMimeBytes</c> already limits; this decides how much of it a caller is
    /// handed.
    /// </para>
    /// </remarks>
    [Range(1_000, 1_000_000)]
    public int MaxBodyCharacters { get; set; } = 100_000;

    /// <summary>Gets or sets the maximum number of body characters one call returns across every email it names.</summary>
    /// <remarks>
    /// <para>
    /// It is the volume half of the control on how much mail one protocol call can draw out of a mailbox, and nothing a
    /// request carries can raise it. The budget is spent in the order the emails were named, so a call whose first
    /// emails are large returns less of the later ones and says so on each representation it had to cut.
    /// </para>
    /// <para>
    /// The upper bound is twice the largest per-representation bound, which is what one email asking for both
    /// representations can return; a budget above it would bound nothing that the per-email bound and the count of ten
    /// do not already bound.
    /// </para>
    /// </remarks>
    [Range(2_000, 2_000_000)]
    public int MaxCharactersPerRead { get; set; } = 200_000;

    /// <summary>Checks the one rule neither range can state on its own.</summary>
    /// <param name="validationContext">The context the options framework validates against.</param>
    /// <returns>The failure when the budget cannot cover a single email, or nothing when the two agree.</returns>
    /// <remarks>
    /// A single email asking for both representations may return <see cref="MaxBodyCharacters" /> twice, so a budget
    /// below that would cut a one-email call by a limit that exists for calls naming several — and the truncation it
    /// reported would send a caller to split a call it cannot split further. Startup is where that is caught, because
    /// the alternative is a deployment discovering it one read at a time.
    /// </remarks>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        this.MaxCharactersPerRead < 2 * (long)this.MaxBodyCharacters
            ? [new ValidationResult(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "EmailContent:MaxCharactersPerRead must be at least twice EmailContent:MaxBodyCharacters, which is {0}.",
                    2 * (long)this.MaxBodyCharacters),
                [nameof(this.MaxCharactersPerRead)])]
            : [];
}
