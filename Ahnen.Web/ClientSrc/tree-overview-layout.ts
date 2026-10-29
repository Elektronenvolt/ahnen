import type { JsonFam, JsonGedcomData, JsonIndi } from 'topola';
import { createTreeLayout, generationIdentifier, type TreeLayout } from './tree3d-layout';

/** Optional fields the server adds to the Topola JSON; Topola itself ignores them. */
export interface OverviewIndi extends JsonIndi {
  birthYear?: number | null;
  deceased?: boolean | null;
}

export interface OverviewFam extends JsonFam {
  status?: string;
  marriageYear?: number | null;
  pedigrees?: Record<string, string> | null;
}

export interface OverviewOptions {
  /** Size of one person card (same for all cards). */
  cardWidth: number;
  cardHeight: number;
  /** Generations run left to right instead of top to bottom. */
  horizontal: boolean;
  /** Preferred width/height ratio of the whole drawing (the viewport's). */
  aspect: number;
}

export interface OverviewCard {
  /** Unique per card; ghost cards get a `~g<n>` suffix. */
  id: string;
  person: string;
  ghost: boolean;
  /** Center of the card. */
  x: number;
  y: number;
  /** Layout level of the lane (see `generationIdentifier`). */
  generation: number;
}

export interface OverviewJunction {
  family: string;
  x: number;
  y: number;
  status: string;
  hasChildren: boolean;
  /** The children continue in another block (this junction then has no links of its own). */
  continued: boolean;
}

export type OverviewLinkKind = 'spouse' | 'drop' | 'bus' | 'child' | 'ghost';

export interface OverviewLink {
  kind: OverviewLinkKind;
  family?: string;
  status?: string;
  adopted?: boolean;
  points: [number, number][];
}

