// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;

namespace MailFathom.AI.BodyCleanup;

/// <summary>The plan this pass runs on, which is the deployment's chat plan with this pass's own model in it.</summary>
/// <param name="Plan">Where the call goes and what it may spend.</param>
/// <remarks>
/// <para>
/// A type of its own rather than the registered <see cref="ChatGenerationPlan" />, because this is the one pass in the
/// product that may route to a model other than the one a question runs on: cleaning a body is a judgement a small fast
/// model makes well, and an operator should not have to move <c>Chat:Model</c> — which is what answers questions — to get
/// it. Resolving the shared plan here would silently ignore that declaration.
/// </para>
/// <para>
/// Everything else in it is the deployment's: the address, the credential's alias, the API, the bounds, the timeout, and
/// the reasoning effort. A reduced effort was measured to halve the output while losing exactly the hardest judgement, so
/// effort is not something this pass trades for latency and is not declared separately from the endpoint's own.
/// </para>
/// </remarks>
public sealed record MailBodyCleanupPlan(ChatGenerationPlan Plan);
