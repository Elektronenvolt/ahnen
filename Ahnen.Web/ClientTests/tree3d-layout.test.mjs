import assert from 'node:assert/strict';
import test, { after } from 'node:test';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { buildSync } from 'esbuild';

const directory = mkdtempSync(join(tmpdir(), 'ahnen-layout-tests-'));
const output = join(directory, 'layout.mjs');
after(() => rmSync(directory, { recursive: true, force: true }));
buildSync({
  entryPoints: [fileURLToPath(new URL('../ClientSrc/tree3d-layout.ts', import.meta.url))],
  bundle: true, platform: 'node', format: 'esm', outfile: output
});
const { createTreeLayout, generationIdentifier, neighborhood, TERRACE_STEP } = await import(pathToFileURL(output).href);

const family = (id, partners, children) =>
  ({ id, husb: partners[0], wife: partners[1], children });
const data = (ids, fams) =>
  ({ indis: ids.map(id => ({ id })), fams });
const ordinary = data(['grandpa', 'grandma', 'father', 'mother', 'kid', 'sibling'], [
  family('f1', ['grandpa', 'grandma'], ['father']),
  family('f2', ['father', 'mother'], ['kid', 'sibling'])
]);

test('ordinary generations have correct signed levels, shared partner ranks, and terrace heights', () => {
  const layout = createTreeLayout(ordinary, 'father');
  for (const [id, rank] of [['grandpa', -1], ['grandma', -1], ['father', 0], ['mother', 0], ['kid', 1]])
    assert.equal(layout.positions.get(id).generation, rank);
  assert.equal(layout.positions.get('grandpa').y - layout.positions.get('father').y, TERRACE_STEP);
  assert.equal(layout.warnings.size, 0);
});

test('displayed generations increase above root and decrease below root without moving terraces', () => {
  const layout = createTreeLayout(ordinary, 'father');
  assert.equal(generationIdentifier(layout.positions.get('grandpa').generation), 'G+1');
  assert.equal(generationIdentifier(layout.positions.get('father').generation), 'G0');
  assert.equal(generationIdentifier(layout.positions.get('kid').generation), 'G-1');
  assert.equal(generationIdentifier(-2), 'G+2');
  assert.equal(generationIdentifier(2), 'G-2');
  assert(layout.positions.get('grandpa').y > layout.positions.get('father').y);
  assert(layout.positions.get('father').y > layout.positions.get('kid').y);
});

test('people can simultaneously be parents and children without age assumptions', () => {
  const layout = createTreeLayout(ordinary, 'father');
  const focus = neighborhood(layout, 'father');
  assert.deepEqual([...focus.roles.get('grandpa')], ['parent']);
  assert.deepEqual([...focus.roles.get('mother')], ['partner']);
  assert.deepEqual([...focus.roles.get('kid')], ['child']);
  assert.deepEqual([...neighborhood(layout, 'kid').roles.get('father')], ['parent']);
  assert.equal(focus.families.size, 2);
});

test('partner households are kept together and stored sibling order is preserved', () => {
  const layout = createTreeLayout(ordinary, 'father');
  assert(layout.clusters.some(cluster => cluster.people.includes('father') && cluster.people.includes('mother')));
  assert(layout.positions.get('kid').z < layout.positions.get('sibling').z);
});

test('single parents, multiple partners and half-siblings retain every link and marker', () => {
  const input = data(['p', 'a', 'b', 'c', 'd', 'e'], [
    family('a', ['p', 'a'], ['c']),
    family('b', ['p', 'b'], ['d']),
    family('c', ['a'], ['e'])
  ]);
  const layout = createTreeLayout(input, 'p');
  assert.equal(layout.positions.size, 6);
  assert.equal(layout.families.length, 3);
  assert.deepEqual([...neighborhood(layout, 'p').roles.keys()].sort(), ['a', 'b', 'c', 'd', 'p']);
  assert.equal(layout.positions.get('d').generation, 1);
});

test('sibling households follow recorded child order rather than alphabetical identifiers', () => {
  const layout = createTreeLayout(data(['p', 'z', 'a', 'sz', 'sa'], [
    family('origin', ['p'], ['z', 'a']),
    family('az', ['z', 'sz'], []), family('aa', ['a', 'sa'], [])
  ]), 'p');
  assert(layout.positions.get('z').z < layout.positions.get('a').z);
});

