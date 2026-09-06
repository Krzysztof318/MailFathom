// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>Names what produced one mark, which is the distinction the trust model rests on.</summary>
/// <remarks>
/// <para>
/// Somebody deciding whether to act on a mark asks what said so before they ask what it says, and the answer is in the
/// record rather than inferred from which field is populated or from which build wrote the row. A deterministic rule
/// re-run over the same message says the same thing; a model promises no such thing, and a reader weighs the two
/// differently for exactly that reason.
/// </para>
/// <para>
/// A third member will name what a person approved, once this deployment records approvals. It is absent rather than
/// reserved: a member nothing ever writes is a state every reader has to rule out on every row.
/// </para>
/// <para>
/// It reaches a client by name rather than by number, for the reason <see cref="EmailEnrichmentAspect" /> does: what a
/// screen draws differently is the standing itself, and an ordinal names it to nobody reading the response.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<EmailEnrichmentSource>))]
public enum EmailEnrichmentSource
{
    /// <summary>A deterministic rule of this deployment's own, named by the rule.</summary>
    DeterministicRule = 0,

    /// <summary>A model, named by the agent the derivation was composed as.</summary>
    Model = 1,
}
