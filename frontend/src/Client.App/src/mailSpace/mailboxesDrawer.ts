// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';

// The way to the mailboxes where they stand behind a drawer rather than in a column of their own, which is what every
// composition but the desktop's does with them. The design draws the control that opens it at the start of the list's
// own head row, beside the field that finds a message — and that row is drawn by the search, which the mail space is
// handed rather than draws. So the space, which owns the drawer, publishes the way to open it here, and the row reads
// it: `null` where the mailboxes stand in a column and there is no drawer to open.

export const MailboxesDrawerContext = createContext<(() => void) | null>(null);

export function useMailboxesDrawer(): (() => void) | null {
    return useContext(MailboxesDrawerContext);
}
