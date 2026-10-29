import assert from 'node:assert/strict';
import test, { after } from 'node:test';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { buildSync } from 'esbuild';

const directory = mkdtempSync(join(tmpdir(), 'ahnen-overview-layout-tests-'));
const output = join(directory, 'layout.mjs');
after(() => rmSync(directory, { recursive: true, force: true }));
buildSync({
  entryPoints: [fileURLToPath(new URL('../ClientSrc/tree-overview-layout.ts', import.meta.url))],
  bundle: true, platform: 'node', format: 'esm', outfile: output
});
const { layoutOverview, COUPLE_GAP } = await import(pathToFileURL(output).href);

const CARD = { cardWidth: 130, cardHeight: 100 };
const options = (horizontal = false) => ({ ...CARD, horizontal, aspect: 16 / 9 });

/** Same shape the C# ChartDataService produces for Topola. */
function toTopola(tree) {
  const sex = new Map(tree.persons.map(p => [p.id, p.sex]));
  const year = date => (date ? Number(/(\d{4})$/.exec(date)[1]) : null);
  const fams = tree.families.map(family => {
    const [a, b] = family.partnerIds;
    let husb = a;
    let wife = b;
    if (sex.get(a) === 'F' && sex.get(b) !== 'F') [husb, wife] = [b, a];
    const marriage = family.events.find(e => e.type === 'MARR');
    const status = family.events.some(e => e.type === 'DIV') ? 'DIV'
      : family.events.some(e => e.type === 'EVEN') ? 'SEP'
        : marriage ? 'MARR' : family.events.some(e => e.type === 'ENGA') ? 'ENGA' : 'NONE';
    const pedigrees = Object.fromEntries(family.childRefs.filter(c => c.pedigree !== 'BIRTH').map(c => [c.personId, c.pedigree]));
    return {
      id: family.id, husb, wife, children: family.childRefs.map(c => c.personId),
      status, marriageYear: year(marriage?.date), pedigrees
    };
  });
  return { indis: tree.persons.map(p => ({ id: p.id, sex: p.sex })), fams };
}

const family = (id, partners, children, extra = {}) => ({ id, husb: partners[0], wife: partners[1], children, ...extra });
const data = (ids, fams) => ({ indis: ids.map(id => ({ id })), fams });

function boxOf(card, horizontal = false) {
  const w = CARD.cardWidth;
  const h = CARD.cardHeight;
  return { left: card.x - w / 2, right: card.x + w / 2, top: card.y - h / 2, bottom: card.y + h / 2, horizontal };
}

function assertNoOverlap(layout) {
  const boxes = layout.cards.map(card => ({ card, ...boxOf(card) }));
  for (let i = 0; i < boxes.length; i++)
    for (let j = i + 1; j < boxes.length; j++) {
      const a = boxes[i];
      const b = boxes[j];
      const apart = a.right <= b.left + 0.001 || b.right <= a.left + 0.001 || a.bottom <= b.top + 0.001 || b.bottom <= a.top + 0.001;
      assert(apart, `cards ${a.card.id} and ${b.card.id} overlap`);
    }
}

function assertWithinBounds(layout) {
  const { x, y, width, height } = layout.bounds;
  for (const card of layout.cards) {
    assert(card.x - CARD.cardWidth / 2 >= x && card.x + CARD.cardWidth / 2 <= x + width, `${card.id} outside bounds (x)`);
    assert(card.y - CARD.cardHeight / 2 >= y && card.y + CARD.cardHeight / 2 <= y + height, `${card.id} outside bounds (y)`);
  }
}

function assertEveryoneOnce(layout, input) {
  const real = layout.cards.filter(card => !card.ghost).map(card => card.person).sort();
  assert.deepEqual(real, input.indis.map(person => person.id).sort());
  assert.equal(new Set(layout.cards.map(card => card.id)).size, layout.cards.length, 'card ids are unique');
}

const sample = toTopola(JSON.parse(readFileSync(new URL('../data/familytree.json', import.meta.url), 'utf8')));

for (const horizontal of [false, true])
  test(`200-person tree (${horizontal ? 'horizontal' : 'vertical'}): complete, tidy and compact`, () => {
    const layout = layoutOverview(sample, 'I1', options(horizontal));
    assertEveryoneOnce(layout, sample);
    assertNoOverlap(layout);
    assertWithinBounds(layout);
    assert.deepEqual([...layout.warnings], []);

    const { width, height } = layout.bounds;
    const ratio = Math.max(width / height, height / width);
    assert(ratio <= 3, `aspect ratio ${ratio.toFixed(2)} is at most 3:1`);
    const fitScale = Math.min(1600 / width, 800 / height);
    assert(fitScale >= 0.2, `fit scale ${fitScale.toFixed(3)} is at least 0.2 (was 0.07)`);
    console.log(`  ${horizontal ? 'h' : 'v'}: ${Math.round(width)}x${Math.round(height)} ratio ${ratio.toFixed(2)} fit ${fitScale.toFixed(3)} ghosts ${layout.stats.ghosts} blocks ${layout.stats.blocks}`);
  });

