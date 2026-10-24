import {
  AncestorChart,
  ChartColors,
  DescendantChart,
  DetailedRenderer,
  HourglassChart,
  JsonDataProvider,
  RelativesChart,
  createChart,
  type ChartHandle,
  type IndiInfo,
  type JsonGedcomData
} from 'topola';
import { select, type Selection } from 'd3-selection';
import 'd3-transition';
import { zoom, zoomIdentity, type ZoomBehavior } from 'd3-zoom';
import { clearSelection3d, destroy3d, fit3d, focus3d, init3d, update3d, zoom3d } from './tree3d';
import { applyDetailLevel, markSelected, overviewModel, renderOverview } from './tree-overview';
import { Minimap, type MinimapModel } from './tree-minimap';
import { limitDepth } from './tree-depth';
import type { OverviewFam } from './tree-overview-layout';

export type ChartKind = 'hourglass' | 'relatives' | 'ancestors' | 'descendants';
export type ColorMode = 'none' | 'sex' | 'generation';

export function scrollSearchOption(id: string): void {
  document.getElementById(id)?.scrollIntoView({ block: 'nearest' });
}

export interface TreeOptions {
  chartType: ChartKind;
  colors: ColorMode;
  horizontal: boolean;
  showIds: boolean;
  showSex: boolean;
  /** Generations of ancestors/descendants drawn around the focus person; 0 shows all. */
  depth: number;
  viewMode: '2d' | '3d';
}

interface DotNetRef {
  invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

const STORAGE_KEY = 'ahnen.tree.options';
const DEFAULTS: TreeOptions = {
  chartType: 'hourglass',
  colors: 'sex',
  horizontal: false,
  showIds: false,
  showSex: true,
  depth: 0,
  viewMode: '2d'
};
/** Below this zoom level an automatic fit centers on the focused person instead of shrinking further. */
const MIN_AUTO_SCALE = 0.5;
const MAX_AUTO_SCALE = 1;
const CONTENT_SCALE = 1.2;

const CHART_TYPES = {
  hourglass: HourglassChart,
  relatives: RelativesChart,
  ancestors: AncestorChart,
  descendants: DescendantChart
} as const;
const COLORS = {
  none: ChartColors.NO_COLOR,
  sex: ChartColors.COLOR_BY_SEX,
  generation: ChartColors.COLOR_BY_GENERATION
} as const;

let container: HTMLElement | null = null;
let outerSvg: Selection<SVGSVGElement, unknown, null, undefined> | null = null;
let zoomLayer: Selection<SVGGElement, unknown, null, undefined> | null = null;
let chartSvgEl: SVGSVGElement | null = null;
let zoomBehavior: ZoomBehavior<SVGSVGElement, unknown> | null = null;
let handle: ChartHandle | null = null;
let json: JsonGedcomData | null = null;
let options: TreeOptions = { ...DEFAULTS };
let focusId: string | null = null;
let dotnet: DotNetRef | null = null;
let locale = 'en';
let resizeObserver: ResizeObserver | null = null;
let chartSize: [number, number] = [0, 0];
/** True once the user pans/zooms manually, which suppresses automatic re-fitting on resize. */
let userAdjusted = false;
let renderToken = 0;
let viewMode: '2d' | '3d' = '2d';
let overview = false;
let rootId: string | null = null;
/** Person opened with Shift-click while the overview is shown. */
let overviewSelected: string | null = null;
let minimap: Minimap | null = null;
let minimapModel: MinimapModel | null = null;

function loadOptions(): TreeOptions {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const saved = JSON.parse(raw) as Partial<TreeOptions>;
      return { ...DEFAULTS, ...saved, viewMode: saved.viewMode === '3d' ? '3d' : '2d' };
    }
  } catch {
    /* ignore corrupt or unavailable storage */
  }
  return { ...DEFAULTS };
}

function saveOptions(): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(options));
  } catch {
    /* storage may be unavailable (private mode) */
  }
}

/** Drops null members (the .NET serializer emits them) and applies the id/sex display settings. */
function prepare(data: JsonGedcomData): JsonGedcomData {
  const clean = JSON.parse(JSON.stringify(data)) as JsonGedcomData;
  for (const indi of clean.indis) {
    indi.hideId = !options.showIds;
    indi.hideSex = !options.showSex;
  }
  return clean;
}

