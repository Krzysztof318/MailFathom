// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type { AskedQuestion, SelectedFragment } from './askScope';
import type { MailScope } from './mailScope';
import type { CitedAttachment, OpenedAttachment } from './openAttachment';
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
     * The rows of the folder tree somebody has moved away from the fold they open at, by each row's own key.
     *
     * Two kinds of row open two ways: a mailbox opens showing its folders, because a column of closed mailboxes says
     * nothing until every one of them has been pressed, and a folder opens with its subfolders away, because a
     * mailbox filed three levels deep would otherwise arrive as everything it has ever held. So what is recorded is
     * the move rather than the state — one set covering both directions, where two sets could disagree.
     */
    readonly foldsToggled: readonly string[];

    /**
     * Whether the mailbox column is folded to its icon rail rather than drawn at the width that carries names.
     *
     * Beside the rows somebody folded rather than inside the column that draws it, because two things read it: the
     * composition, which decides how wide the column is, and the tree inside it, which decides whether a row is a name
     * or a symbol. It is a second axis rather than the same one — folding a mailbox away hides its folders, and folding
     * the column narrows every row that is still there — which is why neither value is derivable from the other.
     */
    readonly mailboxesFolded: boolean;

    /**
     * Whether the panels the design draws around a conversation are hidden, leaving the correspondence alone.
     *
     * The design's *fullscreen* toolbar control is the only thing that sets it, and it is a setting for the Mail space
     * rather than for one message — so it holds as the reader moves from one thread to the next, which is what makes
     * it worth a control at all. It is here beside the folded column for the same reason that one is: several things
     * read it, and none of them owns it.
     *
     * What is hidden depends on the composition, which is the reader's rather than this value's: the head of the
     * thread goes on the desktop and the tablet, and a single-pane composition keeps it whatever this says, because
     * there the head carries the way back to the list.
     */
    readonly panelsHidden: boolean;

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
     * The message whose own markup is being shown on the full-HTML surface, or `null` where nobody asked for one.
     *
     * Beside the selection for the same reason a conversation is, and never written down anywhere else: the reader's
     * consent to be shown a stranger's markup is given per message and outlives nothing, so a reload finds it gone and
     * asks again. `rememberedWorkspace` is where that is enforced rather than here, because that is the only place
     * this value could otherwise survive one.
     */
    readonly fullHtml: string | null;

    /**
     * The file being read in front of what is open, or `null` where the message itself is what is being read.
     *
     * Beside the selection for the same reason the conversation is: the way out of a file is the message it was opened
     * from, so holding both is what makes closing it a return. The two are never both on the screen — a file opened
     * from a message inside a conversation stands in front of the conversation as well — which is why the viewer reads
     * this before either of them rather than beside them.
     */
    readonly attachment: OpenedAttachment | null;

    /**
     * The file a search result cited, waiting for the message it belongs to to be read, or `null` where nothing is.
     *
     * A search row cites a file by its position and knows nothing else about it: what a citation carries is a
     * coordinate, and opening a file needs the size the message declared for it, which only a read of that message
     * publishes. So following a citation is two steps rather than one — the message opens, and the pane that read it
     * opens the file — and this is the half-finished act between them. Any message read spends it, so one abandoned by
     * reading something else opens nothing later, and it is not remembered across a reload for the same reason the open
     * file is not.
     */
    readonly citedAttachment: CitedAttachment | null;

    /**
     * The part of what is open that a question would be asked about, or `null` where the whole of it is.
     *
     * It is the words a person selected rather than a position in anything, because what the intent field does with it
     * is quote it: a range would have to be resolved against a document that is drawn again on every read, and against
     * the same message read a second time under a different ask.
     *
     * It names the message the words were taken from as well as the words, because a passage is a scope of its own and
     * the message around it is the next one out — a fragment that could not say which message it belonged to could
     * neither be widened to that message nor be told apart from one taken out of a different one.
     */
    readonly fragment: SelectedFragment | null;

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
     * What the intent field was pointed at, by its key, or `null` where the field asks about what the mail space shows.
     *
     * Beside the scope the list is read under rather than instead of it, because the two answer different questions:
     * one says what is drawn, and the other what the next question is about. Widening a question after an answer that
     * was too narrow, and narrowing it to the paragraph in front of somebody, are both exactly the second changing
     * while the first does not — so either keeps the folder they were reading and the messages they had picked out.
     *
     * A key rather than the scope, because it means nothing except against what the field is offering: `askScope.ts`
     * resolves it there and falls back to the screen where the thing it named has gone.
     */
    readonly askScopeKey: string | null;

    /**
     * What was asked before, newest first, each with the scope it was asked under.
     *
     * Here rather than in the space that answers, for the reason the searches are: the field is drawn in every space
     * and an answer is drawn in one, so a list the answer held would be empty everywhere a question is composed. It is
     * a list of questions rather than a conversation — what somebody does with one is ask it again, usually wider —
     * and it goes with the credential like everything else the workspace holds, because what a person asked their own
     * mail is theirs.
     */
    readonly askedBefore: readonly AskedQuestion[];

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

// Where a workspace nobody has chosen a mailbox in stands, which is the inbox rather than the widest scope. It is
// stated as the role scope rather than resolved from a directory nothing has read yet, and that is what makes the two
// cases the design asks for fall out of one value: with several accounts the tree draws the row that is every inbox at
// once and this names it, and with one it draws no such row — so `folders/FolderTree.tsx` finds a scope naming nothing
// drawn and lands on `openingScope`, which is that account's own inbox. `everything` here was the defect behind both:
// it is a row the tree draws whenever there is more than one account, so the fallback never fired and the client opened
// on every folder of every account, sent and deleted mail among them.
export const emptyWorkspace: Workspace = {
    scope: { kind: 'role', role: 'Inbox' },
    foldsToggled: [],
    mailboxesFolded: false,
    panelsHidden: false,
    selection: null,
    conversation: null,
    fullHtml: null,
    attachment: null,
    citedAttachment: null,
    fragment: null,
    selected: [],
    question: '',
    askScopeKey: null,
    askedBefore: [],
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
