// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';

// The head row of the mail column, which the design project draws as one line: the way to the mailboxes, the search
// field, what searches it, and the control that opens the list's filters. The first three belong to the search, which
// draws the row; the last belongs to the list, which owns the filters it opens and stands a component below the row.
//
// What crosses between them is the place in the row rather than the state: the search offers an element at the end of
// its row, and the list renders its control into it. So the filters stay the list's — what is narrowed, what the
// control counts, and what the panel changes are one component's — and the row stays the search's, and neither has to
// hold the other's state to draw one line.
//
// `null` is a tree with no row above the list, where the list draws the control in its own header instead.

export const ListHeadRowContext = createContext<HTMLElement | null>(null);

export function useListHeadRow(): HTMLElement | null {
    return useContext(ListHeadRowContext);
}
