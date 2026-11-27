import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const SECTIONS = [
  { name: '## Status', pattern: /^## Status$/ },
  { name: '## Prologue (Summary)', pattern: /^## Prologue( \(Summary\))?$/ },
  { name: '## Discussion (Context)', pattern: /^## Discussion( \(Context\))?$/ },
  { name: '## Solution (Decision)', pattern: /^## Solution( \(Decision\))?$/ },
  { name: '## Consequences (Results)', pattern: /^## Consequences( \(Results\))?$/ },
];

const FILE_NAME = /^(\d{4})-[a-z0-9]+(?:-[a-z0-9]+)*\.md$/;
const IGNORED = new Set(['template.md', 'README.md']);
const H1 = /^# ADR-(\d{4}) – \S/;
const AMENDMENT = /^## Amendment \d{4}-\d{2}-\d{2}: \S.* \(#\d+\)$/;
const STATUS = /^(Proposed|Accepted|Deprecated|Superseded by ADR-\d{4})\b/;

function headingLines(text) {
  const lines = text.replace(/^﻿/, '').replace(/\r/g, '').split('\n');
  let inFence = false;
  return lines.map((raw, index) => {
    if (raw.startsWith('```') || raw.startsWith('~~~')) {
      inFence = !inFence;
      return { index, text: raw, fenced: true };
    }
    const isHeading = !inFence && raw.startsWith('#');
    return { index, text: isHeading ? raw.trimEnd() : raw, fenced: inFence };
  });
}

function checkH1(lines, fileNumber, fail) {
  const h1s = lines.filter((l) => !l.fenced && l.text.startsWith('# '));
  const firstNonBlank = lines.find((l) => l.text.trim() !== '');
  if (h1s.length === 0) {
    fail('H1 must be "# ADR-NNNN – <title>" (en dash)');
    return;
  }
  if (h1s.length > 1) fail('more than one H1');
  if (firstNonBlank !== h1s[0]) fail('the H1 must be the first non-blank line');
  const match = H1.exec(h1s[0].text);
  if (!match) {
    fail('H1 must be "# ADR-NNNN – <title>" (en dash)');
  } else if (fileNumber && match[1] !== fileNumber) {
    fail(`H1 number ${match[1]} does not match file number ${fileNumber}`);
  }
}

function checkSections(lines, fail) {
  const level2 = lines.filter((l) => !l.fenced && l.text.startsWith('## '));
  const position = new Map();
  for (const section of SECTIONS) {
    const found = level2.filter((l) => section.pattern.test(l.text));
    if (found.length === 0) fail(`missing "${section.name}"`);
    if (found.length > 1) fail(`"${section.name}" appears more than once`);
    if (found.length > 0) position.set(section.name, found[0].index);
  }
  const present = SECTIONS.filter((s) => position.has(s.name));
  present.forEach((later, j) => {
    present.slice(0, j).forEach((earlier) => {
      if (position.get(later.name) < position.get(earlier.name)) {
        fail(`"${later.name}" appears before "${earlier.name}"`);
      }
    });
  });

  const consequences = position.get(SECTIONS[4].name);
  for (const heading of level2) {
    if (SECTIONS.some((s) => s.pattern.test(heading.text))) continue;
    if (consequences !== undefined && heading.index > consequences) {
      if (!AMENDMENT.test(heading.text)) {
        fail(`"${heading.text}" after "${SECTIONS[4].name}"; only "## Amendment YYYY-MM-DD: <summary> (#N)" is allowed`);
      }
    } else {
      fail(`"${heading.text}" is not an allowed section`);
    }
  }
  if (consequences !== undefined) {
    for (const heading of lines) {
      if (heading.fenced || heading.index < consequences) continue;
      if (/^#{3,} Amendment/.test(heading.text)) fail('amendment heading must be "## Amendment YYYY-MM-DD: <summary> (#N)"');
    }
  }
}

function checkStatus(lines, fail) {
  const status = lines.find((l) => !l.fenced && l.text === '## Status');
  if (!status) return;
  const next = lines.slice(status.index + 1).find((l) => l.text.trim() !== '');
  if (!next || !STATUS.test(next.text)) {
    fail(`invalid status line "${next ? next.text : ''}"; use Proposed, Accepted, Deprecated or Superseded by ADR-NNNN`);
  }
}

export function checkAdrDirectory(dir) {
  const errors = [];
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch (error) {
    return { errors: [`${dir}: cannot read directory (${error.code ?? error.message})`] };
  }
  const files = entries
    .filter((e) => e.isFile() && e.name.endsWith('.md') && !IGNORED.has(e.name))
    .map((e) => e.name)
    .sort();
  for (const name of files) {
    const fail = (message) => errors.push(`${name}: ${message}`);
    const nameMatch = FILE_NAME.exec(name);
    if (!nameMatch) fail('file name must be NNNN-kebab-case-title.md');
    const lines = headingLines(fs.readFileSync(path.join(dir, name), 'utf8'));
    checkH1(lines, nameMatch?.[1], fail);
    checkSections(lines, fail);
    checkStatus(lines, fail);
  }
  return { errors, checked: files.length };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const dir = process.argv[2] ?? 'docs/adr';
  const { errors, checked } = checkAdrDirectory(dir);
  if (errors.length > 0) {
    for (const error of errors) console.error(error);
    process.exit(1);
  }
  console.log(`${checked} ADRs checked, all conform`);
}
