// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>How this deployment's answering endpoint is named to the person whose question it answered.</summary>
/// <param name="Alias">The deployment's own name for the endpoint, which is what its logs, its metrics, and its failures already call it.</param>
/// <param name="PublishedModel">The model name the operator declared for publication, and empty where they declared none.</param>
/// <remarks>
/// <para>
/// <strong>Both halves are declarations rather than discoveries.</strong>
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md">ADR 0022</see>
/// settles that the routed model name is never published: for a cloud deployment it is the operator's own resource
/// name, which can carry a tenant, a project, or an environment in it, so publishing it to every signed-in client would
/// disclose deployment topology to answer a question about model quality. What may be published is a second name the
/// operator writes for exactly that purpose, and empty publishes nothing beyond the alias.
/// </para>
/// <para>
/// It is a value in this layer rather than the endpoint record itself, because that record carries an address and a
/// routing name and neither may be written down. What travels is a pair of names somebody chose.
/// </para>
/// <para>
/// Absent altogether on a deployment that declares no chat endpoint, which is the deployment that answers no questions
/// at all — so a run there is refused before there is anything to attribute.
/// </para>
/// </remarks>
public sealed record AnsweringEndpointIdentity(string Alias, string PublishedModel);
