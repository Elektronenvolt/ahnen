// Deterministic generator for a GEDCOM-shaped sample family tree (default: 200 people).
//   node tools/generate-sample-tree.mjs [outFile] [seed] [total]
// Covers: 1-6 children per couple (some childless), single parents, people married several times
// (earlier marriages divorced/separated), unmarried partnerships, adopted children, marriages across
// branches, disconnected families and isolated people.
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const MALE = ['Hans', 'Karl', 'Otto', 'Fritz', 'Wilhelm', 'Georg', 'Ernst', 'Paul', 'Max', 'Jonas', 'Lukas', 'Felix',
  'Anton', 'Emil', 'Ludwig', 'Heinrich', 'Walter', 'Peter', 'Jan', 'Tim', 'Leon', 'Noah', 'Ben', 'Finn'];
const FEMALE = ['Anna', 'Marie', 'Emma', 'Clara', 'Greta', 'Lena', 'Helga', 'Ida', 'Frieda', 'Berta', 'Elsa', 'Lotte',
  'Sophie', 'Hanna', 'Mia', 'Lea', 'Nora', 'Eva', 'Ruth', 'Inge', 'Klara', 'Paula', 'Julia', 'Laura'];
const SURNAMES = ['Mustermann', 'Schmidt', 'Weber', 'Fischer', 'Wagner', 'Becker', 'Hoffmann', 'Koch', 'Richter',
  'Klein', 'Wolf', 'Neumann', 'Braun', 'Zimmer', 'Krüger', 'Lehmann', 'Schulz', 'Vogel', 'Keller', 'Arnold'];
const MONTHS = ['JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN', 'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC'];
const PLACES = ['München', 'Hamburg', 'Berlin', 'Köln', 'Leipzig', 'Dresden', 'Bremen', 'Wien'];
const EXTRA_COMPONENT_SIZE = 4;

