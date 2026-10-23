import { hierarchy, tree } from 'd3-hierarchy';
import { select } from 'd3-selection';
import {
  ChartUtil, JsonDataProvider, type ChartInfo, type IndiInfo, type JsonGedcomData,
  type Renderer, type TreeNode
} from 'topola';
import {
  layoutOverview, type OverviewCard, type OverviewIndi, type OverviewLayout, type OverviewLink
} from './tree-overview-layout';
import { neighborhood } from './tree3d-layout';
import type { MinimapModel } from './tree-minimap';

let lastModel: MinimapModel | null = null;

/** Shape of the most recently rendered overview, for the minimap. */
export function overviewModel(): MinimapModel | null {
  return lastModel;
}

/** Below these zoom levels the full cards are replaced by readable chips, then by plain tiles. */
export const CHIP_SCALE = 0.7;
export const TILE_SCALE = 0.28;

export interface OverviewRenderOptions {
  /** Width/height ratio of the viewport, used to pack separate families. */
  aspect?: number;
  selectedId?: string | null;
  scale?: number;
  onClick?: (info: IndiInfo) => void;
}

type OverviewNode = ReturnType<typeof makeNode>;

function makeNode(id: string, person: string, generation: number) {
  return Object.assign(tree<TreeNode>()(hierarchy<TreeNode>({
    id, indi: { id: person }, generation
  })), { id });
}

/** One Topola card per placed person (ghost cards repeat a person next to a partner). */
export function overviewNodes(
  data: JsonGedcomData, rootId: string | null, renderer: Renderer, horizontal: boolean, aspect = 16 / 9
): { nodes: OverviewNode[]; layout: OverviewLayout } {
  const probes = data.indis.map(person => makeNode(person.id, person.id, 0));
  renderer.updateNodes(probes);
  const layout = layoutOverview(data, rootId, {
    cardWidth: Math.max(0, ...probes.map(node => node.data.width ?? 0)),
    cardHeight: Math.max(0, ...probes.map(node => node.data.height ?? 0)),
    horizontal, aspect
  });
  const nodes = layout.cards.map(card =>
    Object.assign(makeNode(card.id, card.person, card.generation), { x: card.x, y: card.y }));
  renderer.updateNodes(nodes);
  return { nodes, layout };
}

function linkPath(link: OverviewLink): string {
  return link.points.map(([x, y], index) => `${index === 0 ? 'M' : 'L'} ${x},${y}`).join(' ');
}

function truncate(text: string, length: number): string {
  return text.length > length ? `${text.slice(0, length - 1)}…` : text;
}

/** Switches between cards, chips and tiles; cheap enough to call on every zoom event. */
export function applyDetailLevel(element: Element | null | undefined, scale: number): void {
  const list = element?.classList;
  if (!list) return;
  list.toggle('lod-chip', scale < CHIP_SCALE && scale >= TILE_SCALE);
  list.toggle('lod-tile', scale < TILE_SCALE);
}

const personOf = (node: OverviewNode) => node.data.indi?.id ?? '';

/** Marks one person (every card and chip showing them) as selected in a rendered overview. */
export function markSelected(svgElement: Element, personId: string | null): void {
  select(svgElement).selectAll<SVGGElement, OverviewNode | OverviewCard>('g.node, g.overview-chip')
    .classed('selected', item => !!personId && ('person' in item ? item.person : personOf(item)) === personId);
}

