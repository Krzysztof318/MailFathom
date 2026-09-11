// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type { MailFolderRole, MailTimelineEntry } from '@mailfathom/client-backend';
import type { ActRefusal, MoveDestination, MoveDestinationGroup } from './mailboxDestinations';

// The things a person does to their own mailbox from the Mail space, as an operation on messages rather than as
// anything on a screen. It belongs to the application rather than to the toolbar, because five surfaces reach the same
// acts — the toolbar over what is open, the bar over a selection, the row that draws one as pending, the message
// somebody swiped, and the head of the message being read — and a second implementation of *archive* is how two of
// them come to file mail differently.
//
// Nothing here reaches a mail server. Each act writes a durable record through `/api/client` and answers; the account's
// own convergence pass is what tells the server, which is why an unreachable account leaves an act pending rather than
// failing it, and why what this holds is what was asked for rather than what has been observed.
//
// The context and its hook sit apart from the provider that fills them for the reason `workspace/useWorkspace.ts`
// gives: a module Vite hot-reloads may export components alone.

/**
 * The acts, named for what a person asked for rather than for the route each travels.
 *
 * Three of them are folder moves and three are flags, which is a fact about the mail server rather than about the
 * screen: a control says *archive*, and where that lands is `mailboxDestinations.ts`'s to answer.
 *
 * Taking a flag off is an act of its own rather than a direction passed to the one that puts it on. What a surface
 * holds afterwards is the act it asked for, and only the act says which side of the change the message is converging
 * towards — so a single name would leave a message asked to be unflagged reading as one still waiting to be flagged.
 */
export type MailboxAct = 'flag' | 'unflag' | 'markRead' | 'markUnread' | 'archive' | 'delete' | 'move';

/** The acts that write one of the two flags a mail server keeps, which are the ones no folder is involved in. */
export type FlagAct = 'flag' | 'unflag' | 'markRead' | 'markUnread';

/** The rest: the acts that file the message in another folder, which are the ones a folder is involved in. */
export type FilingAct = Exclude<MailboxAct, FlagAct>;

/** Whether the act writes one of those flags rather than filing the message somewhere else. */
export function changesAFlag(act: MailboxAct): act is FlagAct {
    return act === 'flag' || act === 'unflag' || act === 'markRead' || act === 'markUnread';
}

/** One message an act is about: what names it, and where it is, which is what filing and taking that back both need. */
export interface ActedMessage {
    readonly storedEmailId: string;
    readonly account: string;

    /** The folder the message is in, which is where taking a move back puts it. */
    readonly folder: string;

    /**
     * Whether the deployment last reported the message without `\Seen`.
     *
     * Carried with the message because one act's *direction* is the message's own state rather than a control's: the
     * read control offers to mark read or to mark unread depending on where the messages under it stand, and a strip
     * standing over a selection knows nothing about those messages except what is written down here. It is what the
     * deployment answered rather than what a row draws — `drawnActs.ts` reads this against what this client has marked
     * since, which is the one place that correction belongs.
     */
    readonly unread: boolean;
}

/**
 * One act as it was asked for: what was asked, where it was asked, and whether it takes the message out of there.
 *
 * The folder is part of it because an act is about a message *in a place*, and the sentence a row wears belongs to the
 * place the act was asked from. Without it a message somebody filed into the trash goes on saying `Moving to the
 * trash…` while it sits in the trash — a row reporting an act against the folder it already landed in.
 *
 * Whether it leaves is the other half of the same fact, and it is what the list acts on: an act a person performs is
 * theirs, so the message goes from the folder it is leaving at the press rather than when a mailbox is seen to agree.
 * A row that is gone says nothing, which is why nothing draws a sentence for one of these.
 */
export interface AskedAct {
    readonly act: MailboxAct;

    /** The folder the act was asked in, which is the only folder its sentence is drawn in. */
    readonly from: string;

