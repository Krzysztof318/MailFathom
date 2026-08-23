// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The browser head's half of one question: where its deployment is. The managed side is PageOriginDeploymentAddress,
// and the answer is the origin this document was served from — a deployment that serves the client serves it from the
// same address its client surface answers on, so there is nothing for an installation to state and nothing that can
// disagree with the server.
//
// The trailing slash is what makes it a base rather than a document: every route the application calls is resolved
// against this, and MailFathom.Client.Backend refuses an address carrying anything more than an origin.
var mailFathomDeployment = {
    origin: function () {
        return globalThis.location.origin + "/";
    }
};
