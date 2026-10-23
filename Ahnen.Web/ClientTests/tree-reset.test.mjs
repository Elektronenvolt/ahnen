import assert from 'node:assert/strict';
import test, { after } from 'node:test';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { build } from 'esbuild';

const directory = mkdtempSync(join(tmpdir(), 'ahnen-reset-tests-'));
after(() => rmSync(directory, { recursive: true, force: true }));
const output = join(directory, 'tree.mjs');
const mocks = {
  topola: `
    const h = () => globalThis.treeHarness;
    export class AncestorChart {}
    export class DescendantChart {}
    export class HourglassChart {}
    export class RelativesChart {}
    export const ChartColors = {};
    export class JsonDataProvider {}
    export class DetailedRenderer { getCss() { return ''; } }
    export function createChart(options) {
      let data = options.json;
      h().onClick = options.indiCallback;
      return {
        setData(next) { data = next; },
        render(args) {
          h().renders.push({ kind: options.chartType.name, data, args });
          return { size: [1000, 600], animationPromise: h().animation ?? Promise.resolve() };
        }
      };
    }
  `,
  'd3-selection': `
    class Selection {
      append() { return new Selection(); }
      attr() { return this; }
      text() { return this; }
      selectAll() { return this; }
      each() { return this; }
      interrupt() { return this; }
      remove() { return this; }
      on() { return this; }
      transition() { return this; }
      duration() { return this; }
      call(fn, ...args) { fn(this, ...args); return this; }
      node() {
        return {
          getBoundingClientRect: () => ({ width: 800, height: 600 }),
          transform: { baseVal: { consolidate: () => ({ matrix: globalThis.treeHarness.matrix }) } }
        };
      }
    }
    export const select = () => new Selection();
  `,
  'd3-transition': '',
  'd3-zoom': `
    export const zoomIdentity = {
      translate(x, y) { return { scale(k) { return { x, y, k }; } }; }
    };
    export function zoom() {
      const behavior = () => {};
      behavior.scaleExtent = behavior.on = () => behavior;
      behavior.transform = (_, t) => {
        globalThis.treeHarness.transforms.push(t);
        globalThis.treeHarness.matrix = { e: t.x, f: t.y, a: t.k };
      };
      return behavior;
    }
  `,
  './tree-minimap': `
    export class Minimap {
      setModel() {}
      setView() {}
      destroy() {}
    }
  `,
  './tree-overview': `
    export function applyDetailLevel() {}
    export function markSelected() {}
    export function overviewModel() { return { bounds: { x: 0, y: 0, width: 100, height: 100 }, rects: [] }; }
    export function renderOverview(data, root, renderer, horizontal) {
      globalThis.treeHarness.overviews.push({ data, root, horizontal });
      return { size: [2000, 1000], animationPromise: Promise.resolve() };
    }
  `,
  './tree3d': `
    export function clearSelection3d() { globalThis.treeHarness.clears++; }
    export function focus3d(id) { globalThis.treeHarness.focus3d.push(id); }
    export function destroy3d() {}
    export function fit3d() {}
    export function init3d() {}
    export function update3d() {}
    export function zoom3d() {}
  `
};
await build({
  entryPoints: [fileURLToPath(new URL('../ClientSrc/tree.ts', import.meta.url))],
  bundle: true, platform: 'node', format: 'esm', outfile: output,
  plugins: [{
    name: 'browser-boundaries',
    setup(builder) {
      builder.onResolve({ filter: /.*/ }, args =>
        Object.hasOwn(mocks, args.path) ? { path: args.path, namespace: 'mock' } : undefined);
      builder.onLoad({ filter: /.*/, namespace: 'mock' }, args => ({ contents: mocks[args.path], loader: 'js' }));
    }
  }]
});
const tree = await import(pathToFileURL(output).href);
const data = { indis: [{ id: 'root' }, { id: 'child' }, { id: 'unrelated' }], fams: [] };
const preferences = { chartType: 'descendants', horizontal: true, colors: 'generation', showIds: true, showSex: false, depth: 0, viewMode: '2d' };
const settle = () => new Promise(resolve => setImmediate(resolve));