    /** Whether the act takes the message out of that folder rather than leaving it there. */
    readonly leaves: boolean;
}

export interface MailboxActs {
    /**
     * What this client has asked for and the deployment has not been seen to have applied, by the message it is about.
     *
     * Held rather than derived, for the reason `readMarking/useReadMarking.ts` gives about its own: a mutation is
     * durable the moment it is written down and converges minutes later, so a screen drawing only what the deployment
     * last reported would show mail somebody has just filed as though nothing had happened. It goes when the tab does.
     */
    readonly asked: ReadonlyMap<string, AskedAct>;

    /** Why the act cannot be performed on those messages, or `null` where it can. */
    readonly refusalOf: (act: MailboxAct, messages: readonly ActedMessage[]) => ActRefusal | null;

    /**
     * The role the folder that message is in plays, or `null` where it plays none and where this client has not read
     * the user's folders at all.
     *
     * Answered here because this is where the folders are already held, and asked because opening a message is not one
     * behaviour for every folder: a message in a drafts folder is something somebody was writing, and it opens in the
     * composer rather than in the reading pane. The question is about where a message sits rather than about what may
     * be done to it, which is the one thing here that is not an act — kept together with the acts because a second
     * reader of `/folders` costs every session a request, which the note in `MailboxActs.tsx` already weighs.
     *
     * A session whose credential may not file mail reads no folders, so every message answers `null` there and a draft
     * opens as a message. That is the same limitation as the acts themselves being refused, and it goes when the read
     * does.
     */
    readonly folderRoleOf: (message: ActedMessage) => MailFolderRole | null;

    /** The folders those messages could be filed into, under the one account they are all in. */
    readonly destinationsOf: (messages: readonly ActedMessage[]) => readonly MoveDestinationGroup[];

    /**
     * Whether `delete` would destroy those messages rather than file them in the trash.
     *
     * Asked here rather than worked out per surface, because the folders it is read from are the provider's and the two
     * surfaces that need the answer — the question standing in front of the act, and the report of what it came to —
     * would otherwise each hold a copy of the rule and could disagree about which act somebody just performed.
     */
    readonly deletesPermanently: (messages: readonly ActedMessage[]) => boolean;

    /**
     * Performs an act, reports what it was written down for through the toast surface, and hands what it came to to the
     * pending-changes queue, which says what was refused and follows what was not.
     *
     * Safe to call for an act `refusalOf` refuses: nothing is submitted, so a control that was drawn before the answer
     * arrived cannot file mail into a folder that is not there.
     */
    readonly perform: (act: MailboxAct, messages: readonly ActedMessage[], destination?: MoveDestination) => void;
}

/**
 * What a tree with no provider above it reads, which is a client that changes nothing.
 *
 * A default rather than the refusal `useWorkspace` raises, because changing nothing is a state this application really
 * has — no session, a credential without the grant, or a deployment that serves no mail — and every one of them draws
 * the same client. Nothing below distinguishes them, which is the point.
 */
export const nothingActed: MailboxActs = {
    asked: new Map(),
    refusalOf: () => 'notOffered',
    folderRoleOf: () => null,
    destinationsOf: () => [],
    deletesPermanently: () => false,
    perform: () => undefined,
};

export const MailboxActsContext = createContext<MailboxActs>(nothingActed);

export function useMailboxActs(): MailboxActs {
    return useContext(MailboxActsContext);
}

/**
 * Whether opening that message is opening a draft, which is the composer rather than the reading pane.
 *
 * A draft is a message somebody was writing, so pressing it puts them back where they were writing it. What decides
 * that is the role its folder plays rather than anything on the message: a mail server files a draft where its own
 * configuration says drafts go, and a folder called `Entwürfe` is the drafts folder as surely as one called `Drafts`.
 *
 * Stated once, beside the acts, because two lists open a message — the mailbox's own and the search's — and a second
 * reading of *this is a draft* is how the two come to open the same message differently.
 *
 * A workspace whose folders have not arrived yet names no role, so a message pressed in the window between it mounting
 * and that read landing opens as mail. The list and the folders are read in the same render pass, and a press that does
 * nothing while a second read finishes is worse than the surface this opens instead — so what closes the window is the
 * one shared reading of the folders `MailboxActs.tsx` already carries as its debt, rather than a wait on the press.
 *
 * ponytail: a press classified against a folder tree that may not have landed. The single `/folders` read the client
 * shares is the upgrade, and it closes this window in the same move as it closes the second read.
 */
