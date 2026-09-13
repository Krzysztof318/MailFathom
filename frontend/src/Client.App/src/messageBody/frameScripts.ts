// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

// The client's own scripts, which `MessageMarkupFrame.tsx` prepends to a frame's `srcDoc` ahead of the message markup.
// Each is exported as the text between its `<script>` tags and nothing else, because that exact text is what a content
// security policy admits by hash: a `srcdoc` document inherits the policy of the page embedding it, so
// `contentSecurityPolicy.ts` beside `vite.config.ts` hashes these two strings, and a script edited here is admitted by
// the next build without anybody copying a digest. A character added between the tags anywhere else would be a script
// the policy refuses without a visible error.

// Reports a followed link. It cancels the frame's own handling of the click and reports the `href` **as the sender wrote
// it** — the attribute rather than the resolved property, because a relative reference resolves against `about:srcdoc`
// and would arrive as an address that means nothing. Whether the target is one a reader may be handed is the parent's
// decision and is taken there.
//
// The listener is on the document in the capture phase, so a link wrapped in whatever a template put around it is still
// answered by the first handler to see the event. It reads the tree upwards rather than trusting the event target,
// since a click lands on the text node's element — a `span` inside the anchor, the image inside a banner link.
export const linkScript = `(function(){
function anchor(n){while(n&&n.nodeType===1){if(n.nodeName==="A")return n;n=n.parentNode}return null}
document.addEventListener("click",function(e){var a=anchor(e.target);if(!a)return;
var href=a.getAttribute("href");if(!href)return;e.preventDefault();
try{parent.postMessage({link:href},"*")}catch(err){}},true)})()`;

// Measures the document at a viewport height of zero — otherwise each fitting would enlarge the content it is measuring
// and the number would grow without end — and observes the body rather than the document element, which would close
// the same loop. It reports by `postMessage` and does nothing else, which is what makes granting the flag bounded.
//
// It also stops the framed document scrolling inside itself, which is the design project's `scrolling="no"` written
// the way the platform still has: that attribute is deprecated and the lint set refuses it, and what replaces it is
// `overflow: hidden` on the framed document. The one case the frame is meant to scroll is the one where no report ever
// arrives, and nothing there ran this script to hide it.
export const measuringScript = `(function(){var last=0,sends=0;
function measure(){var de=document.documentElement,b=document.body;if(!de||!b)return 0;
var held=de.style.height;de.style.height="0px";
var h=Math.max(b.scrollHeight,b.offsetHeight,Math.ceil(b.getBoundingClientRect().height));
de.style.height=held;return h}
function send(){if(sends>24)return;var h=measure();
if(h&&Math.abs(h-last)>3){last=h;sends++;try{parent.postMessage({height:h},"*")}catch(e){}}}
function boot(){var de=document.documentElement,b=document.body;
if(de)de.style.overflow="hidden";if(b)b.style.overflow="hidden";
if(window.ResizeObserver&&b){try{new ResizeObserver(function(){send()}).observe(b)}catch(e){}}send()}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot);else boot();
window.addEventListener("load",send);setTimeout(send,120);setTimeout(send,600);})()`;
