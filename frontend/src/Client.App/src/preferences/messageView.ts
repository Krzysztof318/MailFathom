// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';

// Which of the two reading surfaces a message is drawn on, reached by the components that draw one.
//
// A context rather than a prop, and that is the exception the rule in `frontend/src/AGENTS.md` § *Components* names
// rather than a way around it: the value decides how *every* message anywhere in the client is drawn, and the two
// components that read it — the body and the head's control — sit three and four levels below the frame that holds the
// preference, behind components that would have to carry a value none of them has anything to do with.
//
// It carries the answer alone rather than the whole preferences object, because that is what a message needs: a screen
// that could reach `chooseMessageView` from here would be one that could change the setting from inside a message.

/**
 * Whether an open message draws the sender's own markup inline, which a tree with no provider above it reads as `false`.
 *
 * The reduced text is the default for the reason the deployment's own unset answer is: it is what this client has
 * always drawn, and a test or a screen mounted without the frame draws what somebody who has set nothing would see.
 */
export const EmbeddedHtmlMessagesContext = createContext(false);

/** Whether the message being drawn shows the sender's own markup inline rather than the reduced document tree. */
export function useEmbeddedHtmlMessages(): boolean {
    return useContext(EmbeddedHtmlMessagesContext);
}
