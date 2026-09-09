// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';

// Whether the folder tree carries the standing views of what MailFathom read, reached by the section that draws them.
//
// A context rather than a prop, on the exception `preferences/messageView.ts` takes and for the same reason: the
// section sits three components below the frame that holds the preference, behind a space, a mail screen and a column
// that each have nothing to do with it.
//
// It carries the answer alone rather than the whole preferences object, because that is what the section needs: the
// control that moves it belongs to the settings screen, and a tree that could reach the setter from here would be one
// where the section turned itself off.

/**
 * Whether the tree draws the standing views, which a tree with no provider above it reads as `true`.
 *
 * Drawn is the default for the reason the deployment's own unset answer is: the section is part of the tree the design
 * project draws, so it is something somebody turns off rather than something they go looking for.
 */
export const AiFiltersShownContext = createContext(true);

/** Whether the folder tree draws the standing views of what a derivation read in the mail. */
export function useAiFiltersShown(): boolean {
    return useContext(AiFiltersShownContext);
}
