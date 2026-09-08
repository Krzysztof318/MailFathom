// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    discardMailDraft,
    reviseMailDraft,
    sendMailDraft,
    stageMailDraftAttachment,
    unstageMailDraftAttachment,
    withdrawOutgoingMail,
    writeMailDraft,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type MailSendRefusal,
    type MailSendWithdrawal,
    type MailStagedAttachment,
} from '@mailfathom/client-backend';
import { useAttachmentUpload } from '../deployment/attachmentUpload';
import { wireComposition, type Composition } from './composition';

// What the deployment holds for the message being written, and the five acts that change it. It is a hook rather than
// state inside the composer because none of it is what is on the screen: the draft in the user's own drafts folder,
// the files staged against it, and what became of a send are all the deployment's, and each act is a sequence with an
// outcome rather than a value to render.
//
// **Attaching and sending both need a draft the deployment holds**, because a file is staged against one and a send is
// queued from one. So each saves first where nothing has been saved yet, which is what makes the design's two controls
// work without a third that says "save before attaching" — and it is still a save the person asked for, because
// attaching and sending are both acts they asked for.

// Which act asked for the write, which is not the same question as which request went out: attaching and sending both
// write the draft first, and a refusal met there is the refusal that act met. Only two words exist for one because
// only two are true of the person — they pressed save, or they pressed send — and an attach is a save with a file
// behind it.
type DraftAct = 'save' | 'send';

/** What the composer is doing about the deployment, which is one piece of state rather than a set of flags. */
export type DraftStanding =
    | { readonly kind: 'held' }
    | { readonly kind: 'saving' }
    | { readonly kind: 'saved' }
    | { readonly kind: 'attaching'; readonly fileName: string }
    | { readonly kind: 'sending' }
    | { readonly kind: 'queued'; readonly outgoingEmailId: string }
    | { readonly kind: 'withdrawn'; readonly withdrawal: MailSendWithdrawal }
    | { readonly kind: 'refused'; readonly refusal: MailSendRefusal }
    | { readonly kind: 'refusedSave'; readonly refusal: MailSendRefusal }
    | { readonly kind: 'failed'; readonly reason: ClientFailureReason };

/** The draft the deployment holds for what is being written, and what a person does to it. */
export interface DraftAtDeployment {
    readonly standing: DraftStanding;

    /** The files staged against it, oldest first, which is nothing until one is attached. */
    readonly staged: readonly MailStagedAttachment[];

    /** Files the draft in the user's own drafts folder, creating it where nothing has been saved yet. */
    readonly save: (composition: Composition) => Promise<boolean>;

    /** Stages one file against the draft, saving it first where nothing has been saved yet. */
    readonly attach: (composition: Composition, file: File) => Promise<void>;

    /** Takes one staged file back off. */
    readonly unstage: (attachmentId: string) => Promise<void>;

    /**
     * Queues the message, saving what has been written since first, and answers what it came to.
     *
     * The standing is answered as well as held because the surface says what became of a send where the design project
     * says it — in a toast, which stands over whatever the person moved on to rather than at the foot of the composer.
     * A toast is raised at the moment of the outcome rather than watched for, so what is owed to the caller is the
     * outcome itself; reading it back out of state afterwards would raise a second toast on every later render.
     */
    readonly send: (composition: Composition) => Promise<DraftStanding>;

    /**
     * Takes a queued send back while it has not begun transmitting, and answers what that came to.
     *
     * Asked before the deployment has answered the send, it withdraws the message the moment there is one to withdraw
     * rather than doing nothing: somebody who said stop while it was still going out said it about this message, and
     * asking them to say it again once a round trip has landed is a stop the client quietly dropped.
     */
    readonly withdraw: () => Promise<DraftStanding>;

    /** Gives the draft up, taking its copies back out of the user's drafts folder. */
    readonly discard: () => Promise<boolean>;
}

