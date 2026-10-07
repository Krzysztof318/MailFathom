// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.mailHtml = (() => {
  const { mdPlain } = MailFathomDesign.markdown;

  /* The original messages as their senders wrote them — what actually arrives from the server; the thread window
     shows cleaned-up text and the original opens on demand (the "code" icon) in a separate tab / preview. */
  const HTML_SHELL = (title, body) => '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>' + title + '</title></head>' +
    '<body style="margin:0;padding:0;background:#eef1f5;font-family:Georgia,\'Times New Roman\',serif;color:#1f2933">' + body + '</body></html>';
  /* Measuring script injected BEFORE the message markup. It runs in an opaque origin and does
     exactly one thing: report height via parent.postMessage. The message markup itself carries
     no scripts — it is sanitised before it gets here. */
  const S_OPEN = "<" + "script>", S_CLOSE = "<" + "/script>";
  const FIT_SCRIPT = (frameId) => S_OPEN + '(function(){var ID=' + JSON.stringify(frameId) + ',last=0,sends=0;' +
    /* We measure with a zero-height viewport so the result does not depend on the frame height
       — otherwise every fit would grow the content and the measurement would never settle. */
    'function measure(){var de=document.documentElement,b=document.body;if(!de||!b)return 0;' +
    'var prev=de.style.height;de.style.height="0px";' +
    'var h=Math.max(b.scrollHeight,b.offsetHeight,Math.ceil(b.getBoundingClientRect().height));' +
    'de.style.height=prev;return h}' +
    'function send(){if(sends>24)return;var h=measure();' +
    'if(h&&Math.abs(h-last)>3){last=h;sends++;try{parent.postMessage({frameId:ID,height:h},"*")}catch(e){}}}' +
    'function boot(){var de=document.documentElement,b=document.body;' +
    'if(de)de.style.overflow="hidden";if(b)b.style.overflow="hidden";' +
    /* We observe the content (body), not the viewport — observing documentElement would close the loop. */
    'if(window.ResizeObserver&&b){try{new ResizeObserver(function(){send()}).observe(b)}catch(e){}}send()}' +
    'if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot);else boot();' +
    'window.addEventListener("load",send);setTimeout(send,120);setTimeout(send,600);})()' + S_CLOSE;

  const embedHtml = (src, frameId) => {
    const i = src.indexOf("<head>");
    return i < 0
      ? FIT_SCRIPT(frameId) + src
      : src.slice(0, i + 6) + FIT_SCRIPT(frameId) + src.slice(i + 6);
  };

  const htmlFallback = (m, subject) => HTML_SHELL(subject || "Message", `
<div style="padding:24px 12px;font-family:Georgia,'Times New Roman',serif">
 <table role="presentation" width="600" align="center" cellpadding="0" cellspacing="0" style="width:600px;max-width:100%;background:#ffffff;border:1px solid #dde3ea">
  <tr><td style="padding:20px 24px;border-bottom:1px solid #eef1f5;font-family:Arial,sans-serif">
   <div style="font-size:15px;color:#14293f"><strong>` + (subject || "Message") + `</strong></div>
   <div style="font-size:12px;color:#7b8794;margin-top:4px">` + m.from + ` · ` + m.when + `</div>
  </td></tr>
  <tr><td style="padding:22px 24px;font-size:15px;line-height:1.7;color:#2c3a47">`
     + m.paras.map(p => '<p style="margin:0 0 14px 0">' + (p.text || mdPlain(p.md || "")) + '</p>').join("") +
    `</td></tr>
  <tr><td style="background:#f7f9fb;border-top:1px solid #e3e8ee;padding:13px 24px;font-family:Arial,sans-serif;font-size:11px;color:#9aa5b1">HTML version of the message · MailFathom shows simplified text by default</td></tr>
 </table>
</div>`);

  return { HTML_SHELL, FIT_SCRIPT, embedHtml, htmlFallback };
})();
