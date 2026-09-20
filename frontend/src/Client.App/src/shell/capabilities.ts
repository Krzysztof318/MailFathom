// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { DeploymentSession, MailFathomPermission } from '@mailfathom/client-backend';
import { spaces, type Space } from '../routing/spaces';

// What the client offers follows the grant the deployment reported for the credential that signed in, so a capability
// the credential does not carry is absent rather than present and refused when it is pressed. The service is what
// enforces; nothing here is a second copy of that decision, and a screen the client did offer is still refused by the
// route behind it if the grant changed underneath.
//
// A capability is named for what a person does with it rather than for the permission behind it, because that is what
// a sentence on the screen has to say. The one table below is where the two meet, and it is exhaustive by its own type
// so a capability added later does not compile until it says which grant it is reached under.

export const clientCapabilities = [
    'readMail',
    'askMail',
    'writeMailFlags',
    'fileMail',
    'deleteMail',
    'composeMail',
    'sendMail',
    'manageFolders',
    'readContacts',
    'writeContacts',
] as const;

/** Something the client offers a person, where the grant permits it. */
export type ClientCapability = (typeof clientCapabilities)[number];

// One capability per grant rather than one per act: two capabilities behind one permission would be two sentences
// saying the same thing in the strip that reports what a credential may not do. `writeMailFlags` is therefore what
// marking read on open and flagging from the toolbar are both reached under, and filing mail elsewhere is a grant of
// its own because a mail server treats the two differently — a wrong flag misdescribes mail somebody can still find.
const capabilityGrants: Readonly<Record<ClientCapability, MailFathomPermission>> = {
    readMail: 'mailfathom.mail.read',
    askMail: 'mailfathom.mail.ask',
    writeMailFlags: 'mailfathom.mail.flags.write',
    fileMail: 'mailfathom.mail.move',
    deleteMail: 'mailfathom.mail.delete',
    composeMail: 'mailfathom.mail.drafts.write',
    sendMail: 'mailfathom.mail.send',
    manageFolders: 'mailfathom.mail.accounts.write',
    readContacts: 'mailfathom.mail.contacts.read',
    writeContacts: 'mailfathom.mail.contacts.write',
};

// Which capability each space is reached under. Every space that is still a placeholder carries none, because nothing
// behind one is reached over a grant yet; the space that fills it is what decides what it needs, and naming a
// permission here before then would be a guess enforced on a screen that does not exist. `discover` and `agent` are the two
// exceptions among them: each is a placeholder today and asking is what each will be, so both are already withheld
// from a credential that may not ask rather than offered as placeholders somebody's grant would never let become a
// screen. `people` is no longer one of either group: it is a screen, and reading the address book is what it is, and
// `calendar` is the same — what it reads and writes is the deployment's calendar, which is reached under reading mail
// rather than under a grant of its own. So is `tasks`: a task list is read and written over the mail grant, and the
// arranged day it offers is asked for under the asking one, which the screen asks about for itself rather than being
// reached under.
const spaceCapabilities: Readonly<Record<Space, ClientCapability | null>> = {
    discover: 'askMail',
    mail: 'readMail',
    cases: null,
    agent: 'askMail',
    tasks: 'readMail',
    calendar: 'readMail',
    people: 'readContacts',
};

/** Whether this credential may do that here. */
export function offers(session: DeploymentSession, capability: ClientCapability): boolean {
    return session.permissions.includes(capabilityGrants[capability]);
}

/** What the client offers and this credential may not do, in the order the client would have offered them. */
export function withheldFrom(session: DeploymentSession): readonly ClientCapability[] {
    return clientCapabilities.filter((capability) => !offers(session, capability));
}

/** The spaces this credential may open, which is what the navigation is drawn from and what an address is answered against. */
export function spacesOffered(session: DeploymentSession): readonly Space[] {
    return spaces.filter((space) => {
        const needed = spaceCapabilities[space];

        return needed === null || offers(session, needed);
    });
}