test('200-person tree: parents sit above their children and each marriage has a junction', () => {
  const layout = layoutOverview(sample, 'I1', options(false));
  const primary = new Map(layout.cards.filter(card => !card.ghost).map(card => [card.person, card]));
  const origin = new Map();
  for (const fam of sample.fams) for (const child of fam.children) if (!origin.has(child)) origin.set(child, fam);
  for (const [child, fam] of origin) {
    for (const parent of [fam.husb, fam.wife].filter(Boolean))
      assert(primary.get(parent).y < primary.get(child).y || layout.cards.some(c => c.ghost && c.person === parent),
        `${parent} above ${child}`);
  }
  for (const fam of sample.fams)
    assert.equal(layout.junctions.filter(junction => junction.family === fam.id && !junction.continued).length, 1, `junction for ${fam.id}`);
  const dropsFor = id => layout.links.filter(link => link.kind === 'drop' && link.family === id).length;
  for (const fam of sample.fams) assert.equal(dropsFor(fam.id), fam.children.length ? 1 : 0, `drop for ${fam.id}`);
  const stubs = layout.links.filter(link => link.kind === 'child');
  assert.equal(stubs.length, sample.fams.reduce((sum, fam) => sum + fam.children.length, 0));
});

test('layout is deterministic', () => {
  const a = layoutOverview(sample, 'I1', options());
  const b = layoutOverview(sample, 'I1', options());
  assert.deepEqual(a.cards, b.cards);
  assert.deepEqual(a.links, b.links);
});

test('a person married three times is drawn once with spouses on both sides in marriage order', () => {
  const input = data(['m', 'w1', 'w2', 'w3', 'a', 'b', 'c'], [
    family('f3', ['m', 'w3'], ['c'], { marriageYear: 1990 }),
    family('f1', ['m', 'w1'], ['a'], { marriageYear: 1970 }),
    family('f2', ['m', 'w2'], ['b'], { marriageYear: 1980 })
  ]);
  const layout = layoutOverview(input, 'm', options());
  assertEveryoneOnce(layout, input);
  assert.equal(layout.cards.filter(card => card.person === 'm').length, 1);
  const x = id => layout.cards.find(card => card.person === id).x;
  assert(x('w1') < x('m') && x('m') < x('w2') && x('w2') < x('w3'));
  assert.equal(x('m') - x('w1'), 130 + COUPLE_GAP);
  assert(x('a') < x('b') && x('b') < x('c'), 'children follow marriage order');
  assertNoOverlap(layout);
  assert.equal(layout.links.filter(link => link.kind === 'spouse').length, 3);
});

test('single parent, childless couple and unrelated people are all placed', () => {
  const input = data(['mum', 'kid', 'h', 'w', 'x', 'y'], [
    family('f1', ['mum'], ['kid']),
    family('f2', ['h', 'w'], [])
  ]);
  const layout = layoutOverview(input, 'mum', options());
  assertEveryoneOnce(layout, input);
  assertNoOverlap(layout);
  const junction = layout.junctions.find(item => item.family === 'f1');
  const mum = layout.cards.find(card => card.person === 'mum');
  assert.equal(junction.x, mum.x);
  assert.equal(layout.links.filter(link => link.family === 'f2' && link.kind !== 'spouse').length, 0);
});

test('a marriage between two branches uses one ghost card and a dotted link', () => {
  const input = data(['a', 'b', 'a1', 'b1', 'k'], [
    family('f1', ['a'], ['a1']),
    family('f2', ['b'], ['b1']),
    family('f3', ['a1', 'b1'], ['k'])
  ]);
  const layout = layoutOverview(input, 'a', options());
  assertEveryoneOnce(layout, input);
  assert.equal(layout.cards.filter(card => card.ghost).length, 1);
  assert.equal(layout.links.filter(link => link.kind === 'ghost').length, 1);
  assertNoOverlap(layout);
});

test('an earlier marriage of an in-marrying partner is shown through a ghost host', () => {
  const input = data(['p', 'q', 'r', 'kid', 'stepkid'], [
    family('f1', ['p'], ['q']),
    family('f2', ['q', 'r'], ['kid']),
    family('f0', ['r', 'old'], ['stepkid'])
  ]);
  input.indis.push({ id: 'old' });
  const layout = layoutOverview(input, 'p', options());
  assertEveryoneOnce(layout, input);
  assert(layout.cards.some(card => card.ghost && card.person === 'r'));
  assertNoOverlap(layout);
});

test('parent/child cycles keep everybody visible and warn', () => {
  const input = data(['a', 'b'], [family('f1', ['a'], ['b']), family('f2', ['b'], ['a'])]);
  const layout = layoutOverview(input, 'a', options());
  assertEveryoneOnce(layout, input);
  assert(layout.warnings.size > 0);
});

test('empty data and a single person work', () => {
  assert.equal(layoutOverview(data([], []), null, options()).cards.length, 0);
  assert.equal(layoutOverview(data(['x'], []), 'x', options()).cards.length, 1);
});

test('adopted children keep their place and are flagged', () => {
  const input = data(['p', 'q', 'c'], [family('f1', ['p', 'q'], ['c'], { pedigrees: { c: 'ADOPTED' } })]);
  const layout = layoutOverview(input, 'p', options());
  assert.equal(layout.links.find(link => link.kind === 'child').adopted, true);
});

test('generation lanes are labelled like the 3D view', () => {
  const input = data(['g', 'f', 'k'], [family('f1', ['g'], ['f']), family('f2', ['f'], ['k'])]);
  const layout = layoutOverview(input, 'f', options());
  assert.deepEqual(layout.lanes.map(lane => lane.label), ['G+1', 'G0', 'G-1']);
});
