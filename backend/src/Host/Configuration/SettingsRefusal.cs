// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration;

/// <summary>One configuration section a start would refuse, and what an operator has to change in it.</summary>
/// <remarks>
/// The section and the settings type are carried beside the sentences because the two readers of a refusal need
/// different halves of it: a start raises <see cref="Microsoft.Extensions.Options.OptionsValidationException" />, which
/// names both, and a configuration write reports the sentences to the administrator who wrote the change. Modelling it
/// as the exception itself would have made the second reader construct and catch one to read a list of strings.
/// </remarks>
/// <param name="SectionName">The configuration section the refusal is about.</param>
/// <param name="SettingsType">The type the section binds to, which the startup failure names.</param>
/// <param name="Errors">One sentence per thing an operator has to change.</param>
internal sealed record SettingsRefusal(string SectionName, Type SettingsType, IReadOnlyList<string> Errors);