test('pedigree collapse retains one marker per ancestor and all ancestral connections', () => {
  const input = data(['g', 's', 'p', 'q', 'x', 'y', 'a', 'b', 'root'], [
    family('1', ['g', 's'], ['p', 'q']),
    family('2', ['p', 'x'], ['a']),
    family('3', ['q', 'y'], ['b']),
    family('4', ['a', 'b'], ['root'])
  ]);
  const layout = createTreeLayout(input, 'root');
  assert.equal(layout.positions.size, input.indis.length);
  assert.equal(layout.positions.get('g').generation, -3);
  assert.equal(layout.warnings.size, 0);
  assert.equal(layout.families.flatMap(f => f.children).length, 5);
});

test('additional/adoptive parent families are not dropped', () => {
  const layout = createTreeLayout(data(['birth', 'adopt', 'kid'], [
    family('birth-family', ['birth'], ['kid']), family('adopt-family', ['adopt'], ['kid'])
  ]), 'kid');
  assert(neighborhood(layout, 'kid').roles.get('birth').has('parent'));
  assert(neighborhood(layout, 'kid').roles.get('adopt').has('parent'));
  assert.equal(layout.families.length, 2);
});

test('unequal ancestral paths preserve parent-before-child direction instead of first-visited levels', () => {
  const input = data(['a', 'b', 'c', 'd'], [
    family('1', ['a'], ['b', 'd']), family('2', ['b'], ['c']), family('3', ['c'], ['d'])
  ]);
  const layout = createTreeLayout(input, 'd');
  for (const link of layout.families)
    for (const parent of link.partners)
      for (const child of link.children)
        assert(layout.positions.get(parent).generation < layout.positions.get(child).generation);
  assert(layout.warnings.has('conflicting-levels'));
  assert.equal(layout.positions.size, 4);
});

test('ancestral partners cannot collapse a directed path into a cycle', () => {
  const layout = createTreeLayout(data(['a', 'b', 'c'], [
    family('1', ['a'], ['b']), family('2', ['b'], ['c']), family('3', ['a', 'c'], [])
  ]), 'b');
  assert(layout.warnings.has('conflicting-levels'));
  assert(layout.positions.get('a').generation < layout.positions.get('c').generation);
});

test('cycles surface a warning while keeping all people and links', () => {
  const layout = createTreeLayout(data(['a', 'b'], [
    family('a', ['a'], ['b']), family('b', ['b'], ['a'])
  ]), 'a');
  assert(layout.warnings.has('cycle'));
  assert.equal(layout.positions.size, 2);
  assert.equal(layout.families.length, 2);
});

test('unconnected components are separate and not classified as relatives of the root', () => {
  const layout = createTreeLayout(data(['root', 'other', 'child'], [family('f', ['other'], ['child'])]), 'root');
  assert.equal(layout.positions.get('root').component, 0);
  assert.notEqual(layout.positions.get('other').component, 0);
  assert.equal(neighborhood(layout, 'root').roles.size, 1);
  const first = layout.clusters.filter(cluster => cluster.component === 0);
  const others = layout.clusters.filter(cluster => cluster.component !== 0);
  assert(Math.max(...first.map(c => c.z + c.depth / 2)) < Math.min(...others.map(c => c.z - c.depth / 2)));
});

test('layout is deterministic under person and family input reordering and never mutates input', () => {
  const before = JSON.stringify(ordinary);
  const a = createTreeLayout(ordinary, 'father');
  const b = createTreeLayout({ indis: [...ordinary.indis].reverse(), fams: [...ordinary.fams].reverse() }, 'father');
  assert.deepEqual(a.positions, b.positions);
  assert.equal(JSON.stringify(ordinary), before);
});

test('clusters never overlap within a generation, even with large sibling groups', () => {
  const kids = Array.from({ length: 80 }, (_, i) => `kid${i}`);
  const layout = createTreeLayout(data(['p', 'q', ...kids], [family('f', ['p', 'q'], kids)]), 'p');
  for (const cluster of layout.clusters) {
    for (const id of cluster.people) {
      const position = layout.positions.get(id);
      assert(position.z - 0.6 > cluster.z - cluster.depth / 2);
      assert(position.z + 1.65 < cluster.z + cluster.depth / 2);
    }
  }
  for (const a of layout.clusters)
    for (const b of layout.clusters)
      if (a !== b && a.generation === b.generation)
        assert(Math.abs(a.z - b.z) >= (a.depth + b.depth) / 2);
});

test('empty data, missing references and unknown focus are handled without fabricated relatives', () => {
  assert.equal(createTreeLayout(data([], []), null).positions.size, 0);
  const layout = createTreeLayout(data(['p'], [family('f', ['missing'], ['p'])]), 'missing');
  assert.equal(layout.rootId, 'p');
  assert.equal(neighborhood(layout, 'missing').roles.size, 0);
  assert.equal(layout.positions.size, 1);
});