export function opensAsDraft(acts: MailboxActs, email: MailTimelineEntry): boolean {
    return (
        acts.folderRoleOf({
            storedEmailId: email.id,
            account: email.account,
            folder: email.folder,
            unread: email.unread,
        }) === 'Drafts'
    );
}

/**
 * The act a row is still waiting on, or `null` where it is waiting on none.
 *
 * Nothing polls for convergence: the row itself is what says the change arrived. A flag this client asked for is
 * pending until the deployment reports the message flagged, one it asked to have taken off is pending until the
 * deployment reports it unflagged, and a message asked to be marked unread is pending until it is reported unread — so
 * the sentence goes on its own the moment the account's pass has been round.
 *
 * **An act is only pending where it was asked.** The three that file a message elsewhere take the row out of the folder
 * they are leaving at the press, and the message then arrives somewhere else — where the act is finished rather than
 * waiting, whatever this client has or has not seen. So a row drawn in a folder the act did not act in wears no
 * sentence about it, which is the same rule read from the other end.
 */
export function actPending(acts: MailboxActs, email: MailTimelineEntry): AskedAct | null {
    const asked = acts.asked.get(email.id);

    if (asked?.from !== email.folder) {
        return null;
    }

    if (asked.act === 'flag') {
        return email.flagged ? null : asked;
    }

    if (asked.act === 'unflag') {
        return email.flagged ? asked : null;
    }

    if (asked.act === 'markUnread') {
        return email.unread ? null : asked;
    }

    if (asked.act === 'markRead') {
        return email.unread ? asked : null;
    }

    return asked;
}

/**
 * Whether the row is drawn flagged: what the deployment last reported, less what this client has asked since.
 *
 * **The flag appears at the press and goes at the press**, which is the whole of what either act reports. A mailbox
 * mutation is durable the moment it is written down and converges minutes later, so a mark drawn from the observation
 * alone would leave somebody who pressed *flag* looking at an unflagged row and pressing it again — and a sentence in
 * the row's own line saying the flag is on its way is the client narrating a mechanism instead of showing an outcome.
 * The design draws neither: it draws the flag.
 *
 * Read here rather than in the component that draws the mark, for the reason `drawnUnread` is read out of
 * `readMarking/`: the conversation draws the same marks about the same message, and a second reading of *this message
 * is flagged* is how two screens come to disagree about one message.
 */
export function drawnFlagged(acts: MailboxActs, email: MailTimelineEntry): boolean {
    const asked = actPending(acts, email);

    if (asked?.act === 'flag') {
        return true;
    }

    if (asked?.act === 'unflag') {
        return false;
    }

    return email.flagged;
}

/**
 * Whether this row has been asked to leave the folder it is drawn in.
 *
 * **An act a person performs is theirs, and the screen follows the person.** So a message archived, filed, or sent to
 * the trash is out of the folder it was in from the press, rather than when a mailbox is next seen to agree — which is
 * a read the deployment answers with the pre-change state anyway, and then never re-reads.
 *
 * It is asked of the row rather than of a message, because the act took the message out of *that* folder and out of no
 * other: the same message drawn in the folder it was filed into has arrived there, and belongs in that list.
 */
export function actLeaving(acts: MailboxActs, email: MailTimelineEntry): boolean {
    const asked = acts.asked.get(email.id);

    return asked !== undefined && asked.leaves && asked.from === email.folder;
}