function resolveFocus(preferred: string | null): string | null {
  if (!json || json.indis.length === 0) return null;
  const has = (id: string | null) => !!id && json!.indis.some(i => i.id === id);
  if (has(preferred)) return preferred;
  if (has(focusId)) return focusId;
  return json.indis[0].id;
}

function onIndiClick(info: IndiInfo): void {
  const shift = !!info.modifiers?.shiftKey;
  if (overview && shift) {
    overviewSelected = info.id;
    if (chartSvgEl) markSelected(chartSvgEl, info.id);
  }
  void dotnet?.invokeMethodAsync('OnPersonClicked', info.id, !shift);
}

function buildHandle(): void {
  if (!json || !chartSvgEl) return;
  handle = createChart({
    json,
    svgSelector: '#topolaChart',
    chartType: CHART_TYPES[options.chartType],
    renderer: DetailedRenderer,
    horizontal: options.horizontal,
    colors: COLORS[options.colors],
    animate: true,
    expanders: true,
    locale,
    indiCallback: onIndiClick
  });
}

/** Renders the chart; resolves once animations have finished. */
async function render(): Promise<void> {
  if (!handle || !chartSvgEl || !json) return;
  // Text measurements in a hidden SVG are zero; render after 2D is visible.
  if (viewMode === '3d') return;
  focusId = resolveFocus(focusId);
  if (!focusId) return;
  const token = ++renderToken;
  const info = overview
    ? renderOverview(json, rootId, new DetailedRenderer({
      data: new JsonDataProvider(json), colors: COLORS[options.colors],
      horizontal: options.horizontal, locale, animate: false, indiCallback: onIndiClick
    }), options.horizontal, {
      aspect: viewportAspect(), selectedId: overviewSelected, scale: currentTransform().k * CONTENT_SCALE, onClick: onIndiClick
    })
    : renderPersonChart(handle, json, focusId);
  chartSize = [info.size[0] * CONTENT_SCALE, info.size[1] * CONTENT_SCALE];
  await info.animationPromise.catch(() => undefined);
  if (token !== renderToken) return;
  decorateChart();
  syncMinimap();
}

function renderPersonChart(chart: ChartHandle, data: JsonGedcomData, startIndi: string) {
  if (options.depth > 0) chart.setData(limitDepth(data, startIndi, options.depth));
  return chart.render({ startIndi });
}

/** Marks the focus person (in every repeated card) and divorced/separated partnerships. */
function decorateChart(): void {
  if (!chartSvgEl || !json || overview) return;
  const status = new Map((json.fams as OverviewFam[]).map(family => [family.id, family.status]));
  const ended = new Set(['DIV', 'SEP', 'ANUL', 'DIVF']);
  select(chartSvgEl).selectAll<SVGGElement, { indi?: { id: string } }>('g.indi').each(function (d) {
    this.classList?.toggle('focus', !!focusId && d?.indi?.id === focusId);
  });
  select(chartSvgEl).selectAll<SVGGElement, { data?: { id?: string } }>('g.family').each(function (d) {
    this.classList?.toggle('ended', ended.has(status.get(d?.data?.id ?? '') ?? ''));
  });
}

function viewport(): { w: number; h: number } {
  const rect = outerSvg!.node()!.getBoundingClientRect();
  return { w: rect.width, h: rect.height };
}

function viewportAspect(): number {
  const { w, h } = viewport();
  return w > 0 && h > 0 ? w / h : 16 / 9;
}

/** Keeps the minimap in step with the overview and the visible area. */
function syncMinimap(): void {
  if (!container || !outerSvg) return;
  const model = overview && viewMode === '2d' ? overviewModel() : null;
  if (!model) {
    minimap?.setModel(null);
    minimapModel = null;
    return;
  }
  minimap ??= new Minimap(container, panToLayoutPoint, locale === 'de' ? 'Übersichtskarte' : 'Overview map');
  if (minimapModel !== model) {
    minimap.setModel(model);
    minimapModel = model;
  }
  const { w, h } = viewport();
  const t = currentTransform();
  const k = t.k * CONTENT_SCALE;
  minimap.setView({
    x: -t.x / k + model.bounds.x, y: -t.y / k + model.bounds.y, width: w / k, height: h / k
  });
}

function panToLayoutPoint(x: number, y: number): void {
  const model = overviewModel();
  if (!model) return;
  const { w, h } = viewport();
  const t = currentTransform();
  userAdjusted = true;
  transitionTo(w / 2 - (x - model.bounds.x) * t.k * CONTENT_SCALE,
    h / 2 - (y - model.bounds.y) * t.k * CONTENT_SCALE, t.k, 0);
}

