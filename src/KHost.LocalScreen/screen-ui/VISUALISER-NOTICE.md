# Visualiser: vendored files

| File | Source | Version | Licence |
|---|---|---|---|
| `butterchurn.min.js` | npm `butterchurn`, `lib/butterchurn.min.js`, unmodified | 2.6.7 | MIT, © 2013-2018 Jordan Berg |
| `visualiser-presets.js` | npm `butterchurn-presets`, `lib/butterchurnPresets.min.js`: ten presets copied verbatim as JSON | 2.4.7 | MIT, © 2013-2018 Jordan Berg |

Both are MIT, which permits inclusion in KHost under the PolyForm Shield licence; the MIT text is in
`THIRD-PARTY-NOTICES.md` §4.

**The presets' own authorship.** Each preset is a MilkDrop community preset converted by the pack's
author, and is named for its original authors (Geiss, Flexi, Martin, Rovastar, Zylot and others).
The pack carries MIT as a whole and states nothing per preset; MilkDrop presets were historically
shared freely with Winamp and have no licence of their own. Treat the MIT grant as the pack's, not
as a statement from each preset author.

**How the ten were chosen.** Rendered offline in WebGL 2 for 240 frames each, twice: fed silence,
and fed a synthetic tone. Kept only presets whose mean luminance (0-255) stayed between 15 and 90
both ways: alive with no input, since an encoded song on WebKit reaches the visualiser as silence
(see AGENTS.md), and dark enough that the words over it stay the brightest thing on screen. Many
presets in the pack fail the first test and fade to black in silence. The set measured 18-83.

To change the set, re-extract with the pack's `getPresets()` rather than editing the file by hand:
the host sends a number, and the screen takes the preset at that number modulo the list's length,
so the order is what a number means.
