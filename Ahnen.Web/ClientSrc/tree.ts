import f3 from 'family-chart';

/** Shape of the person datum provided by the C# ChartDataService. */
interface ChartDatum {
  id: string;
  data: {
    gender: 'M' | 'F';
    label: string;
    dates: string;
    avatar?: string;
    [key: string]: unknown;
  };
  rels: {
    parents: string[];
    spouses: string[];
    children: string[];
  };
}

interface DotNetRef {
  invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

let chart: ReturnType<typeof f3.createChart> | null = null;
let resizeObserver: ResizeObserver | null = null;
let currentContainer: HTMLElement | null = null;

const silhouette = (bg: string, fg: string): string =>
  'data:image/svg+xml;utf8,' +
  encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64" fill="${bg}"/><circle cx="32" cy="24" r="11" fill="${fg}"/><path d="M10 60c2-14 10-20 22-20s20 6 22 20z" fill="${fg}"/></svg>`
  );
const SILHOUETTE_M = silhouette('#dbeafe', '#60a5fa');
const SILHOUETTE_F = silhouette('#fce7f3', '#f472b6');

/**
 * Creates (or re-creates) the family chart inside the given container.
 * Re-renders on container resize via ResizeObserver so the tree always
 * stays fully visible.
 */
export function init(
  containerId: string,
  data: ChartDatum[],
  mainId: string | null,
  dotnetRef: DotNetRef
): void {
  destroy();
  const container = document.getElementById(containerId);
  if (!container) return;
  currentContainer = container;
  container.innerHTML = '';

  chart = f3
    .createChart(container, data as never)
    .setTransitionTime(600)
    .setCardXSpacing(260)
    .setCardYSpacing(180)
    .setOrientationVertical()
    .setSingleParentEmptyCard(false);

  chart
    .setCardHtml()
    .setStyle('imageRect')
    .setCardDisplay([
      (d: { data: ChartDatum['data'] }) => d.data.label,
      (d: { data: ChartDatum['data'] }) => d.data.dates
    ] as never)
    .setCardImageField('avatar')
    .setDefaultPersonIcon(
      ((d: { data: ChartDatum }) =>
        `<img src="${d.data.data.gender === 'F' ? SILHOUETTE_F : SILHOUETTE_M}" alt="" style="width:100%;height:100%;object-fit:cover;"/>`) as never
    )
    .setOnCardClick((_e: Event, d: { data: ChartDatum }) => {
      void dotnetRef.invokeMethodAsync('OnPersonClicked', d.data.id);
    });

  if (mainId) chart.updateMainId(mainId);
  chart.updateTree({ initial: true, tree_position: 'fit' });

  // Re-fit the tree whenever the container changes size (window resize,
  // detail panel opening, split view, …) so nothing is ever cut off.
  let pending: number | null = null;
  resizeObserver = new ResizeObserver(() => {
    if (pending !== null) window.clearTimeout(pending);
    pending = window.setTimeout(() => {
      pending = null;
      if (chart) chart.updateTree({ tree_position: 'fit', transition_time: 300 });
    }, 150);
  });
  resizeObserver.observe(container);
}

/** Replaces the data (after edits) while keeping the current viewport. */
export function updateData(data: ChartDatum[], mainId: string | null): void {
  if (!chart) return;
  chart.updateData(data as never);
  if (mainId) chart.updateMainId(mainId);
  chart.updateTree({ tree_position: 'inherit' });
}

/** Re-roots the tree on the given person. */
export function setMain(personId: string): void {
  if (!chart) return;
  chart.updateMainId(personId);
  chart.updateTree({ tree_position: 'main_to_middle' });
}

export function fit(): void {
  chart?.updateTree({ tree_position: 'fit' });
}

/** Zooms by one step (dir > 0 zooms in) around the viewport center. */
export function zoomBy(dir: number): void {
  if (!currentContainer) return;
  const svg = currentContainer.querySelector('svg.main_svg');
  if (!svg) return;
  const rect = svg.getBoundingClientRect();
  svg.dispatchEvent(
    new WheelEvent('wheel', {
      bubbles: true,
      cancelable: true,
      clientX: rect.left + rect.width / 2,
      clientY: rect.top + rect.height / 2,
      deltaY: dir > 0 ? -240 : 240,
      ctrlKey: true
    })
  );
}

export function destroy(): void {
  resizeObserver?.disconnect();
  resizeObserver = null;
  chart = null;
  if (currentContainer) currentContainer.innerHTML = '';
  currentContainer = null;
}