function transitionTo(x: number, y: number, k: number, ms: number): void {
  if (!outerSvg || !zoomBehavior) return;
  const t = zoomIdentity.translate(x, y).scale(k);
  if (ms > 0) outerSvg.transition().duration(ms).call(zoomBehavior.transform, t);
  else outerSvg.call(zoomBehavior.transform, t);
}

/** Scales the whole chart into the viewport (never beyond 100%). */
function fitAll(ms = 400): void {
  const { w, h } = viewport();
  const [cw, ch] = chartSize;
  if (!w || !h || !cw || !ch) return;
  const k = Math.min(w / cw, h / ch, MAX_AUTO_SCALE);
  transitionTo((w - cw * k) / 2, (h - ch * k) / 2, k, ms);
}

function currentTransform(): { x: number; y: number; k: number } {
  const m = zoomLayer?.node()?.transform.baseVal.consolidate()?.matrix;
  return m ? { x: m.e, y: m.f, k: m.a } : { x: 0, y: 0, k: 1 };
}

/** Centers a person's box in the viewport at the given scale. */
function centerOn(id: string, k: number, ms = 400): boolean {
  if (!outerSvg || !chartSvgEl) return false;
  let target: SVGGElement | null = null;
  select(chartSvgEl)
    .selectAll<SVGGElement, { indi?: { id: string } }>('g.indi')
    .each(function (d) {
      if (d?.indi?.id === id) target = this;
    });
  const el = target as SVGGElement | null;
  if (!el) return false;
  const box = el.getBoundingClientRect();
  const svgBox = outerSvg.node()!.getBoundingClientRect();
  const t = currentTransform();
  // Person center in chart (pre-zoom) coordinates.
  const cx = (box.left + box.width / 2 - svgBox.left - t.x) / t.k;
  const cy = (box.top + box.height / 2 - svgBox.top - t.y) / t.k;
  const { w, h } = viewport();
  transitionTo(w / 2 - cx * k, h / 2 - cy * k, k, ms);
  return true;
}

/** Initial/automatic view: fit everything, or center on the focus person if that would be too small. */
function autoView(ms = 400): void {
  userAdjusted = false;
  const { w, h } = viewport();
  const [cw, ch] = chartSize;
  if (!w || !h || !cw || !ch) return;
  const fitK = Math.min(w / cw, h / ch, MAX_AUTO_SCALE);
  if (fitK >= MIN_AUTO_SCALE || !focusId) {
    fitAll(ms);
    return;
  }
  if (!centerOn(focusId, MIN_AUTO_SCALE, ms)) fitAll(ms);
}

function focus(personId: string): void {
  if (!json || !handle) return;
  const wasOverview = overview;
  if (overview) {
    overview = false;
    overviewSelected = null;
    clearChart();
    buildHandle();
  }
  focusId = resolveFocus(personId);
  const pending = render();
  const token = renderToken;
  void pending.then(() => {
    if (token !== renderToken || viewMode !== '2d') return;
    userAdjusted = wasOverview;
    if (focusId) centerOn(focusId, wasOverview ? currentTransform().k : Math.max(currentTransform().k, MIN_AUTO_SCALE));
  });
}

/**
 * Creates (or re-creates) the chart inside the given container and returns the effective
 * (persisted) display options so the Blazor UI can reflect them.
 */
