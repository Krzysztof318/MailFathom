# Third-party notices for the MailFathom desktop client

This file travels with the MailFathom desktop client and describes software inside it that somebody else wrote.
MailFathom itself is Apache-2.0; `LICENSE` and `NOTICE` beside this file are its terms and its attribution.

## LibVLCSharp, under the GNU Lesser General Public License

**This product uses the LibVLCSharp library, which is covered by the GNU Lesser General Public License, version 2.1 or
later.** The full text of that licence is in `LGPL-2.1.txt` beside this file.

LibVLCSharp is `LibVLCSharp.dll`, its own file in this directory. It reaches this application through the Uno Platform
desktop runtime, which declares it as a dependency, and it is what the platform's media playback is implemented over on
Linux. Nothing here modifies it: it is the assembly as published, it is not merged into another assembly, it is not
trimmed, rewritten, or compiled ahead of time, and it is not inside a bundle you cannot open — so you may replace it
with your own version of the library and run this application against that instead.

The corresponding source for the version in this artifact is published by VideoLAN at
<https://github.com/videolan/libvlcsharp>, and is offered from the same place this artifact is downloaded from:
<https://github.com/Krzysztof318/MailFathom/releases>. `LibVLCSharp.dll`'s own file version identifies which release of
it this artifact carries.

The native VLC libraries are **not** part of this artifact. LibVLCSharp is the managed binding alone, and media playback
would need those libraries installed separately; they are published by VideoLAN under their own terms.

## Everything else

Every other component in this artifact is under a permissive licence — Apache-2.0, MIT, MS-PL, or the Unicode License,
the last of which covers the ICU build that decides how text outside one alphabet is sorted and cased — and none of
them is modified here either. MailFathom's complete third-party register, naming each component, its licence, and the review it passed,
is published with the source at
<https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md>.
