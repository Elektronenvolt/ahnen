import type { JsonGedcomData, JsonIndi } from 'topola';

export const PERSON_SCALE = 2;
export const PERSON_LABEL = { width: 2.9, depth: 0.96, x: 0, z: 1.13 } as const;
export const PERSON_BOUNDS = {
  minX: Math.min(-0.6, PERSON_LABEL.x - PERSON_LABEL.width / 2) * PERSON_SCALE,
  maxX: Math.max(0.6, PERSON_LABEL.x + PERSON_LABEL.width / 2) * PERSON_SCALE,
  minZ: -0.6 * PERSON_SCALE,
  maxZ: (PERSON_LABEL.z + PERSON_LABEL.depth / 2) * PERSON_SCALE
};
export const PLATFORM_WIDTH = 8.8;
export const TERRACE_SPACING = PLATFORM_WIDTH + 1.6;
export const TERRACE_STEP = 1.35;
export const PERSON_SPACING = PERSON_BOUNDS.maxZ - PERSON_BOUNDS.minZ + 1.2;
const CLUSTER_GAP = 1.5;

export function generationIdentifier(layoutLevel: number): string {
  // Layout ranks increase toward descendants; displayed generations increase toward ancestors.
  const generation = -layoutLevel;
  return `G${generation > 0 ? '+' : ''}${generation}`;
}

export interface PersonPosition {
  x: number;
  y: number;
  z: number;
  generation: number;
  component: number;
}

export interface FamilyCluster {
  id: string;
  people: string[];
  generation: number;
  component: number;
  x: number;
  y: number;
  z: number;
  depth: number;
}

export interface FamilyLink {
  id: string;
  partners: string[];
  children: string[];
}

export interface TreeLayout {
  rootId: string | null;
  people: Map<string, JsonIndi>;
  positions: Map<string, PersonPosition>;
  clusters: FamilyCluster[];
  families: FamilyLink[];
  warnings: Set<'cycle' | 'conflicting-levels'>;
}

export type PersonRole = 'self' | 'parent' | 'partner' | 'child';

export interface Neighborhood {
  roles: Map<string, Set<PersonRole>>;
  families: Set<string>;
}

function unique(ids: string[]): string[] {
  return [...new Set(ids)];
}

function average(values: number[]): number {
  return values.reduce((sum, value) => sum + value, 0) / values.length;
}

class Groups {
  private readonly parents: Map<string, string>;

  constructor(ids: string[]) {
    this.parents = new Map(ids.map(id => [id, id]));
  }

  find(id: string): string {
    const parent = this.parents.get(id)!;
    if (parent === id) return id;
    const root = this.find(parent);
    this.parents.set(id, root);
    return root;
  }

  join(a: string, b: string): void {
    const roots = [this.find(a), this.find(b)].sort();
    this.parents.set(roots[1], roots[0]);
  }
}

function adjacency(ids: string[], families: FamilyLink[], groups: Groups): Map<string, Set<string>> {
  const result = new Map<string, Set<string>>();
  for (const id of ids) result.set(groups.find(id), new Set());
  for (const family of families) {
    for (const parent of family.partners) {
      for (const child of family.children)
        result.get(groups.find(parent))!.add(groups.find(child));
    }
  }
  return result;
}

function reaches(graph: Map<string, Set<string>>, start: string, target: string): boolean {
  const visited = new Set([start]);
  const queue = [start];
  for (let i = 0; i < queue.length; i++) {
    for (const next of graph.get(queue[i]) ?? []) {
      if (next === target) return true;
      if (!visited.has(next)) {
        visited.add(next);
        queue.push(next);
      }
    }
  }
  return false;
}

function ranked(graph: Map<string, Set<string>>): Map<string, number> | null {
  const incoming = new Map([...graph.keys()].map(id => [id, 0]));
  for (const children of graph.values())
    for (const child of children) incoming.set(child, incoming.get(child)! + 1);
  const queue = [...incoming].filter(([, count]) => count === 0).map(([id]) => id).sort();
  const levels = new Map(queue.map(id => [id, 0]));
  for (let i = 0; i < queue.length; i++) {
    const id = queue[i];
    for (const child of graph.get(id)!) {
      levels.set(child, Math.max(levels.get(child) ?? 0, levels.get(id)! + 1));
      incoming.set(child, incoming.get(child)! - 1);
      if (incoming.get(child) === 0) queue.push(child);
    }
  }
  return queue.length === graph.size ? levels : null;
}