export function init(
  containerId: string,
  data: JsonGedcomData,
  mainId: string | null,
  dotnetRef: DotNetRef,
  uiLocale: string
): TreeOptions {
  destroy();
  const el = document.getElementById(containerId);
  if (!el) return options;
  container = el;
  dotnet = dotnetRef;
  locale = uiLocale;
  options = loadOptions();
  viewMode = options.viewMode;
  json = prepare(data);
  rootId = mainId;
  focusId = resolveFocus(mainId);

  el.innerHTML = '';
  outerSvg = select(el).append('svg').attr('class', 'topola-svg') as Selection<SVGSVGElement, unknown, null, undefined>;
  outerSvg.append('style').text(new DetailedRenderer({ data: new JsonDataProvider(json) }).getCss());
  zoomLayer = outerSvg.append('g') as Selection<SVGGElement, unknown, null, undefined>;
  const contentLayer = zoomLayer.append('g').attr('class', 'chart-content').attr('transform', `scale(${CONTENT_SCALE})`);
  const inner = contentLayer.append('svg').attr('id', 'topolaChart').attr('overflow', 'visible');
  chartSvgEl = inner.node() as SVGSVGElement;

  zoomBehavior = zoom<SVGSVGElement, unknown>()
    .scaleExtent([0.05, 3])
    .on('zoom', ev => {
      zoomLayer!.attr('transform', ev.transform.toString());
      if (overview) {
        applyDetailLevel(chartSvgEl, ev.transform.k * CONTENT_SCALE);
        syncMinimap();
      }
      if (ev.sourceEvent) userAdjusted = true;
    });
  outerSvg.call(zoomBehavior).on('dblclick.zoom', null);

  buildHandle();
  const pendingRender = render();
  const initialToken = renderToken;
  void pendingRender.then(() => {
    if (initialToken === renderToken) autoView(0);
  });

  // Keep the tree fully visible whenever the container changes size (window resize, detail panel, …).
  let pending: number | null = null;
  resizeObserver = new ResizeObserver(() => {
    if (pending !== null) window.clearTimeout(pending);
    pending = window.setTimeout(() => {
      pending = null;
      if (!userAdjusted) autoView(300);
    }, 150);
  });
  resizeObserver.observe(el);
  return options;
}

/** Replaces the data (after edits) while keeping the current viewport. */
export function updateData(data: JsonGedcomData, mainId: string | null): void {
  if (!handle || data.indis.length === 0) return;
  json = prepare(data);
  rootId = mainId;
  handle.setData(json);
  focusId = resolveFocus(focusId ?? mainId);
  void render();
}

/** Applies new display options, persists them and re-renders. */
export function setOptions(next: TreeOptions): TreeOptions {
  options = { ...DEFAULTS, ...next };
  saveOptions();
  if (!json || !chartSvgEl) return options;
  json = prepare(json);
  // Topola only styles boxes on creation, so start from a clean canvas to apply colour/orientation changes.
  clearChart();
  buildHandle();
  const pending = render();
  const token = renderToken;
  void pending.then(() => {
    if (token === renderToken && !overview && viewMode === '2d') autoView(0);
  });
  return options;
}

/** Re-roots the tree on the given person. */
export function setMain(personId: string): void {
  focus(personId);
  if (viewMode === '3d') focus3d(personId);
}

function clearChart(): void {
  if (!chartSvgEl) return;
  chartSvgEl.classList?.remove('overview-mode', 'lod-chip', 'lod-tile');
  minimap?.setModel(null);
  minimapModel = null;
  select(chartSvgEl).selectAll('*').interrupt().remove();
}

/** Clears the person filter without changing preferences, root, or the zoom transform. */
export async function resetSelection(preferredRoot: string | null): Promise<void> {
  overview = true;
  overviewSelected = null;
  rootId = preferredRoot;
  userAdjusted = true;
  renderToken++;
  outerSvg?.interrupt();
  clearSelection3d();
  clearChart();
  buildHandle();
  await render();
}

export function fit(): void {
  userAdjusted = false;
  fitAll();
}

/** Zooms by one step (dir > 0 zooms in) around the viewport center. */
export function zoomBy(dir: number, mode = viewMode): void {
  if (mode === '3d') {
    zoom3d(dir);
    return;
  }
  if (!outerSvg || !zoomBehavior) return;
  userAdjusted = true;
  outerSvg.transition().duration(200).call(zoomBehavior.scaleBy, dir > 0 ? 1.3 : 1 / 1.3);
}

export function setViewMode(mode: '2d' | '3d'): void {
  viewMode = mode;
  options.viewMode = mode;
  saveOptions();
  syncMinimap();
}

export async function refresh2d(): Promise<void> {
  await render();
}

export { clearSelection3d, destroy3d, fit3d, focus3d, init3d, update3d };

export function destroy(): void {
  destroy3d();
  minimap?.destroy();
  minimap = null;
  minimapModel = null;
  resizeObserver?.disconnect();
  resizeObserver = null;
  renderToken++;
  outerSvg?.interrupt();
  if (container) container.innerHTML = '';
  container = null;
  outerSvg = null;
  zoomLayer = null;
  chartSvgEl = null;
  zoomBehavior = null;
  handle = null;
  json = null;
  overview = false;
  overviewSelected = null;
  rootId = null;
  focusId = null;
  dotnet = null;
  viewMode = '2d';
}