export function useDraftAtDeployment(session: ClientSession, transport: MailFathomTransport): DraftAtDeployment {
    const upload = useAttachmentUpload();
    const [standing, setStanding] = useState<DraftStanding>({ kind: 'held' });
    const [staged, setStaged] = useState<readonly MailStagedAttachment[]>([]);

    // The draft the deployment holds, as a ref rather than as state: two acts in the same turn have to see the
    // identifier the first of them wrote, and nothing on the screen is drawn from it.
    const draftId = useRef<string | null>(null);

    // Which change to the staged files is the newest. A save answers with the whole list the deployment holds, so
    // one that started before a file was taken off would put that file back on the screen when it lands afterwards —
    // the deployment having discarded it already, and the screen then saying a message carries something it does not.
    // Every act that changes the list takes a number on the way out and writes only while it is still the newest.
    const staging = useRef(0);

    // The save in flight, if any. `saved` reads `draftId.current` and only writes it back once its own request has
    // answered, so two acts starting inside that window would each write a draft and strand whatever the loser staged
    // against it. A second caller joins the first rather than starting a second write.
    const saving = useRef<Promise<string | null> | null>(null);
    const queued = useRef<string | null>(null);
    const uploading = useRef<AbortController | null>(null);

    // Whether somebody has asked to stop the send while the deployment had not yet answered it. A ref rather than
    // state because nothing draws it: what reads it is the send itself, one turn later, and a value read out of a
    // render would be the one from before they asked.
    const stopping = useRef(false);

    // What the standing already is, beside the state that draws it. An act answers what it came to, and two acts in
    // one turn — a stop landing on a send, a refusal met while writing the draft a send needed — have to read what the
    // one before them settled on rather than the value this render was given.
    const settledAs = useRef<DraftStanding>({ kind: 'held' });

    function hold(next: DraftStanding): DraftStanding {
        settledAs.current = next;
        setStanding(next);

        return next;
    }

    // An upload whose composer has gone is an upload nobody is waiting for, and letting it finish would stage a file
    // against a draft the author has closed.
    useEffect(
        () => () => {
            uploading.current?.abort();
        },
        [],
    );

    function saved(composition: Composition, asked: DraftAct): Promise<string | null> {
        const already = saving.current;

        if (already !== null) {
            return already;
        }

        const writing = write(composition, asked);

        saving.current = writing;

        return writing.finally(() => {
            saving.current = null;
        });
    }

    async function write(composition: Composition, asked: DraftAct): Promise<string | null> {
        const held = draftId.current;
        const wire = wireComposition(composition);
        const at = ++staging.current;

        const answer =
            held === null
                ? await writeMailDraft(session, transport, wire)
                : await reviseMailDraft(session, transport, held, wire);

        if (answer.outcome === 'failed') {
            hold({ kind: 'failed', reason: answer.failure.reason });

            return null;
        }

        // A refusal is said in the words of the act that met it. The deployment screens the draft book by the same
        // rules as the outbox and answers the same codes, so the same refusal reaches a save — and telling its author
        // the message was not sent, when they pressed save, names an act nobody performed. A send writes the draft
        // before it posts it, so the refusal it meets here is the one it would have met at the send itself, and is
        // worded as the send's.
        if (!answer.value.written) {
            hold(
                asked === 'send'
                    ? { kind: 'refused', refusal: answer.value.refusal }
                    : { kind: 'refusedSave', refusal: answer.value.refusal },
            );

            return null;
        }

        const written = answer.value.draft;

        draftId.current = written.draftId;

        if (at === staging.current) {
            setStaged(written.attachments);
        }

        return written.draftId;
    }

    // Every act ends by saying what happened, and a failure says which of the five it was rather than that something
    // went wrong. Stated once here because five acts would otherwise each carry their own copy of the same three lines.
    function settled<TValue>(answer: ClientResult<TValue>, whenRead: (value: TValue) => DraftStanding): DraftStanding {
        return hold(
            answer.outcome === 'failed' ? { kind: 'failed', reason: answer.failure.reason } : whenRead(answer.value),
        );
    }

    async function send(composition: Composition): Promise<DraftStanding> {
        hold({ kind: 'sending' });

        const held = await saved(composition, 'send');

        if (held === null) {
            // The write said what stopped it and in the words of the send, so what this answers is that same
            // statement rather than a second, vaguer one made here. A stop asked while it was in flight goes with it:
            // it was asked about a message that never left.
            stopping.current = false;

            return settledAs.current;
        }

        const outcome = settled(await sendMailDraft(session, transport, held), (sent) => {
            if (!sent.queued) {
                return { kind: 'refused', refusal: sent.refusal };
            }

            queued.current = sent.outgoingEmailId;

            return { kind: 'queued', outgoingEmailId: sent.outgoingEmailId };
        });

        // A stop belongs to the send it was asked about, so it is read once here and cleared whatever became of that
        // send. Left standing over a send that was refused or failed, it would take back the *next* message somebody
        // deliberately sent — which is a message discarded on state nobody was acting on any more.
        const stopWasAsked = stopping.current;
        stopping.current = false;

        // Somebody asked to stop while the deployment had not yet answered, so this is where their stop lands.
        if (outcome.kind === 'queued' && stopWasAsked) {
            return withdraw();
        }

        return outcome;
    }

    async function withdraw(): Promise<DraftStanding> {
        const sent = queued.current;

        if (sent === null) {
            stopping.current = true;

            return settledAs.current;
        }

        return settled(await withdrawOutgoingMail(session, transport, sent), (withdrawal) => ({
            kind: 'withdrawn',
            withdrawal,
        }));
    }

    return {
        standing,
        staged,

        save: async (composition) => {
            hold({ kind: 'saving' });

            if ((await saved(composition, 'save')) === null) {
                return false;
            }

            hold({ kind: 'saved' });

            return true;
        },

        attach: async (composition, file) => {
            hold({ kind: 'attaching', fileName: file.name });

            const held = await saved(composition, 'save');

            if (held === null) {
                return;
            }

            const abandoning = new AbortController();
            uploading.current = abandoning;

            const answer = await stageMailDraftAttachment(
                session,
                held,
                file.name,
                // What the file declares itself to be, which is what the author's own system said. A file the system
                // could not name is the general binary type, which is what the deployment reads a request declaring
                // none as anyway.
                file.type === '' ? 'application/octet-stream' : file.type,
                (request) => upload(request, file, abandoning.signal),
            );

            uploading.current = null;

            settled(answer, (attachment) => {
                setStaged((already) => [...already, attachment]);

                return { kind: 'held' };
            });
        },

        unstage: async (attachmentId) => {
            const held = draftId.current;

            if (held === null) {
                return;
            }

            const at = ++staging.current;

            settled(await unstageMailDraftAttachment(session, transport, held, attachmentId), () => {
                if (at === staging.current) {
                    setStaged((already) => already.filter((file) => file.attachmentId !== attachmentId));
                }

                return { kind: 'held' };
            });
        },

        send,
        withdraw,

        discard: async () => {
            // Whatever save is in flight first, so a draft written a moment ago is one this knows about rather than
            // one it leaves behind because the identifier had not landed yet.
            await saving.current;

            const held = draftId.current;

            if (held === null) {
                return true;
            }

            const answer = await discardMailDraft(session, transport, held);

            if (answer.outcome === 'failed') {
                // Said rather than swallowed: closing on a refused delete would tell somebody their words are gone
                // while the deployment is still holding them, with nothing on this screen to go back to.
                hold({ kind: 'failed', reason: answer.failure.reason });

                return false;
            }

            draftId.current = null;

            return true;
        },
    };
}
