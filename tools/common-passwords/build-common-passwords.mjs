// Rebuilds src/Goldmark.Api/Security/Passwords/common-passwords.txt, the list of common passwords
// Goldmark refuses (DEC-038 item 6, owner's choice in DEC-040): the 12 to 128 character entries of
// ignis-sec/Pwdb-Public's top-1M list, pinned by commit and SHA-256, lower-cased, de-duplicated and
// sorted ordinally, so the output is reproducible. Shorter and longer passwords already fail the
// length rule. Run with the repository's pinned Node: node tools/common-passwords/build-common-passwords.mjs
import { createHash } from 'node:crypto';
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const commit = '9b53917431ed5cd57cd6cbec8c3a19cf9a5cd8dd';
const url = `https://raw.githubusercontent.com/ignis-sec/Pwdb-Public/${commit}/wordlists/ignis-1M.txt`;
const sha256 = 'e9a88f67aafe65496682dc374559ee714e978bee50314767494c3e37a18c9fc8';
const output = fileURLToPath(new URL('../../src/Goldmark.Api/Security/Passwords/common-passwords.txt', import.meta.url));

const response = await fetch(url);
if (!response.ok) {
  throw new Error(`download failed: HTTP ${response.status}`);
}
const bytes = Buffer.from(await response.arrayBuffer());
const actual = createHash('sha256').update(bytes).digest('hex');
if (actual !== sha256) {
  throw new Error(`unexpected SHA-256 ${actual}`);
}

// Length in UTF-16 code units, as .NET's string.Length counts it. Lines with bytes that are not
// UTF-8 (decoded as U+FFFD) or with control characters are dropped: no one can type them.
const unwanted = /[\u0000-\u001f\u007f�]/u;
const entries = new Set();
for (const raw of bytes.toString('utf8').split('\n')) {
  const line = raw.endsWith('\r') ? raw.slice(0, -1) : raw;
  if (line.length >= 12 && line.length <= 128 && !unwanted.test(line)) {
    entries.add(line.toLowerCase());
  }
}

const sorted = [...entries].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
writeFileSync(output, `${sorted.join('\n')}\n`, 'utf8');
console.log(`${sorted.length} entries written to ${output}`);
