// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The browser head's half of the sign-in redirect. The managed side is BrowserSignInRedirectListener, which is the
// browser's implementation of ISignInRedirectListener in MailFathom.Client.Backend; everything else about the flow is
// the same code the desktop head runs.
//
// A window rather than a navigation, and that is the whole design. Sending this document to the authorization server
// would destroy the page, and with it the proof key and the anti-forgery value the eventual code has to be redeemed
// against — which would leave only one place to put them back: browser storage. The application deliberately writes
// nothing there, so the document that started the sign-in is the document that has to still be running when it ends.
//
// The redirect lands on this application's own origin, so the query is readable from here the moment it arrives. While
// the window is still at the authorization server it is another origin and reading its location throws, which is what
// "not yet" looks like. Reading is what ends the wait, so the window is closed before the application it landed on has
// any real chance to start in it.
var mailFathomSignIn = {
    window: null,

    origin: function () {
        return globalThis.location.origin + "/";
    },

    open: function (authorizationUrl) {
        this.close();

        this.window = globalThis.open(authorizationUrl, "mailfathom-signin", "popup,width=520,height=680");

        return this.window != null;
    },

    // "" while nothing has come back, "closed" once the person dismissed the window, and the query otherwise.
    poll: function () {
        if (this.window == null || this.window.closed) {
            return "closed";
        }

        try {
            var search = this.window.location.search;

            return search != null && search.length > 1 ? search : "";
        } catch (crossOrigin) {
            // Still at the authorization server, where this document may not read a location.
            return "";
        }
    },

    close: function () {
        if (this.window != null) {
            if (!this.window.closed) {
                this.window.close();
            }

            this.window = null;
        }
    }
};
