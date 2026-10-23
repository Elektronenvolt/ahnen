import assert from 'node:assert/strict';
import test, { after } from 'node:test';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { buildSync } from 'esbuild';

const directory = mkdtempSync(join(tmpdir(), 'ahnen-overview-tests-'));
const output = join(directory, 'overview.mjs');
after(() => rmSync(directory, { recursive: true, force: true }));
buildSync({
  entryPoints: [fileURLToPath(new URL('../ClientSrc/tree-overview.ts', import.meta.url))],
  bundle: true, platform: 'node', format: 'esm', outfile: output
});
const { overviewNodes } = await import(pathToFileURL(output).href);

const family = (id, partners, children) => ({ id, husb: partners[0], wife: partners[1], children });
const data = (ids, fams) => ({ indis: ids.map(id => ({ id })), fams });
const renderer = {
  updateNodes(nodes) {
    nodes.forEach((node, index) => {
      node.data.width = 130 + index * 13;
      node.data.height = 100 + index * 7;
    });
  }
};
const ordinary = data(['grandparent', 'parent', 'partner', 'child', 'unrelated', 'other', 'isolated'], [
  family('f1', ['grandparent'], ['parent']),
  family('f2', ['parent', 'partner'], ['child']),
  family('f3', ['unrelated'], ['other'])
]);

function assertCoverage(input, horizontal) {
  const { nodes, families } = overviewNodes(input, input.indis[0].id, renderer, horizontal);
  assert.deepEqual(nodes.map(node => node.data.indi.id).sort(), input.indis.map(person => person.id).sort());
  const ids = new Set(nodes.map(node => node.id));
  assert.equal(ids.size, input.indis.length);
  for (const family of families) {
    for (const id of [...family.partners, ...family.children]) assert(ids.has(id));
    const source = input.fams.find(item => item.id === family.id);
    assert.deepEqual(family.partners, [source.husb, source.wife].filter(Boolean));
    assert.deepEqual(family.children, source.children);
  }
  for (const node of nodes) {
    assert(Number.isFinite(node.x) && Number.isFinite(node.y));
    for (const other of nodes) {
      if (node === other) continue;
      const dx = Math.abs(node.x - other.x);
      const dy = Math.abs(node.y - other.y);
      assert(dx >= (node.data.width + other.data.width) / 2 ||
        dy >= (node.data.height + other.data.height) / 2, `${node.id} overlaps ${other.id}`);
    }
  }
  return nodes;
}

for (const horizontal of [false, true]) {
  test(`overview includes disconnected families and isolated people (${horizontal ? 'horizontal' : 'vertical'})`, () => {
    const nodes = assertCoverage(ordinary, horizontal);
    const parent = nodes.find(node => node.id === 'parent');
    const child = nodes.find(node => node.id === 'child');
    assert(horizontal ? parent.x < child.x : parent.y < child.y);
  });

  test(`overview retains multiple partnerships and cycles (${horizontal ? 'horizontal' : 'vertical'})`, () => {
    assertCoverage(data(['a', 'b', 'c', 'd', 'e'], [
      family('f1', ['a', 'b'], ['c']),
      family('f2', ['a', 'd'], ['e']),
      family('f3', ['c'], ['a'])
    ]), horizontal);
  });
}

test('overview does not mutate data or depend on a selected person', () => {
  const original = structuredClone(ordinary);
  const first = overviewNodes(ordinary, 'parent', renderer, false);
  const second = overviewNodes(ordinary, null, renderer, false);
  assert.equal(first.nodes.length, second.nodes.length);
  assert.deepEqual(ordinary, original);
});