function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function generateSampleTree(seed = 42, total = 200) {
  const random = mulberry32(seed);
  const int = (min, max) => min + Math.floor(random() * (max - min + 1));
  const pick = list => list[Math.floor(random() * list.length)];
  const date = year => `${int(1, 28)} ${pick(MONTHS)} ${year}`;
  const persons = [];
  const families = [];
  const birthOf = new Map();
  const kidsOf = new Map();
  const inFamily = new Set();

  const addPerson = (sex, surname, birth) => {
    const id = `I${persons.length + 1}`;
    const given = pick(sex === 'M' ? MALE : FEMALE);
    const events = [{ type: 'BIRT', date: date(birth), place: pick(PLACES) }];
    if (birth < 1935 || (birth < 1975 && random() < 0.1))
      events.push({ type: 'DEAT', date: date(birth + int(45, 92)) });
    persons.push({
      id, names: [{ type: 'BIRTH', given, surname }], sex, events,
      address: { lines: [], city: null, postalCode: null, country: null }, notes: [], media: []
    });
    birthOf.set(id, birth);
    kidsOf.set(id, 0);
    return persons[persons.length - 1];
  };
  const person = id => persons[Number(id.slice(1)) - 1];
  const addFamily = (partners, status = 'NONE', year = 0, endYear = 0) => {
    const family = { id: `F${families.length + 1}`, partnerIds: partners.map(p => p.id), events: [], childRefs: [] };
    if (status === 'ENGA') family.events.push({ type: 'ENGA', date: date(year) });
    else if (status !== 'NONE') family.events.push({ type: 'MARR', date: date(year), place: pick(PLACES) });
    if (status === 'DIV') family.events.push({ type: 'DIV', date: date(endYear) });
    if (status === 'SEP') family.events.push({ type: 'EVEN', typeDetail: 'Separation', date: date(endYear) });
    families.push(family);
    for (const p of partners) inFamily.add(p.id);
    return family;
  };
  const spouseFor = (p, year) => {
    let sex = p.sex === 'M' ? 'F' : 'M';
    if (random() < 0.04) sex = p.sex;
    const spouse = addPerson(sex, pick(SURNAMES), year + int(-4, 4));
    if (sex === 'F' && p.sex === 'M')
      spouse.names.push({ type: 'MARRIED', given: spouse.names[0].given, surname: p.names[0].surname });
    return spouse;
  };

  const extrasBudget = 2 * (2 + EXTRA_COMPONENT_SIZE) + 3;
  const mainTarget = total - extrasBudget;
  const queue = [];
  for (let i = 0; i < 3; i++) queue.push(addPerson('M', SURNAMES[i], 1845 + i * 4));

  while (queue.length && persons.length < mainTarget) {
    const p = queue.splice(random() < 0.35 ? Math.floor(random() * queue.length) : 0, 1)[0];
    const birth = birthOf.get(p.id);
    if (birth > 2005) continue;
    const roll = random();
    const marriages = roll < 0.2 ? 0 : roll < 0.7 ? 1 : roll < 0.93 ? 2 : 3;
    let start = birth + int(20, 32);
    const units = [];
    if (marriages === 0 && random() < 0.6) units.push([addFamily([p]), start]);
    for (let k = 0; k < marriages; k++) {
      if (mainTarget - persons.length < 2) break;
      let partner = null;
      if (random() < 0.12) {
        const candidates = persons.filter(q => q !== p && Math.abs(birthOf.get(q.id) - birth) <= 8
          && kidsOf.get(q.id) === 0 && !inFamily.has(q.id) && q.sex !== p.sex && birthOf.get(q.id) > 1850
          && q.names[0].surname !== p.names[0].surname);
        if (candidates.length) partner = pick(candidates);
      }
      partner ??= spouseFor(p, birth);
      const year = start + int(-3, 1);
      const earlier = k < marriages - 1;
      const status = earlier ? (random() < 0.8 ? 'DIV' : 'SEP') : random() < 0.1 ? 'NONE' : 'MARR';
      units.push([addFamily([p, partner], status, year, year + int(4, 14)), start]);
      start += int(8, 14);
    }
    for (const [family, first] of units) {
      let kids = random() < 0.18 ? 0 : int(1, 6);
      kids = Math.min(kids, mainTarget - persons.length);
      const father = family.partnerIds.map(person).find(q => q.sex === 'M') ?? person(family.partnerIds[0]);
      let year = first;
      for (let i = 0; i < kids; i++) {
        const child = addPerson(random() < 0.5 ? 'M' : 'F', father.names[0].surname, year);
        const adopted = random() < 0.04;
        family.childRefs.push({ personId: child.id, pedigree: adopted ? 'ADOPTED' : 'BIRTH' });
        year += int(1, 4);
        queue.push(child);
        for (const id of family.partnerIds) kidsOf.set(id, kidsOf.get(id) + 1);
      }
    }
  }

  for (let i = 0; i < 2; i++) {
    const husband = addPerson('M', `Fremd${i}`, 1960 + i * 7);
    const wife = addPerson('F', `Gast${i}`, 1962 + i * 7);
    const family = addFamily([husband, wife], 'MARR', 1985 + i);
    for (let j = 0; j < EXTRA_COMPONENT_SIZE; j++) {
      const child = addPerson(random() < 0.5 ? 'M' : 'F', `Fremd${i}`, 1988 + j * 3 + i);
      family.childRefs.push({ personId: child.id, pedigree: 'BIRTH' });
    }
  }
  while (persons.length < total)
    addPerson(random() < 0.5 ? 'M' : 'F', `Einzel${persons.length}`, 1950 + int(0, 40));
  if (persons.length !== total) throw new Error(`generated ${persons.length} people, expected ${total}`);

  return { schemaVersion: 1, rootPersonId: 'I1', persons, families };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const [out = 'seed/sample-200/familytree.json', seed = '42', total = '200'] = process.argv.slice(2);
  const data = generateSampleTree(Number(seed), Number(total));
  mkdirSync(dirname(out), { recursive: true });
  writeFileSync(out, `${JSON.stringify(data, null, 2)}\n`);
  console.log(`${data.persons.length} persons, ${data.families.length} families -> ${out}`);
}
