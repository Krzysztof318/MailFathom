// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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

export const clientCapabilities = ['readMail', 'askMail'] as const;

/** Something the client offers a person, where the grant permits it. */
export type ClientCapability = (typeof clientCapabilities)[number];

const capabilityGrants: Readonly<Record<ClientCapability, MailFathomPermission>> = {
    readMail: 'mailfathom.mail.read',
    askMail: 'mailfathom.mail.ask',
};

// Which capability each space is reached under. Every space that is still a placeholder carries none, because nothing
// behind one is reached over a grant yet; the space that fills it is what decides what it needs, and naming a
// permission here before then would be a guess enforced on a screen that does not exist. `discover` and `agent` are the two
// exceptions among them: each is a placeholder today and asking is what each will be, so both are already withheld
// from a credential that may not ask rather than offered as placeholders somebody's grant would never let become a
// screen.
const spaceCapabilities: Readonly<Record<Space, ClientCapability | null>> = {
    discover: 'askMail',
    mail: 'readMail',
    cases: null,
    agent: 'askMail',
    tasks: null,
    calendar: null,
    people: null,
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
