// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Evaluations.Providers;

/// <summary>The one endpoint a run reaches, for every model it measures and for the judge that grades them.</summary>
/// <remarks>
/// <para>
/// One address and one key rather than a pair per role, under the names the provider-contract tests already read —
/// <c>vars.CHAT_PROVIDER_ADDRESS</c> and <c>secrets.CHAT_PROVIDER_API_KEY</c>. A judge is a model like any other and is
/// reached from the same account, so a second credential would be a second thing to rotate for no difference a run can
/// observe. What still distinguishes the judge is its model, which is declared apart and pinned.
/// </para>
/// <para>
/// The address is optional because a provider's own default is a valid answer; the key is not, and a requested run
/// without one fails naming it rather than skipping.
/// </para>
/// </remarks>
internal static class EvaluationEndpoint
{
    /// <summary>The variable carrying the address every request goes to.</summary>
    public const string AddressVariable = "MAILFATHOM_CHAT_ADDRESS";

    /// <summary>The variable carrying the key every request authenticates with.</summary>
    public const string ApiKeyVariable = "MAILFATHOM_CHAT_API_KEY";

    /// <summary>Reads where requests go.</summary>
    /// <returns>The address, or <see langword="null" /> where the run left the provider's own default.</returns>
    public static Uri? Address() =>
        AiEvaluationRun.Optional(AddressVariable) is { } address ? new Uri(address, UriKind.Absolute) : null;

    /// <summary>Reads the key every request is authenticated with.</summary>
    /// <returns>The key.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run was asked for without one.</exception>
    public static string ApiKey() => AiEvaluationRun.Required(ApiKeyVariable);
}