export function createTreeLayout(input: JsonGedcomData, preferredRoot: string | null): TreeLayout {
  const people = new Map([...input.indis].sort((a, b) => a.id.localeCompare(b.id)).map(p => [p.id, p]));
  const ids = [...people.keys()];
  const rootId = preferredRoot && people.has(preferredRoot) ? preferredRoot : ids[0] ?? null;
  const families: FamilyLink[] = [...input.fams].sort((a, b) => a.id.localeCompare(b.id)).map(family => ({
    id: family.id,
    partners: unique([family.husb, family.wife].filter((id): id is string => !!id && people.has(id))),
    children: unique((family.children ?? []).filter(id => people.has(id)))
  }));
  const warnings: TreeLayout['warnings'] = new Set();
  const components = new Groups(ids);
  for (const family of families) {
    const members = [...family.partners, ...family.children];
    for (const id of members.slice(1)) components.join(members[0], id);
  }
  const componentIds = unique(ids.map(id => components.find(id)));
  if (rootId) {
    const rootComponent = components.find(rootId);
    componentIds.sort((a, b) => a === rootComponent ? -1 : b === rootComponent ? 1 : a.localeCompare(b));
  }
  const positions = new Map<string, PersonPosition>();
  const clusters: FamilyCluster[] = [];
  let componentOffset = 0;

  componentIds.forEach((componentId, component) => {
    const members = ids.filter(id => components.find(id) === componentId);
    const memberSet = new Set(members);
    const componentFamilies = families.filter(family =>
      [...family.partners, ...family.children].some(id => memberSet.has(id)));
    const partners = new Groups(members);
    const plainGraph = adjacency(members, componentFamilies, partners);
    const hasCycle = ranked(plainGraph) === null;
    if (hasCycle) warnings.add('cycle');

    if (!hasCycle) {
      for (const family of componentFamilies) {
        if (family.partners.length !== 2) continue;
        const a = partners.find(family.partners[0]);
        const b = partners.find(family.partners[1]);
        if (a === b) continue;
        const graph = adjacency(members, componentFamilies, partners);
        // Partner proximity must not turn valid ancestry into a directed cycle.
        if (reaches(graph, a, b) || reaches(graph, b, a)) warnings.add('conflicting-levels');
        else partners.join(a, b);
      }
    }
    const ranks = ranked(adjacency(members, componentFamilies, partners));
    const anchor = component === 0 && rootId ? rootId : members[0];
    const anchorRank = ranks?.get(partners.find(anchor)) ?? 0;
    const generationOf = (id: string) => (ranks?.get(partners.find(id)) ?? 0) - anchorRank;
    for (const family of componentFamilies)
      for (const parent of family.partners)
        for (const child of family.children)
          if (generationOf(child) - generationOf(parent) !== 1 && !hasCycle)
            warnings.add('conflicting-levels');

    const partnerMembers = new Map<string, string[]>();
    for (const id of members) {
      const key = partners.find(id);
      const group = partnerMembers.get(key) ?? [];
      group.push(id);
      partnerMembers.set(key, group);
    }
    const originFamily = new Map<string, string>();
    const recordedOrder = new Map<string, number>();
    for (const family of componentFamilies)
      family.children.forEach((id, index) => {
        if (!originFamily.has(id)) {
          originFamily.set(id, family.id);
          recordedOrder.set(id, index);
        }
      });

    const clusterGroups = new Map<string, string[]>();
    for (const id of members) {
      const partnerGroup = partners.find(id);
      const siblingFamily = originFamily.get(id);
      const key = partnerMembers.get(partnerGroup)!.length > 1
        ? `partners:${partnerGroup}`
        : siblingFamily ? `siblings:${siblingFamily}:${generationOf(id)}` : `person:${id}`;
      const group = clusterGroups.get(key) ?? [];
      group.push(id);
      clusterGroups.set(key, group);
    }
    const layers = new Map<number, FamilyCluster[]>();
    const clusterOrder = new Map<string, { family: string; index: number }>();
    for (const [key, membersInCluster] of clusterGroups) {
      const peopleInCluster = membersInCluster.sort((a, b) =>
        (recordedOrder.get(a) ?? 0) - (recordedOrder.get(b) ?? 0) || a.localeCompare(b));
      const generation = generationOf(peopleInCluster[0]);
      const layer = layers.get(generation) ?? [];
      const cluster: FamilyCluster = {
        id: `${component}:${key}`, people: peopleInCluster, generation, component,
        x: generation * TERRACE_SPACING, y: 0, z: 0,
        depth: peopleInCluster.length * PERSON_SPACING
      };
      const origins = peopleInCluster.filter(id => originFamily.has(id)).map(id => ({
        family: originFamily.get(id)!, index: recordedOrder.get(id)!
      })).sort((a, b) => a.family.localeCompare(b.family) || a.index - b.index);
      if (origins[0]) clusterOrder.set(cluster.id, origins[0]);
      layer.push(cluster);
      layers.set(generation, layer);
    }
    const levels = [...layers.keys()].sort((a, b) => a - b);
    const maxGeneration = Math.max(...levels);
    const neighbors = new Map(members.map(id => [id, new Set<string>()]));
    for (const family of componentFamilies) {
      for (const parent of family.partners)
        for (const child of family.children) {
          neighbors.get(parent)!.add(child);
          neighbors.get(child)!.add(parent);
        }
    }
    const placeLayer = (level: number) => {
      const layer = layers.get(level)!;
      const depth = layer.reduce((sum, cluster) => sum + cluster.depth, 0) + (layer.length - 1) * CLUSTER_GAP;
      let z = -depth / 2;
      for (const cluster of layer) {
        cluster.y = (maxGeneration - level) * TERRACE_STEP;
        cluster.z = z + cluster.depth / 2;
        cluster.people.forEach((id, i) => positions.set(id, {
          x: cluster.x, y: cluster.y,
          z: cluster.z - (PERSON_BOUNDS.minZ + PERSON_BOUNDS.maxZ) / 2 +
            (i - (cluster.people.length - 1) / 2) * PERSON_SPACING,
          generation: level, component
        }));
        z += cluster.depth + CLUSTER_GAP;
      }
    };
    for (const level of levels) placeLayer(level);
    // Barycentric sweeps keep households intact while aligning them with related households.
    for (let pass = 0; pass < 4; pass++) {
      for (const level of pass % 2 === 0 ? levels : [...levels].reverse()) {
        const score = (cluster: FamilyCluster) => {
          const relatives = cluster.people.flatMap(id => [...neighbors.get(id)!])
            .map(id => positions.get(id)!.z);
          return relatives.length > 0 ? average(relatives) : cluster.z;
        };
        const scores = new Map(layers.get(level)!.map(cluster => [cluster.id, score(cluster)]));
        layers.get(level)!.sort((a, b) => {
          const difference = scores.get(a.id)! - scores.get(b.id)!;
          if (difference !== 0) return difference;
          const originA = clusterOrder.get(a.id);
          const originB = clusterOrder.get(b.id);
          if (originA && originB)
            return originA.family.localeCompare(originB.family) || originA.index - originB.index || a.id.localeCompare(b.id);
          return a.id.localeCompare(b.id);
        });
        placeLayer(level);
      }
    }
    const componentClusters = levels.flatMap(level => layers.get(level)!);
    const minZ = Math.min(...componentClusters.map(cluster => cluster.z - cluster.depth / 2));
    const maxZ = Math.max(...componentClusters.map(cluster => cluster.z + cluster.depth / 2));
    const offset = componentOffset - minZ;
    for (const cluster of componentClusters) {
      cluster.z += offset;
      for (const id of cluster.people) positions.get(id)!.z += offset;
    }
    componentOffset += maxZ - minZ + 8;
    clusters.push(...componentClusters);
  });
  return { rootId, people, positions, clusters, families, warnings };
}

export function neighborhood(layout: TreeLayout, personId: string | null): Neighborhood {
  const roles: Neighborhood['roles'] = new Map();
  const families = new Set<string>();
  if (!personId || !layout.people.has(personId)) return { roles, families };
  const add = (id: string, role: PersonRole) => {
    const set = roles.get(id) ?? new Set<PersonRole>();
    set.add(role);
    roles.set(id, set);
  };
  add(personId, 'self');
  for (const family of layout.families) {
    if (family.partners.includes(personId)) {
      families.add(family.id);
      for (const id of family.partners) if (id !== personId) add(id, 'partner');
      for (const id of family.children) add(id, 'child');
    }
    if (family.children.includes(personId)) {
      families.add(family.id);
      for (const id of family.partners) add(id, 'parent');
    }
  }
  return { roles, families };
}
