import type { JsonFam, JsonGedcomData, JsonIndi } from 'topola';

/**
 * Cuts the data a Topola chart can reach to `depth` generations of ancestors and descendants
 * around the focus person. Nothing is deleted from the source: copies of the boundary people and
 * families lose their onward links, so the chart simply stops there. `depth` 0 keeps everything.
 */
export function limitDepth(data: JsonGedcomData, focusId: string | null, depth: number): JsonGedcomData {
  if (!focusId || !(depth > 0)) return data;
  const people = new Map(data.indis.map(person => [person.id, person]));
  const families = new Map(data.fams.map(family => [family.id, family]));
  if (!people.has(focusId)) return data;

  const cutParents = new Set<string>();
  const cutChildren = new Set<string>();
  const seen = new Set<string>([focusId]);
  let layer = [focusId];
  for (let level = 0; level < depth && layer.length; level++) {
    const next: string[] = [];
    for (const id of layer) {
      const famc = people.get(id)?.famc;
      const family = famc ? families.get(famc) : undefined;
      for (const parent of [family?.husb, family?.wife])
        if (parent && people.has(parent) && !seen.has(parent)) {
          seen.add(parent);
          next.push(parent);
        }
    }
    layer = next;
  }
  for (const id of layer) cutParents.add(id);

  const below = new Set<string>([focusId]);
  layer = [focusId];
  for (let level = 0; level < depth && layer.length; level++) {
    const next: string[] = [];
    for (const id of layer)
      for (const famId of people.get(id)?.fams ?? [])
        for (const child of families.get(famId)?.children ?? [])
          if (people.has(child) && !below.has(child)) {
            below.add(child);
            next.push(child);
          }
    layer = next;
  }
  for (const id of layer) cutChildren.add(id);

  const trimmedFamilies = new Map<string, JsonFam>();
  for (const id of cutChildren)
    for (const famId of people.get(id)?.fams ?? []) {
      const family = families.get(famId);
      if (family?.children?.length) trimmedFamilies.set(famId, { ...family, children: [] });
    }
  return {
    indis: data.indis.map((person): JsonIndi => {
      if (!cutParents.has(person.id) || !person.famc) return person;
      // Someone cut off above can still be the focus person's partner or descendant; those keep their parents.
      return below.has(person.id) ? person : { ...person, famc: undefined };
    }),
    fams: data.fams.map(family => trimmedFamilies.get(family.id) ?? family)
  };
}
