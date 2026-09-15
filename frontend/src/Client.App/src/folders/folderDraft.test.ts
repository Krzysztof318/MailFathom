// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ManagedMailFolder } from '@mailfathom/client-backend';
import { describe, expect, it } from 'vitest';
import {
    admissibleParents,
    depthOf,
    draftForEditedFolder,
    draftForNewFolder,
    folderNameOf,
    namePathOf,
    refusalOf,
    unchanged,
    withName,
    withParent,
    withRole,
} from './folderDraft';

function folder(
    stated: Partial<ManagedMailFolder> & { readonly id: string; readonly name: string },
): ManagedMailFolder {
    return { parentId: null, role: null, allowedActs: ['rename', 'move', 'delete'], ...stated };
}

// One mailbox, with a branch at each depth the ceiling allows: `Projects` holds `2026`, `Archive` runs the full three
// levels, and `Clients` sits beside them at the top. The full branch is what the depth rules are judged against.
const projects = folder({ id: 'p', name: 'Projects' });
const year = folder({ id: 'p26', name: '2026', parentId: 'p' });
const clients = folder({ id: 'c', name: 'Clients' });
const inbox = folder({ id: 'in', name: 'INBOX', role: 'Inbox', allowedActs: [] });
const archive = folder({ id: 'a', name: 'Archive' });
const old = folder({ id: 'o', name: 'Old', parentId: 'a' });
const older = folder({ id: 'oo', name: 'Older', parentId: 'o' });
const folders = [projects, year, clients, inbox, archive, old, older];

const making = draftForNewFolder('work', 'Work', null);
const editing = draftForEditedFolder('work', 'Work', projects);

describe('draftForNewFolder', () => {
    it('opens on nothing typed at the top of the mailbox', () => {
        expect(making).toMatchObject({ mode: 'create', standingId: null, parentId: null, name: '', role: null });
    });

    it('opens inside the folder a New folder inside was asked on', () => {
        expect(draftForNewFolder('work', 'Work', { id: 'p', name: 'Projects' })).toMatchObject({
            parentId: 'p',
            standingParentId: 'p',
        });
    });
});

describe('draftForEditedFolder', () => {
    it('opens on the folder as it stands, so that saving can tell what moved', () => {
        expect(editing).toMatchObject({
            mode: 'edit',
            standingId: 'p',
            name: 'Projects',
            standingName: 'Projects',
            parentId: null,
            standingParentId: null,
            role: null,
        });
    });

    it('carries the role the folder plays, which is what says its name is the service’s rather than anybody’s', () => {
        expect(draftForEditedFolder('work', 'Work', inbox)).toMatchObject({ role: 'Inbox', name: 'INBOX' });
    });
});

describe('withRole', () => {
    it('drops the name with the choice, a folder for a role being named by the service rather than here', () => {
        expect(withRole(withName(making, 'Whatever'), 'Trash')).toMatchObject({ role: 'Trash', name: '' });
    });

    it('leaves the name alone on the way back to an ordinary folder, there being none to drop', () => {
        expect(withRole(withName(making, 'Contracts'), null)).toMatchObject({ role: null, name: 'Contracts' });
    });
});