/** Uses Topola's cards with a family-unit layout: couples side by side, right-angle links to children. */
export function renderOverview(
  data: JsonGedcomData, rootId: string | null, renderer: Renderer, horizontal: boolean,
  extra: OverviewRenderOptions = {}
): ChartInfo {
  const { nodes, layout } = overviewNodes(data, rootId, renderer, horizontal, extra.aspect);
  const util = new ChartUtil({ data: new JsonDataProvider(data), renderer, svgSelector: '#topolaChart', animate: false });
  const svg = util.getSvgForRendering();
  const group = svg.select<SVGGElement>('g');
  group.selectAll('.overview-layer').remove();
  svg.classed('overview-mode', true);
  applyDetailLevel(svg.node() as Element | null, extra.scale ?? 1);

  group.insert('g', ':first-child').attr('class', 'overview-layer overview-links')
    .selectAll('path').data(layout.links).join('path')
    .attr('class', link => `overview-link kind-${link.kind} status-${link.status ?? 'NONE'}${link.adopted ? ' adopted' : ''}`)
    .attr('d', linkPath)
    .attr('fill', 'none');
  group.insert('g', ':first-child').attr('class', 'overview-layer overview-lanes')
    .selectAll('g').data(layout.lanes).join('g')
    .attr('class', 'overview-lane')
    .each(function (lane, index) {
      const element = select(this);
      element.append('rect').attr('class', index % 2 ? 'odd' : 'even')
        .attr('x', lane.x).attr('y', lane.y).attr('width', lane.width).attr('height', lane.height).attr('rx', 10);
      element.append('text').attr('class', 'overview-lane-label')
        .attr('x', lane.x + 12).attr('y', lane.y + 30).text(lane.label);
    });

  const animationPromise = util.renderNodes(nodes, svg);
  const cards = group.selectAll<SVGGElement, OverviewNode>('g.node').classed('ghost', node => node.id.includes('~'));

  group.append('g').attr('class', 'overview-layer overview-junctions')
    .selectAll('circle').data(layout.junctions.filter(junction => junction.hasChildren)).join('circle')
    .attr('class', junction => `overview-junction status-${junction.status}${junction.continued ? ' continued' : ''}`)
    .attr('cx', junction => junction.x).attr('cy', junction => junction.y).attr('r', 4.5);

  const people = new Map(data.indis.map(person => [person.id, person as OverviewIndi]));
  const width = Math.max(0, ...nodes.map(node => node.data.width ?? 0));
  const height = Math.max(0, ...nodes.map(node => node.data.height ?? 0));
  const chips = group.append('g').attr('class', 'overview-layer overview-chips')
    .selectAll<SVGGElement, OverviewCard>('g').data(layout.cards).join('g')
    .attr('class', card => {
      const sex = people.get(card.person)?.sex;
      return `overview-chip ${sex === 'F' ? 'female' : sex === 'M' ? 'male' : 'unknown'}${card.ghost ? ' ghost' : ''}`;
    })
    .attr('transform', card => `translate(${card.x - width / 2},${card.y - height / 2})`);
  chips.append('rect').attr('width', width).attr('height', height).attr('rx', 8);
  chips.each(function (card) {
    const person = people.get(card.person);
    const element = select(this);
    const given = person?.firstName ?? '';
    const surname = person?.lastName ?? '';
    const years = `${person?.birthYear ?? ''}${person?.deceased ? ' †' : ''}`.trim();
    element.append('text').attr('class', 'tile-initial').attr('x', width / 2).attr('y', height * 0.68)
      .text((given || surname || '?').slice(0, 1));
    const nameLine = (css: string, text: string, y: number) => {
      const line = element.append('text').attr('class', css).attr('x', width / 2).attr('y', y).text(truncate(text, 16));
      // Long names are squeezed instead of spilling out of the chip.
      if (Math.min(text.length, 16) * 14 > width - 12) line.attr('textLength', width - 12).attr('lengthAdjust', 'spacingAndGlyphs');
    };
    nameLine('chip-given', given, height * 0.3);
    nameLine('chip-surname', surname, height * 0.56);
    element.append('text').attr('class', 'chip-years').attr('x', width / 2).attr('y', height * 0.84).text(years);
    element.append('title').text([given, surname].filter(Boolean).join(' '));
  });
  chips.on('click', (event: MouseEvent, card) =>
    extra.onClick?.({ id: card.person, generation: card.generation, modifiers: event }));

  // Hover shows a person's parents, partners and children; everything else fades.
  const links = group.selectAll<SVGPathElement, OverviewLink>('path.overview-link');
  const setFocus = (personId: string | null) => {
    const { roles, families } = neighborhood(layout.base, personId);
    const active = personId !== null && roles.size > 0;
    group.classed('focusing', active);
    cards.classed('related', node => active && roles.has(personOf(node)));
    chips.classed('related', card => active && roles.has(card.person));
    links.classed('related', link => active && !!link.family && families.has(link.family));
  };
  cards.on('mouseenter', (_: MouseEvent, node) => setFocus(personOf(node))).on('mouseleave', () => setFocus(null));
  chips.on('mouseenter', (_: MouseEvent, card) => setFocus(card.person)).on('mouseleave', () => setFocus(null));
  cards.classed('selected', node => !!extra.selectedId && personOf(node) === extra.selectedId)
    .classed('root', node => personOf(node) === rootId);
  chips.classed('selected', card => !!extra.selectedId && card.person === extra.selectedId)
    .classed('root', card => card.person === rootId);

  lastModel = {
    bounds: layout.bounds,
    rects: layout.cards.map(card => ({
      x: card.x - width / 2, y: card.y - height / 2, width, height,
      kind: card.ghost ? 'ghost' : people.get(card.person)?.sex === 'F' ? 'female' : people.get(card.person)?.sex === 'M' ? 'male' : 'unknown'
    }))
  };
  const size: [number, number] = [layout.bounds.width, layout.bounds.height];
  const origin: [number, number] = [-layout.bounds.x, -layout.bounds.y];
  svg.attr('width', size[0]).attr('height', size[1]);
  util.updateSvgDimensions({ size, origin });
  return { size, origin, animationPromise } as ChartInfo;
}
