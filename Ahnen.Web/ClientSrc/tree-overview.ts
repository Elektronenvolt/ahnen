import { hierarchy, tree } from 'd3-hierarchy';
import {
  ChartUtil, getChartInfo, JsonDataProvider, type ChartInfo, type JsonGedcomData,
  type Renderer, type TreeNode
} from 'topola';
import { createTreeLayout, PERSON_SPACING, TERRACE_SPACING } from './tree3d-layout';

export function overviewNodes(data: JsonGedcomData, rootId: string | null, renderer: Renderer, horizontal: boolean) {
  const layout = createTreeLayout(data, rootId);
  const nodes = [...layout.positions].map(([id, position]) => {
    return Object.assign(tree<TreeNode>()(hierarchy<TreeNode>({
      id, indi: { id }, generation: position.generation
    })), { id });
  });
  renderer.updateNodes(nodes);
  const width = Math.max(0, ...nodes.map(node => node.data.width ?? 0)) + 60;
  const height = Math.max(0, ...nodes.map(node => node.data.height ?? 0)) + 60;
  const positioned = nodes.map(node => {
    const position = layout.positions.get(node.id)!;
    const across = position.z / PERSON_SPACING;
    const down = position.x / TERRACE_SPACING;
    return Object.assign(node, {
      x: (horizontal ? down : across) * width,
      y: (horizontal ? across : down) * height
    });
  });
  return { nodes: positioned, families: layout.families };
}

/** Uses Topola's cards with the shared full-graph layout instead of a person-limited chart. */
export function renderOverview(
  data: JsonGedcomData, rootId: string | null, renderer: Renderer, horizontal: boolean
): ChartInfo {
  const { nodes, families } = overviewNodes(data, rootId, renderer, horizontal);
  const util = new ChartUtil({ data: new JsonDataProvider(data), renderer, svgSelector: '#topolaChart', animate: false });
  const svg = util.getSvgForRendering();
  const group = svg.select('g');
  const positions = new Map(nodes.map(node => [node.id!, node]));
  const links: string[] = [];
  for (const family of families) {
    const partners = family.partners.map(id => positions.get(id)!);
    const children = family.children.map(id => positions.get(id)!);
    const members = [...partners, ...children];
    if (members.length < 2) continue;
    const parents = partners.length ? partners : children;
    const junction = {
      x: parents.reduce((sum, node) => sum + node.x, 0) / parents.length,
      y: parents.reduce((sum, node) => sum + node.y, 0) / parents.length
    };
    if (horizontal) junction.x += (Math.max(...parents.map(node => node.data.width ?? 0)) / 2) + 25;
    else junction.y += (Math.max(...parents.map(node => node.data.height ?? 0)) / 2) + 25;
    for (const node of members)
      links.push(`M ${node.x},${node.y} L ${junction.x},${junction.y}`);
  }
  group.selectAll('path.overview-link').data(links).join('path')
    .lower()
    .attr('class', 'overview-link').attr('d', path => path)
    .attr('fill', 'none').attr('stroke', '#98a2b3');
  const animationPromise = util.renderNodes(nodes, svg);
  const info = getChartInfo(nodes);
  svg.attr('width', info.size[0]).attr('height', info.size[1]);
  util.updateSvgDimensions(info);
  return { ...info, animationPromise };
}
