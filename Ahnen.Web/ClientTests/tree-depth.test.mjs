import assert from 'node:assert/strict';
import test, { after } from 'node:test';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { buildSync } from 'esbuild';

const directory = mkdtempSync(join(tmpdir(), 'ahnen-depth-tests-'));
const output = join(directory, 'depth.mjs');
after(() => rmSync(directory, { recursive: true, force: true }));
buildSync({
  entryPoints: [fileURLToPath(new URL('../ClientSrc/tree-depth.ts', import.meta.url))],
  bundle: true, platform: 'node', format: 'esm', outfile: output
});
const { limitDepth } = await import(pathToFileURL(output).href);

// gg -> g -> me -> kid -> grandkid, with a partner for me
const data = {
  indis: [
    { id: 'gg', fams: ['f1'] }, { id: 'g', famc: 'f1', fams: ['f2'] }, { id: 'me', famc: 'f2', fams: ['f3'] },
    { id: 'partner', fams: ['f3'] }, { id: 'kid', famc: 'f3', fams: ['f4'] }, { id: 'grandkid', famc: 'f4' }
  ],
  fams: [
    { id: 'f1', husb: 'gg', children: ['g'] }, { id: 'f2', husb: 'g', children: ['me'] },
    { id: 'f3', husb: 'me', wife: 'partner', children: ['kid'] }, { id: 'f4', husb: 'kid', children: ['grandkid'] }
  ]
};

test('depth 0 or an unknown focus returns the data untouched', () => {
  assert.equal(limitDepth(data, 'me', 0), data);
  assert.equal(limitDepth(data, 'nobody', 2), data);
  assert.equal(limitDepth(data, null, 2), data);
});

test('one generation up and down stops at parents and children', () => {
  const result = limitDepth(data, 'me', 1);
  const person = id => result.indis.find(p => p.id === id);
  assert.equal(person('me').famc, 'f2', 'focus keeps its parents');
  assert.equal(person('g').famc, undefined, 'grandparent link is cut above the parents');
  assert.deepEqual(result.fams.find(f => f.id === 'f3').children, ['kid']);
  assert.deepEqual(result.fams.find(f => f.id === 'f4').children, [], 'grandchildren are cut');
});

test('a deep enough limit keeps everything reachable and never mutates the input', () => {
  const snapshot = structuredClone(data);
  const result = limitDepth(data, 'me', 4);
  assert.deepEqual(result.indis, data.indis);
  assert.deepEqual(result.fams, data.fams);
  limitDepth(data, 'me', 1);
  assert.deepEqual(data, snapshot);
});