export interface OverviewLane {
  generation: number;
  label: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface OverviewLayout {
  cards: OverviewCard[];
  junctions: OverviewJunction[];
  links: OverviewLink[];
  lanes: OverviewLane[];
  bounds: { x: number; y: number; width: number; height: number };
  warnings: Set<string>;
  /** Shared graph model (generations, neighbourhoods). */
  base: TreeLayout;
}

export const COUPLE_GAP = 30;
export const SIBLING_GAP = 18;
export const BRANCH_GAP = 40;
export const LANE_GAP = 72;
export const GUTTER = 60;
export const BLOCK_GAP = 60;
export const MARGIN = 40;
const ARC_START = 10;
const ARC_STEP = 8;

interface Card {
  id: string;
  person: string;
  ghost: boolean;
  dx: number;
  x: number;
  y: number;
}

interface Marriage {
  family: OverviewFam;
  order: number;
  spouse: Card | null;
  children: Unit[];
  junctionDx: number;
  /** The children are drawn in a block of their own (see `detach`). */
  detached?: boolean;
}

interface Unit {
  row: Card[];
  hostIndex: number;
  marriages: Marriage[];
  lane: number;
  hull: [number, number];
  /** Offset of this unit's host card from the parent's host card (children only). */
  dx: number;
  parent: Unit | null;
}

interface Block {
  root: Unit;
  units: Unit[];
  people: number;
  width: number;
  height: number;
  lanes: number[];
  x: number;
  y: number;
}

function byId<T extends { id: string }>(a: T, b: T): number {
  return a.id.localeCompare(b.id);
}

/** Families in chronological order of marriage, undated last, stable by id. */
function chronological(a: OverviewFam, b: OverviewFam): number {
  const ay = a.marriageYear ?? Number.POSITIVE_INFINITY;
  const by = b.marriageYear ?? Number.POSITIVE_INFINITY;
  return ay === by ? a.id.localeCompare(b.id) : ay - by;
}

export function layoutOverview(
  input: JsonGedcomData, rootId: string | null, options: OverviewOptions
): OverviewLayout {
  const base = createTreeLayout(input, rootId);
  const warnings = new Set<string>([...base.warnings]);
  const people = new Set(base.people.keys());
  const families = ([...input.fams] as OverviewFam[]).sort(byId).map(family => ({
    ...family,
    children: [...new Set((family.children ?? []).filter(id => people.has(id)))]
  }));
  const partnersOf = (family: OverviewFam) =>
    [...new Set([family.husb, family.wife].filter((id): id is string => !!id && people.has(id)))];

  // A person hangs below exactly one parent family: birth before adoption/foster, then input order.
  const origin = new Map<string, string>();
  const originIsBirth = new Map<string, boolean>();
  for (const family of families)
    for (const child of family.children) {
      const birth = (family.pedigrees?.[child] ?? 'BIRTH') === 'BIRTH';
      if (!origin.has(child) || (birth && !originIsBirth.get(child))) {
        origin.set(child, family.id);
        originIsBirth.set(child, birth);
      }
    }
  const familiesOf = new Map<string, OverviewFam[]>();
  for (const family of families)
    for (const id of partnersOf(family)) {
      const list = familiesOf.get(id) ?? [];
      list.push(family);
      familiesOf.set(id, list);
    }
  for (const list of familiesOf.values()) list.sort(chronological);

  const rank = (id: string) => base.positions.get(id)?.generation ?? 0;
  const placed = new Set<string>();
  const claimed = new Set<string>();
  let ghostCount = 0;
  const makeCard = (person: string, ghost: boolean): Card => ({
    id: ghost ? `${person}~g${++ghostCount}` : person, person, ghost, dx: 0, x: 0, y: 0
  });

  function buildUnit(host: string, ghostHost: boolean, parent: Unit | null): Unit {
    if (!ghostHost) placed.add(host);
    const unit: Unit = {
      row: [], hostIndex: 0, marriages: [], lane: 0, hull: [0, 0], dx: 0, parent
    };
    const hostCard = makeCard(host, ghostHost);
    const mine = (familiesOf.get(host) ?? []).filter(family => !claimed.has(family.id));
    const left: Card[] = [];
    const right: Card[] = [];
    const leftCount = Math.floor(mine.length / 2);
    mine.forEach((family, index) => {
      claimed.add(family.id);
      const other = partnersOf(family).find(id => id !== host) ?? null;
      let spouse: Card | null = null;
      if (other) {
        const real = !origin.has(other) && !placed.has(other);
        if (real) placed.add(other);
        spouse = makeCard(other, !real);
        (index < leftCount ? left : right).push(spouse);
      }
      unit.marriages.push({ family, order: index, spouse, children: [], junctionDx: 0 });
    });
    unit.row = [...left, hostCard, ...right];
    unit.hostIndex = left.length;
    for (const marriage of unit.marriages)
      for (const child of marriage.family.children ?? [])
        if (origin.get(child) === marriage.family.id && !placed.has(child))
          marriage.children.push(buildUnit(child, false, unit));
    return unit;
  }

  const hasOriginPartner = (id: string) =>
    (familiesOf.get(id) ?? []).some(family => partnersOf(family).some(other => other !== id && origin.has(other)));
  const roots: Unit[] = [];
  const sortedIds = [...people].sort();
  const startRoot = (id: string, ghostHost = false) => {
    const unit = buildUnit(id, ghostHost, null);
    roots.push(unit);
  };
  for (const id of sortedIds)
    if (!placed.has(id) && !origin.has(id) && !hasOriginPartner(id)) startRoot(id);
  // Families of people placed elsewhere (e.g. an earlier marriage of an in-marrying partner).
  for (const family of families) {
    if (claimed.has(family.id)) continue;
    const [first] = partnersOf(family);
    if (first) startRoot(first, placed.has(first));
  }
  // Whatever is left belongs to a parent/child cycle.
  for (const id of sortedIds)
    if (!placed.has(id)) {
      warnings.add('cycle');
      startRoot(id);
    }

  // ----- measure: horizontal extent of every household and its descendants (across axis) -----
  const horizontal = options.horizontal;
  const across = horizontal ? options.cardHeight : options.cardWidth;
  const depth = horizontal ? options.cardWidth : options.cardHeight;
  const pitch = across + COUPLE_GAP;
  const laneStep = depth + LANE_GAP;

  function measure(unit: Unit): void {
    unit.row.forEach((card, index) => { card.dx = (index - unit.hostIndex) * pitch; });
    let min = unit.row[0].dx - across / 2;
    let max = unit.row[unit.row.length - 1].dx + across / 2;
    for (const marriage of unit.marriages)
      marriage.junctionDx = marriage.spouse ? marriage.spouse.dx / 2 : 0;
    let previousEnd = Number.NEGATIVE_INFINITY;
    const ordered = unit.marriages.filter(m => m.children.length > 0)
      .sort((a, b) => a.junctionDx - b.junctionDx || a.order - b.order);
    for (const marriage of ordered) {
      let cursor = 0;
      const hostOffsets: number[] = [];
      for (const child of marriage.children) {
        measure(child);
        hostOffsets.push(cursor - child.hull[0]);
        cursor += child.hull[1] - child.hull[0] + SIBLING_GAP;
      }
      const end = cursor - SIBLING_GAP;
      const centre = (hostOffsets[0] + hostOffsets[hostOffsets.length - 1]) / 2;
      const start = Math.max(marriage.junctionDx - centre, previousEnd + BRANCH_GAP);
      marriage.children.forEach((child, index) => { child.dx = start + hostOffsets[index]; });
      previousEnd = start + end;
      min = Math.min(min, start);
      max = Math.max(max, start + end);
    }
    unit.hull = [min, max];
  }

  function assignLanes(unit: Unit, lane: number): void {
    unit.lane = lane;
    for (const marriage of unit.marriages)
      for (const child of marriage.children)
        assignLanes(child, Math.max(rank(child.row[child.hostIndex].person), lane + 1));
  }

  function groupWidth(marriage: Marriage): number {
    return marriage.children.reduce((sum, child) => sum + child.hull[1] - child.hull[0], 0)
      + (marriage.children.length - 1) * SIBLING_GAP;
  }

  /** The widest not-yet-detached child group below the root household (the root's own group stays). */
  function widestGroup(root: Unit): { unit: Unit; marriage: Marriage } | null {
    let best: { unit: Unit; marriage: Marriage } | null = null as { unit: Unit; marriage: Marriage } | null;
    let bestWidth = 2 * pitch;
    const visit = (unit: Unit, top: boolean) => {
      for (const marriage of unit.marriages) {
        if (!top && marriage.children.length > 0 && groupWidth(marriage) > bestWidth) {
          best = { unit, marriage };
          bestWidth = groupWidth(marriage);
        }
        for (const child of marriage.children) visit(child, false);
      }
    };
    visit(root, true);
    return best;
  }

  /** Moves a child group into a new household that repeats the parents as ghosts, keeping blocks narrow. */
  function detach(unit: Unit, marriage: Marriage): Unit {
    const host = unit.row[unit.hostIndex];
    const spouse = marriage.spouse ? makeCard(marriage.spouse.person, true) : null;
    const header: Unit = {
      row: [makeCard(host.person, true), ...(spouse ? [spouse] : [])], hostIndex: 0, marriages: [],
      lane: unit.lane, hull: [0, 0], dx: 0, parent: null
    };
    header.marriages.push({ family: marriage.family, order: 0, spouse, children: marriage.children, junctionDx: 0 });
    for (const child of marriage.children) child.parent = header;
    marriage.children = [];
    marriage.detached = true;
    return header;
  }

  const limitFor = (list: Unit[]) => {
    const area = list.reduce((sum, root) => {
      const lanes = new Set<number>();
      const collect = (unit: Unit) => { lanes.add(unit.lane); unit.marriages.forEach(m => m.children.forEach(collect)); };
      collect(root);
      return sum + (root.hull[1] - root.hull[0] + GUTTER + BLOCK_GAP) * (lanes.size * laneStep + BLOCK_GAP);
    }, 0);
    return Math.max(8 * pitch, Math.sqrt(area * (horizontal ? 1 / options.aspect : options.aspect)));
  };
  for (const root of roots) {
    measure(root);
    assignLanes(root, rank(root.row[root.hostIndex].person));
  }
  const limit = limitFor(roots);

  function splitTree(root: Unit, out: Unit[]): void {
    for (;;) {
      measure(root);
      if (root.hull[1] - root.hull[0] + GUTTER <= limit) return;
      const candidate = widestGroup(root);
      if (!candidate) return;
      const header = detach(candidate.unit, candidate.marriage);
      out.push(header);
      splitTree(header, out);
    }
  }

  const trees = roots.map(root => {
    const list = [root];
    splitTree(root, list);
    return list;
  });

  const toBlock = (root: Unit): Block => {
    const units: Unit[] = [];
    const collect = (unit: Unit) => {
      units.push(unit);
      unit.marriages.forEach(m => m.children.forEach(collect));
    };
    collect(root);
    const lanes = [...new Set(units.map(unit => unit.lane))].sort((a, b) => a - b);
    const real = units.reduce((sum, unit) => sum + unit.row.filter(card => !card.ghost).length, 0);
    return {
      root, units, people: real, lanes,
      width: root.hull[1] - root.hull[0] + GUTTER,
      height: lanes.length * laneStep - LANE_GAP,
      x: 0, y: 0
    };
  };

  // The tree containing the root person first, then larger families, then single people.
  const treeBlocks = trees.map(tree => tree.map(toBlock));
  const hasRoot = (list: Block[]) => list.some(block => block.units
    .some(unit => unit.row.some(card => !card.ghost && card.person === rootId)));
  const treeKey = treeBlocks.map(list => ({
    list, root: hasRoot(list), people: list.reduce((sum, block) => sum + block.people, 0),
    id: list[0].root.row[list[0].root.hostIndex].person
  }));
  treeKey.sort((a, b) => Number(b.root) - Number(a.root) || b.people - a.people || a.id.localeCompare(b.id));
  const blocks: Block[] = treeKey.flatMap(tree => tree.list);

  // ----- shelf-pack the blocks towards the requested aspect ratio -----
  const aspect = horizontal ? 1 / options.aspect : options.aspect;
  const area = blocks.reduce((sum, block) => sum + (block.width + BLOCK_GAP) * (block.height + BLOCK_GAP), 0);
  const rowLimit = Math.max(...blocks.map(block => block.width), Math.sqrt(area * aspect));
  let cursorX = 0;
  let cursorY = 0;
  let rowHeight = 0;
  for (const block of blocks) {
    if (cursorX > 0 && cursorX + block.width > rowLimit) {
      cursorY += rowHeight + BLOCK_GAP;
      cursorX = 0;
      rowHeight = 0;
    }
    block.x = cursorX;
    block.y = cursorY;
    cursorX += block.width + BLOCK_GAP;
    rowHeight = Math.max(rowHeight, block.height);
  }

  // ----- absolute positions -----
  const lanes: OverviewLane[] = [];
  const primary = new Map<string, Card>();
  const hostX = new Map<Unit, number>();
  blocks.forEach(block => {
    const laneIndex = new Map(block.lanes.map((lane, index) => [lane, index]));
    block.lanes.forEach((lane, index) => lanes.push({
      generation: lane, label: generationIdentifier(lane),
      x: block.x, y: block.y + index * laneStep - LANE_GAP / 2 + 6, width: block.width, height: laneStep - 12
    }));
    const place = (unit: Unit, x: number) => {
      hostX.set(unit, x);
      const y = block.y + laneIndex.get(unit.lane)! * laneStep + depth / 2;
      for (const card of unit.row) {
        card.x = x + card.dx;
        card.y = y;
        if (!card.ghost) primary.set(card.person, card);
      }
      for (const marriage of unit.marriages)
        for (const child of marriage.children) place(child, x + child.dx);
    };
    place(block.root, block.x + GUTTER - block.root.hull[0]);
  });

  // ----- links and junctions (in layout coordinates: generations along y) -----
  const links: OverviewLink[] = [];
  const junctions: OverviewJunction[] = [];
  const everyUnit = blocks.flatMap(block => block.units);
  for (const unit of everyUnit) {
    const host = unit.row[unit.hostIndex];
    const bottom = host.y + depth / 2;
    for (const marriage of unit.marriages) {
      const family = marriage.family;
      const status = family.status ?? 'NONE';
      let junctionX = host.x;
      let junctionY = bottom;
      if (marriage.spouse) {
        const spouse = marriage.spouse;
        const hostIdx = unit.hostIndex;
        const spouseIdx = unit.row.indexOf(spouse);
        junctionX = (host.x + spouse.x) / 2;
        if (Math.abs(hostIdx - spouseIdx) === 1) {
          junctionY = host.y;
          const sign = Math.sign(spouse.x - host.x);
          links.push({
            kind: 'spouse', family: family.id, status,
            points: [[host.x + sign * across / 2, host.y], [spouse.x - sign * across / 2, spouse.y]]
          });
        } else {
          const level = Math.abs(hostIdx - spouseIdx) - 1;
          junctionY = bottom + ARC_START + ARC_STEP * Math.min(level - 1, 3);
          links.push({
            kind: 'spouse', family: family.id, status,
            points: [[host.x, bottom], [host.x, junctionY], [spouse.x, junctionY], [spouse.x, bottom]]
          });
        }
      }
      junctions.push({
        family: family.id, x: junctionX, y: junctionY, status,
        hasChildren: marriage.children.length > 0 || !!marriage.detached,
        continued: !!marriage.detached
      });
      if (marriage.children.length === 0) continue;
      const busY = bottom + LANE_GAP / 2;
      const childCards = marriage.children.map(child => child.row[child.hostIndex]);
      const xs = [junctionX, ...childCards.map(card => card.x)];
      links.push({ kind: 'drop', family: family.id, status, points: [[junctionX, junctionY], [junctionX, busY]] });
      if (Math.min(...xs) !== Math.max(...xs))
        links.push({ kind: 'bus', family: family.id, status, points: [[Math.min(...xs), busY], [Math.max(...xs), busY]] });
      for (const card of childCards)
        links.push({
          kind: 'child', family: family.id, status,
          adopted: (family.pedigrees?.[card.person] ?? 'BIRTH') !== 'BIRTH',
          points: [[card.x, busY], [card.x, card.y - depth / 2]]
        });
    }
    for (const card of unit.row)
      if (card.ghost) {
        const target = primary.get(card.person);
        if (target) links.push({ kind: 'ghost', points: [[card.x, card.y], [target.x, target.y]] });
      }
  }

  // ----- orientation -----
  const turn = (x: number, y: number): [number, number] => (horizontal ? [y, x] : [x, y]);
  const cards: OverviewCard[] = [];
  for (const unit of everyUnit)
    for (const card of unit.row) {
      const [x, y] = turn(card.x, card.y);
      cards.push({ id: card.id, person: card.person, ghost: card.ghost, x, y, generation: unit.lane });
    }
  for (const link of links) link.points = link.points.map(([x, y]) => turn(x, y));
  for (const junction of junctions) [junction.x, junction.y] = turn(junction.x, junction.y);
  for (const lane of lanes) {
    const [x, y] = turn(lane.x, lane.y);
    const [width, height] = horizontal ? [lane.height, lane.width] : [lane.width, lane.height];
    Object.assign(lane, { x, y, width, height });
  }

  const extent = blocks.length
    ? {
      width: Math.max(...blocks.map(block => block.x + block.width)),
      height: Math.max(...blocks.map(block => block.y + block.height))
    }
    : { width: 0, height: 0 };
  const [boundsWidth, boundsHeight] = horizontal ? [extent.height, extent.width] : [extent.width, extent.height];

  return {
    cards, junctions, links, lanes,
    bounds: { x: -MARGIN, y: -MARGIN, width: boundsWidth + 2 * MARGIN, height: boundsHeight + 2 * MARGIN },
    warnings, base
  };
}