describe('refusalOf', () => {
    it('refuses a name nobody has typed yet', () => {
        expect(refusalOf(making, folders)).toBe('nameEmpty');
    });

    it('refuses a name that is nothing but space, which is a folder with no name', () => {
        expect(refusalOf(withName(making, '   '), folders)).toBe('nameEmpty');
    });

    it('refuses a name a sibling in the same place already carries, whatever its case', () => {
        expect(refusalOf(withName(making, 'clients'), folders)).toBe('nameTaken');
    });

    it('allows a name a folder somewhere else carries, the rule being about siblings', () => {
        expect(refusalOf(withParent(withName(making, 'Clients'), 'p'), folders)).toBeNull();
    });

    it('allows a folder being saved under the name it already has, which is a move rather than a collision', () => {
        expect(refusalOf(withParent(editing, 'c'), folders)).toBeNull();
    });

    it('allows a folder at the third level, which is as deep as the column draws', () => {
        expect(refusalOf(withParent(withName(making, 'Q1'), 'p26'), folders)).toBeNull();
    });

    it('refuses nesting past the third level, which is the column’s own ceiling', () => {
        expect(refusalOf(withParent(withName(making, 'Q1'), 'oo'), folders)).toBe('tooDeep');
    });

    it('counts what a move brings with it, so a folder with children cannot go as deep as an empty one', () => {
        expect(refusalOf(withParent(draftForEditedFolder('work', 'Work', clients), 'o'), folders)).toBeNull();
        expect(refusalOf(withParent(editing, 'o'), folders)).toBe('tooDeep');
    });

    it('refuses putting a folder inside itself', () => {
        expect(refusalOf(withParent(editing, 'p'), folders)).toBe('nestedInItself');
    });

    it('refuses putting a folder inside something beneath it', () => {
        expect(refusalOf(withParent(editing, 'p26'), folders)).toBe('nestedInItself');
    });

    it('asks nothing of a folder being created for a role, which carries neither a name nor a parent', () => {
        expect(refusalOf(withRole(making, 'Trash'), folders)).toBeNull();
    });

    it('asks the same of a folder that already plays one, its place being a question even where its name is not', () => {
        expect(refusalOf(withParent(draftForEditedFolder('work', 'Work', inbox), 'oo'), folders)).toBe('tooDeep');
    });
});

describe('unchanged', () => {
    it('says a folder opened and left alone has nothing to ask the deployment for', () => {
        expect(unchanged(editing)).toBe(true);
    });

    it('says a renamed folder has', () => {
        expect(unchanged(withName(editing, 'Work'))).toBe(false);
    });

    it('says a moved folder has', () => {
        expect(unchanged(withParent(editing, 'c'))).toBe(false);
    });

    it('says a folder that does not exist yet always has, there being nothing to leave alone', () => {
        expect(unchanged(withName(making, 'Contracts'))).toBe(false);
    });
});

describe('folderNameOf', () => {
    it('takes the surrounding space off what was typed', () => {
        expect(folderNameOf(withName(making, '  Contracts 2027 '))).toBe('Contracts 2027');
    });
});

describe('namePathOf', () => {
    it('names a folder level by level from the top of the mailbox', () => {
        expect(namePathOf('p26', folders)).toBe('Projects / 2026');
    });

    it('names a folder at the top as itself', () => {
        expect(namePathOf('c', folders)).toBe('Clients');
    });

    it('answers nothing for a folder the report does not name', () => {
        expect(namePathOf('gone', folders)).toBe('');
    });
});

describe('depthOf', () => {
    it('reads the top of the mailbox as no depth at all', () => {
        expect(depthOf(null, folders)).toBe(0);
    });

    it('counts a folder’s own level from one', () => {
        expect(depthOf('p', folders)).toBe(1);
        expect(depthOf('p26', folders)).toBe(2);
    });

    it('stops rather than walking forever where a report names a parent it does not carry', () => {
        expect(depthOf('orphan', [folder({ id: 'orphan', name: 'Orphan', parentId: 'nowhere' })])).toBe(1);
    });
});

describe('admissibleParents', () => {
    it('offers every folder that can still take one inside it', () => {
        expect(admissibleParents(making, folders).map((entry) => entry.id)).toEqual(['p', 'p26', 'c', 'in', 'a', 'o']);
    });

    it('leaves out the folder being moved and everything beneath it', () => {
        expect(admissibleParents(editing, folders).map((entry) => entry.id)).toEqual(['c', 'in', 'a']);
    });

    it('leaves out a folder that would put the branch past the ceiling', () => {
        const deep = draftForEditedFolder('work', 'Work', clients);

        expect(admissibleParents(deep, folders).map((entry) => entry.id)).toEqual(['p', 'p26', 'in', 'a', 'o']);
    });
});
