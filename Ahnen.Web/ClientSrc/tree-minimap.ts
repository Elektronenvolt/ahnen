/** Small map of the whole overview with the visible area; click or drag to move the view. */
export interface MinimapModel {
  /** Layout bounds (x/y may be negative: the drawing starts one margin before the origin). */
  bounds: { x: number; y: number; width: number; height: number };
  rects: { x: number; y: number; width: number; height: number; kind: string }[];
}

export interface MinimapView {
  x: number;
  y: number;
  width: number;
  height: number;
}

const WIDTH = 220;
const HEIGHT = 140;
const NS = 'http://www.w3.org/2000/svg';

export class Minimap {
  private readonly element: HTMLDivElement;
  private readonly svg: SVGSVGElement;
  private readonly frame: SVGRectElement;
  private model: MinimapModel | null = null;
  private scale = 1;
  private offset: [number, number] = [0, 0];

  constructor(host: HTMLElement, private readonly pan: (x: number, y: number) => void, label: string) {
    this.element = document.createElement('div');
    this.element.className = 'overview-minimap';
    this.element.hidden = true;
    this.element.setAttribute('role', 'img');
    this.element.setAttribute('aria-label', label);
    this.svg = document.createElementNS(NS, 'svg');
    this.svg.setAttribute('width', String(WIDTH));
    this.svg.setAttribute('height', String(HEIGHT));
    this.frame = document.createElementNS(NS, 'rect');
    this.frame.setAttribute('class', 'minimap-view');
    this.element.append(this.svg);
    host.append(this.element);

    let dragging = false;
    const move = (event: PointerEvent) => {
      if (!this.model) return;
      const box = this.svg.getBoundingClientRect();
      this.pan(
        (event.clientX - box.left - this.offset[0]) / this.scale + this.model.bounds.x,
        (event.clientY - box.top - this.offset[1]) / this.scale + this.model.bounds.y
      );
    };
    this.svg.addEventListener('pointerdown', event => {
      dragging = true;
      this.svg.setPointerCapture(event.pointerId);
      move(event);
    });
    this.svg.addEventListener('pointermove', event => { if (dragging) move(event); });
    this.svg.addEventListener('pointerup', () => { dragging = false; });
    this.svg.addEventListener('pointercancel', () => { dragging = false; });
  }

  setModel(model: MinimapModel | null): void {
    this.model = model;
    this.element.hidden = !model;
    this.svg.replaceChildren();
    if (!model) return;
    const { bounds } = model;
    this.scale = Math.min(WIDTH / bounds.width, HEIGHT / bounds.height);
    this.offset = [(WIDTH - bounds.width * this.scale) / 2, (HEIGHT - bounds.height * this.scale) / 2];
    for (const rect of model.rects) {
      const element = document.createElementNS(NS, 'rect');
      element.setAttribute('class', `minimap-card ${rect.kind}`);
      element.setAttribute('x', String(this.offset[0] + (rect.x - bounds.x) * this.scale));
      element.setAttribute('y', String(this.offset[1] + (rect.y - bounds.y) * this.scale));
      element.setAttribute('width', String(Math.max(1.5, rect.width * this.scale)));
      element.setAttribute('height', String(Math.max(1.5, rect.height * this.scale)));
      this.svg.append(element);
    }
    this.svg.append(this.frame);
  }

  setView(view: MinimapView | null): void {
    if (!this.model || !view) return;
    const { bounds } = this.model;
    this.frame.setAttribute('x', String(this.offset[0] + (view.x - bounds.x) * this.scale));
    this.frame.setAttribute('y', String(this.offset[1] + (view.y - bounds.y) * this.scale));
    this.frame.setAttribute('width', String(view.width * this.scale));
    this.frame.setAttribute('height', String(view.height * this.scale));
  }

  destroy(): void {
    this.element.remove();
    this.model = null;
  }
}
