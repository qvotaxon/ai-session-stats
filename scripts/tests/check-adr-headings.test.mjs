import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

import { checkAdrDirectory } from '../check-adr-headings.mjs';

const DASH = '\u2013';

function conforming(number = '0004', { h1, status = 'Accepted (2026-01-10)', extra = '' } = {}) {
  return [
    h1 ?? `# ADR-${number} ${DASH} A decision`,
    '',
    '## Status',
    '',
    status,
    '',
    '## Prologue (Summary)',
    '',
    'In the context of x, facing y, we decided for z, to achieve a, accepting b.',
    '',
    '## Discussion (Context)',
    '',
    'Forces.',
    '',
    '## Solution (Decision)',
    '',
    'Do it.',
    '',
    '## Consequences (Results)',
    '',
    '- Result.',
    '',
    extra,
  ].join('\n');
}

function check(files) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'adr-'));
  try {
    for (const [name, content] of Object.entries(files)) {
      fs.writeFileSync(path.join(dir, name), content);
    }
    return checkAdrDirectory(dir).errors;
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

function passes(name, content) {
  assert.deepEqual(check({ [name]: content }), []);
}

function failsWith(name, content, pattern) {
  const errors = check({ [name]: content });
  assert.ok(
    errors.some((e) => pattern.test(e)),
    `expected an error matching ${pattern}, got: ${JSON.stringify(errors)}`,
  );
  assert.ok(errors.every((e) => e.startsWith(`${name}: `)), `errors must name the file: ${JSON.stringify(errors)}`);
}

const FILE = '0004-x.md';

test('a conforming ADR passes', () => {
  passes(FILE, conforming());
});

test('a status line followed by a sentence passes (0011 shape)', () => {
  passes(FILE, conforming('0004', { status: 'Proposed (2026-11-27). Becomes Accepted when the #99 pull request merges.' }));
});

test('a status without a date passes', () => {
  passes(FILE, conforming('0004', { status: 'Accepted' }));
});

test('the bare short-form headings pass', () => {
  const text = conforming()
    .replace('## Prologue (Summary)', '## Prologue')
    .replace('## Discussion (Context)', '## Discussion')
    .replace('## Solution (Decision)', '## Solution')
    .replace('## Consequences (Results)', '## Consequences');
  passes(FILE, text);
});

test('a plain line between the H1 and ## Status passes', () => {
  passes(FILE, conforming().replace('\n\n## Status', '\n\nFormerly numbered 0003.\n\n## Status'));
});

test('one and two well-formed amendments pass', () => {
  const one = '## Amendment 2026-12-01: x (#52)\n\nText.\n';
  const two = `${one}\n## Amendment 2026-12-02: y (#57)\n\nText.\n`;
  passes(FILE, conforming('0004', { extra: one }));
  passes(FILE, conforming('0004', { extra: two }));
});

test('headings inside a fenced block do not count', () => {
  passes(FILE, conforming().replace('Forces.', 'Forces.\n\n```md\n## Context\n# ADR-0001 - x\n```\n\n~~~\n## Notes\n~~~'));
});

test('CRLF input passes', () => {
  passes(FILE, conforming().replace(/\n/g, '\r\n'));
});

test('a file starting with a BOM passes', () => {
  passes(FILE, `\uFEFF${conforming()}`);
});

test('trailing whitespace on headings passes', () => {
  const amendment = '## Amendment 2026-12-01: x (#52) \n\nText.\n';
  passes(FILE, conforming('0004', { extra: amendment }).replace('## Status', '## Status '));
  passes(FILE, conforming().replace('A decision', 'A decision\t'));
});

test('a ### subsection inside an amendment passes', () => {
  passes(FILE, conforming('0004', { extra: '## Amendment 2026-12-01: x (#52)\n\n### Details\n\nText.\n' }));
});

test('### headings elsewhere are not checked', () => {
  passes(FILE, conforming().replace('Do it.', '### Options\n\n#### 1. A\n\nDo it.'));
});

test('two files with the same number pass (no duplicate check yet)', () => {
  assert.deepEqual(check({ '0003-a.md': conforming('0003'), '0003-b.md': conforming('0003') }), []);
});

test('template.md and README.md are ignored even when they do not conform', () => {
  assert.deepEqual(check({ 'template.md': '# nothing\n', 'README.md': '# nothing\n', [FILE]: conforming() }), []);
});

test('files that are not markdown are ignored', () => {
  assert.deepEqual(check({ 'notes.txt': 'x', [FILE]: conforming() }), []);
});

for (const section of ['Status', 'Prologue (Summary)', 'Discussion (Context)', 'Solution (Decision)', 'Consequences (Results)']) {
  test(`a missing "## ${section}" gives its own error`, () => {
    const escaped = section.replace(/[()]/g, '\\$&');
    const text = conforming().replace(new RegExp(`## ${escaped}\\n`), '');
    failsWith(FILE, text, new RegExp(`missing "## ${escaped}"`));
  });
}

test('headings out of order fail', () => {
  const text = conforming()
    .replace('## Discussion (Context)', '## TMP')
    .replace('## Solution (Decision)', '## Discussion (Context)')
    .replace('## TMP', '## Solution (Decision)');
  failsWith(FILE, text, /appears before/);
});

test('a duplicate heading fails', () => {
  failsWith(FILE, conforming().replace('Do it.', 'Do it.\n\n## Solution (Decision)\n\nAgain.'), /more than once|duplicate/i);
});

test('## Context before Consequences fails', () => {
  failsWith(FILE, conforming().replace('Forces.', 'Forces.\n\n## Context\n\nOld.'), /"## Context"/);
});

test('an amendment with a prose heading fails', () => {
  failsWith(FILE, conforming('0004', { extra: '## Amendment (Copilot, proposed by #53)\n\nText.\n' }), /after "## Consequences/);
});

test('an amendment with a placeholder date fails', () => {
  failsWith(FILE, conforming('0004', { extra: '## Amendment <merge day YYYY-MM-DD>: x (#52)\n\nText.\n' }), /after "## Consequences/);
});

test('## Notes after Consequences fails', () => {
  failsWith(FILE, conforming('0004', { extra: '## Notes\n\nText.\n' }), /"## Notes" after "## Consequences/);
});

test('### Amendment with a placeholder date fails', () => {
  failsWith(FILE, conforming('0004', { extra: '### Amendment <merge day YYYY-MM-DD>: x (#52)\n\nText.\n' }), /amendment heading must be/);
});

test('an amendment without an issue reference fails', () => {
  failsWith(FILE, conforming('0004', { extra: '## Amendment 2026-12-01: x\n\nText.\n' }), /after "## Consequences/);
});

test('an invalid status line fails', () => {
  failsWith(FILE, conforming('0004', { status: 'Maybe (2026-01-10)' }), /status/i);
});

test('a missing status line fails', () => {
  failsWith(FILE, conforming('0004', { status: '' }), /status/i);
});

test('a file name that is not NNNN-kebab-case-title.md fails', () => {
  failsWith('ADR-0012-x.md', conforming('0012'), /file name must be NNNN-kebab-case-title\.md/);
});

test('"# ADR-0004: x" fails', () => {
  failsWith(FILE, conforming('0004', { h1: '# ADR-0004: x' }), /H1 must be "# ADR-NNNN – <title>"/);
});

test('"# ADR 0004 – x" fails', () => {
  failsWith(FILE, conforming('0004', { h1: `# ADR 0004 ${DASH} x` }), /H1 must be/);
});

test('"# ADR-0004 - x" (hyphen-minus) fails', () => {
  failsWith(FILE, conforming('0004', { h1: '# ADR-0004 - x' }), /H1 must be/);
});

test('an H1 number that differs from the file number fails', () => {
  failsWith('0003-x.md', conforming('0005'), /H1 number 0005 does not match file number 0003/);
});

test('an H1 that is not the first non-blank line fails', () => {
  failsWith(FILE, `Intro.\n\n${conforming()}`, /H1/);
});

test('two H1s fail', () => {
  failsWith(FILE, conforming().replace('Forces.', `Forces.\n\n# ADR-0004 ${DASH} again`), /H1/);
});

test('a file with no H1 fails', () => {
  failsWith(FILE, conforming().replace(/^# .*\n/, ''), /H1/);
});

test('an empty directory is fine and an unknown directory is an error', () => {
  assert.deepEqual(check({}), []);
  assert.ok(checkAdrDirectory(path.join(os.tmpdir(), 'adr-does-not-exist-102')).errors.length > 0);
});

const CHECKER = fileURLToPath(new URL('../check-adr-headings.mjs', import.meta.url));

function runCli(files) {
  const dir = fs.mkdtempSync(path.join(fs.realpathSync.native(os.tmpdir()), 'adr-cli-'));
  try {
    for (const [name, content] of Object.entries(files)) {
      fs.writeFileSync(path.join(dir, name), content);
    }
    return spawnSync(process.execPath, [CHECKER, dir], { encoding: 'utf8' });
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

test('the CLI exits 1 and prints the error to stderr for a failing directory', () => {
  const result = runCli({ [FILE]: conforming('0004', { h1: '# ADR-0004: x' }) });
  assert.equal(result.status, 1, `stdout: ${result.stdout} stderr: ${result.stderr}`);
  assert.match(result.stderr, /0004-x\.md: H1 must be "# ADR-NNNN . <title>"/);
  assert.equal(result.stdout, '');
});

test('the CLI exits 0 and reports the count for a conforming directory', () => {
  const result = runCli({ [FILE]: conforming() });
  assert.equal(result.status, 0, `stdout: ${result.stdout} stderr: ${result.stderr}`);
  assert.equal(result.stdout.trim(), '1 ADRs checked, all conform');
  assert.equal(result.stderr, '');
});
