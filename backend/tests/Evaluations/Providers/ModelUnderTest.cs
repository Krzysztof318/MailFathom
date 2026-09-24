// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Providers;

namespace MailFathom.Evaluations.Providers;

/// <summary>One model a run measures: the plan a deployment declaring it would build, and the headers every request to it carries.</summary>
/// <param name="Plan">The plan, whose endpoint alias is the name the model's results and cached answers are filed under.</param>
/// <param name="ExtraHeaders">The headers every request carries beside the key, empty where the run declared none.</param>
/// <remarks>
/// The headers are beside the plan rather than in it for the reason a deployment keeps them apart: the plan is what a
/// request asks for, and the headers are how it reaches the provider, which is the credential's half.
/// </remarks>
internal sealed record ModelUnderTest(ChatGenerationPlan Plan, IReadOnlyList<ProviderEndpointHeader> ExtraHeaders)
{
    /// <summary>Gets the name the model's results are filed under.</summary>
    public string Name => this.Plan.Endpoint.Alias;
}
