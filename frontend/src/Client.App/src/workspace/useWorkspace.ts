// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import { everything, type MailScope } from './mailScope';
import type { OpenConversation } from './openConversation';

// What a person carries between the spaces. Discover, Mail, and Cases are one application rather than three, and this
// is what makes them one: the frame owns it, so moving to another space re-renders what is under the frame and leaves
// every value below untouched.
//
// The context and its hook sit apart from the provider that fills them for the reason `localization/useLocalization.ts`
// gives: a module Vite hot-reloads may export components alone.

export interface Workspace {
    /** What the list, the search, and the next question are all asked against, owned here rather than per screen. */
    readonly scope: MailScope;

    /**
     * The rows of the folder tree somebody has folded away, by the key each row is identified with.
     *
     * Folded rather than unfolded, so a tree nobody has touched shows what is in it: an owner who opens the client and
     * sees a column of closed mailboxes has to open every one of them before the client says anything.
     */
    readonly collapsed: readonly string[];

    /**
     * Whether the mailbox column is folded to its icon rail rather than drawn at the width that carries names.
     *
     * Beside the rows somebody folded rather than inside the column that draws it, because two things read it: the
     * composition, which decides how wide the column is, and the tree inside it, which decides whether a row is a name
     * or a symbol. It is a second axis rather than the same one — folding a mailbox away hides its folders, and folding
     * the column narrows every row that is still there — which is why neither value is derivable from the other.
     */
    readonly mailboxesFolded: boolean;

    /** What the person has open, once a space offers something to open. */
    readonly selection: string | null;

    /**
     * The conversation being read in front of what is open, or `null` where a single message is what is being read.
     *
     * Beside the selection rather than instead of it, because the way out of a conversation is the message it was
     * opened from: holding both is what makes closing one a return rather than a second thing to remember.
     */
    readonly conversation: OpenConversation | null;

    /**
     * The part of what is open that a question would be asked about, or `null` where the whole of it is.
     *
     * It is the words a person selected rather than a position in anything, because what the intent field does with it
     * is quote it: a range would have to be resolved against a document that is drawn again on every read, and against
     * the same message read a second time under a different ask.
     */
    readonly fragment: string | null;

    /**
     * The messages the person has picked out, in the order the list draws them.
     *
     * Here rather than inside the list because *select and ask* is what it is for: the question asked of a selection is
     * composed somewhere the list is not, so a selection the list kept to itself would be a visual state nothing else
     * could read as scope.
     */
    readonly selected: readonly string[];

    /** What has been typed into the intent field, which the next question would be asked with. */
    readonly question: string;

    /**
     * What was searched for before, newest first, so a search is one press rather than something to retype.
     *
     * Here rather than inside the search screen because it has to outlive one: the column the search stands in is
     * mounted afresh whenever the mailbox in scope changes, and what somebody looked for is not something a change of
     * folder should forget. It is also what makes these go with the credential — the frame empties the whole workspace
     * when one is let go, and a list of what a person searched for is theirs.
     */
    readonly recentSearches: readonly string[];
}

export interface WorkspaceRevision {
    readonly workspace: Workspace;

    /** Changes the named parts and leaves the rest of the workspace as it was. */
    readonly revise: (change: Partial<Workspace>) => void;
}

export const emptyWorkspace: Workspace = {
    scope: everything,
    collapsed: [],
    mailboxesFolded: false,
    selection: null,
    conversation: null,
    fragment: null,
    selected: [],
    question: '',
    recentSearches: [],
};

export const WorkspaceContext = createContext<WorkspaceRevision | null>(null);

export function useWorkspace(): WorkspaceRevision {
    const revision = useContext(WorkspaceContext);

    if (revision === null) {
        throw new Error('A component read the workspace outside the WorkspaceProvider that main.tsx mounts.');
    }

    return revision;
}
