// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Tasks;

/// <summary>Why a day was not laid out, where nothing about the day itself decided it.</summary>
/// <remarks>
/// Each member is a condition the person can be told about plainly and none of them is a defect: a deployment that
/// declared no model, an allowance that is spent for this period, and a provider that did not answer are three
/// different sentences on a screen and three different things to do next. A day with nothing in it is not here,
/// because an empty arrangement is an answer rather than a refusal.
/// </remarks>
public enum DayLayoutWithholding
{
    /// <summary>This deployment declared no model to lay a day out with.</summary>
    NotActivated = 0,

    /// <summary>What this period may spend is already spent, so the arrangement waits for the next one.</summary>
    AllowanceExhausted = 1,

    /// <summary>The provider failed, timed out, or could not be reached.</summary>
    ProviderUnavailable = 2,
}
