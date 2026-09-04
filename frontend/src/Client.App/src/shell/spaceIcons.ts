// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { IconName } from '../controls/icons';
import type { Space } from '../routing/spaces';

// What each space is drawn by. It sits beside the two components that draw one rather than inside either, because a
// space keeps its symbol wherever it is offered — in the rail, in the bottom bar, and in the overflow the bar reaches
// what it has no room for through. A second copy of this table is how one of the three comes to draw Tasks as
// something else.

/** The symbol each space carries. Exhaustive by its own type, so a new space fails to compile until it has one. */
export const spaceIcons: Readonly<Record<Space, IconName>> = {
    discover: 'explore',
    mail: 'mail',
    cases: 'topic',
    agent: 'auto_awesome',
    tasks: 'task_alt',
    calendar: 'calendar_month',
    people: 'group',
};