async function initialize() {
  tree.destroy();
  const h = globalThis.treeHarness = {
    renders: [], overviews: [], transforms: [], clears: 0, focus3d: [], calls: [], timers: [],
    matrix: { e: 42, f: -10, a: 0.2 }, saved: null
  };
  globalThis.document = { getElementById: () => ({ innerHTML: '' }) };
  globalThis.localStorage = {
    getItem: () => JSON.stringify(preferences),
    setItem: (_, value) => { h.saved = JSON.parse(value); }
  };
  globalThis.ResizeObserver = class {
    constructor(callback) { h.resize = callback; }
    observe() {}
    disconnect() {}
  };
  globalThis.window = {
    setTimeout(callback) { h.timers.push(callback); return h.timers.length; },
    clearTimeout() {}
  };
  const options = tree.init('chart', data, 'root', {
    async invokeMethodAsync(...args) { h.calls.push(args); }
  }, 'en');
  await settle();
  h.transforms.length = 0;
  h.matrix = { e: 42, f: -10, a: 0.2 };
  return { h, options };
}

test('reset shows all data, preserves zoom/preferences/root, and suppresses resize fitting', async () => {
  const { h, options } = await initialize();
  await tree.resetSelection('root');
  assert.equal(h.overviews.length, 1);
  assert.equal(h.overviews[0].root, 'root');
  assert.deepEqual(h.overviews[0].data.indis.map(person => person.id), ['root', 'child', 'unrelated']);
  assert.equal(h.overviews[0].horizontal, true);
  assert.deepEqual(options, preferences);
  assert.equal(h.saved, null);
  assert.equal(h.clears, 1);
  h.resize();
  h.timers.forEach(callback => callback());
  assert.deepEqual(h.transforms, []);
  assert.deepEqual(h.matrix, { e: 42, f: -10, a: 0.2 });
  await tree.resetSelection('root');
  assert.equal(h.overviews.length, 2);
});

test('overview survives settings, data refresh and view switching; selecting restores preferred chart', async () => {
  const { h, options } = await initialize();
  await tree.resetSelection('root');
  tree.setOptions({ ...options, showSex: true });
  tree.updateData({ ...data, indis: [...data.indis, { id: 'new' }] }, 'root');
  assert.equal(h.overviews.at(-1).data.indis.length, 4);
  tree.setViewMode('3d');
  await tree.resetSelection('root');
  tree.setViewMode('2d');
  await tree.refresh2d();
  await settle();
  assert.deepEqual(h.transforms, []);
  tree.setMain('child');
  await settle();
  assert.equal(h.renders.at(-1).kind, 'DescendantChart');
  assert.equal(h.renders.at(-1).args.startIndi, 'child');
  assert.equal(h.saved.chartType, 'descendants');
  assert.equal(h.matrix.a, 0.2);
});

test('chart clicks distinguish Shift details from normal selection', async () => {
  const { h } = await initialize();
  h.onClick({ id: 'child', modifiers: { shiftKey: true } });
  h.onClick({ id: 'child', modifiers: { shiftKey: false } });
  assert.deepEqual(h.calls, [['OnPersonClicked', 'child', false], ['OnPersonClicked', 'child', true]]);
});

test('reset prevents an older render from re-centering afterward', async () => {
  const { h } = await initialize();
  let finish;
  h.animation = new Promise(resolve => { finish = resolve; });
  tree.setMain('child');
  await tree.resetSelection('root');
  finish();
  await settle();
  h.resize();
  h.timers.forEach(callback => callback());
  assert.deepEqual(h.transforms, []);
});

test('reset in 3D clears selection without camera focus and resets the hidden 2D overview', async () => {
  const { h } = await initialize();
  tree.setViewMode('3d');
  await tree.resetSelection('root');
  assert.equal(h.clears, 1);
  assert.deepEqual(h.focus3d, []);
  assert.equal(h.overviews.length, 0);
  tree.setViewMode('2d');
  await tree.refresh2d();
  assert.equal(h.overviews.at(-1).root, 'root');
  tree.setViewMode('3d');
  tree.setMain('child');
  await settle();
  assert.deepEqual(h.focus3d, ['child']);
  tree.setViewMode('2d');
  await tree.refresh2d();
  assert.equal(h.renders.at(-1).args.startIndi, 'child');
  assert.deepEqual(h.transforms, []);
});
