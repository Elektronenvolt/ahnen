import assert from 'node:assert/strict';
import test from 'node:test';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { generateSampleTree } from '../tools/generate-sample-tree.mjs';

const tree = generateSampleTree(42, 200);
const partnerCount = new Map();
for (const family of tree.families)
  for (const id of family.partnerIds) partnerCount.set(id, (partnerCount.get(id) ?? 0) + 1);

test('generates exactly 200 distinct, consistent people', () => {
  assert.equal(tree.persons.length, 200);
  const ids = new Set(tree.persons.map(p => p.id));
  assert.equal(ids.size, 200);
  for (const family of tree.families)
    for (const id of [...family.partnerIds, ...family.childRefs.map(c => c.personId)]) assert(ids.has(id));
  const childLinks = tree.families.flatMap(f => f.childRefs.map(c => c.personId));
  assert.equal(new Set(childLinks).size, childLinks.length, 'every child belongs to one family');
});

test('children per couple are 0 or 1-6 and all sizes occur', () => {
  const sizes = new Set();
  for (const family of tree.families) {
    assert(family.childRefs.length <= 6);
    sizes.add(family.childRefs.length);
  }
  for (const size of [0, 1, 2, 3, 4, 5, 6]) assert(sizes.has(size), `family with ${size} children`);
});

test('covers single parents, repeated marriages, childless couples and partnership kinds', () => {
  assert(tree.families.some(f => f.partnerIds.length === 1 && f.childRefs.length > 0));
  assert([...partnerCount.values()].filter(n => n >= 2).length >= 5);
  assert(tree.families.some(f => f.partnerIds.length === 2 && f.childRefs.length === 0));
  assert(tree.families.some(f => f.events.some(e => e.type === 'DIV')));
  assert(tree.families.some(f => f.partnerIds.length === 2 && f.events.length === 0));
  assert(tree.families.some(f => f.childRefs.some(c => c.pedigree === 'ADOPTED')));
  const parented = new Set(tree.families.flatMap(f => f.childRefs.map(c => c.personId)));
  assert(tree.families.some(f => f.partnerIds.length === 2 && f.partnerIds.every(id => parented.has(id))),
    'a marriage between two branches');
  const connected = new Set(tree.families.flatMap(f => [...f.partnerIds, ...f.childRefs.map(c => c.personId)]));
  assert(tree.persons.some(p => !connected.has(p.id)), 'isolated people');
});

test('uses only GEDCOM-legal values and is deterministic', () => {
  const sex = new Set(['M', 'F', 'X', 'U']);
  const pedigree = new Set(['BIRTH', 'ADOPTED', 'FOSTER', 'SEALING', 'OTHER']);
  const familyEvents = new Set(['MARR', 'DIV', 'ANUL', 'DIVF', 'ENGA', 'MARB', 'MARC', 'MARL', 'EVEN']);
  for (const person of tree.persons) {
    assert(sex.has(person.sex));
    for (const event of person.events) assert.match(event.date, /^\d{1,2} [A-Z]{3} \d{4}$/);
  }
  for (const family of tree.families) {
    for (const event of family.events) assert(familyEvents.has(event.type));
    for (const child of family.childRefs) assert(pedigree.has(child.pedigree));
  }
  assert.deepEqual(generateSampleTree(42, 200), tree);
  assert.notDeepEqual(generateSampleTree(7, 200), tree);
});

test('the committed sample file matches the generator', () => {
  const file = fileURLToPath(new URL('../seed/sample-200/familytree.json', import.meta.url));
  assert.deepEqual(JSON.parse(readFileSync(file, 'utf8')), tree);
});
