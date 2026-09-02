// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam;

/// <summary>Says what an attempt does about an occurrence that already carries a classification.</summary>
/// <remarks>
/// The distinction exists because reclassification is an explicit operation rather than something a configuration reload
/// sets off. A newer rule corpus, a moved threshold, or a scanner switched on all change what a classification would
/// say, and none of them is a reason for the deployment to start re-reading mail it already decided about — that is a
/// choice an operator makes, and this is where it is expressed.
/// </remarks>
public enum SpamClassificationMode
{
    /// <summary>Leave an occurrence that already carries a classification alone.</summary>
    FirstTimeOnly = 0,

    /// <summary>Evaluate the occurrence again and replace whatever was recorded for it.</summary>
    Reclassify = 1,
}
