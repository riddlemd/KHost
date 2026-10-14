// Rewrites golden/ from the drawers as they are now. Run only against code whose picture is known
// good: draw-recordings.test.js holds every later change to these files.
//   node record-goldens.js

'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { runEq, runLyrics, compact, EQ_STYLES } = require('./recording.js');

const dir = path.join(__dirname, 'golden');
fs.mkdirSync(dir, { recursive: true });
const write = (name, value) => fs.writeFileSync(path.join(dir, name), `${JSON.stringify(compact(value))}\n`);

for (const style of EQ_STYLES) write(`eq-${style}.json`, runEq(style));
write('lyrics-ltr.json', runLyrics());
write('lyrics-rtl.json', runLyrics({ rightToLeft: true }));
