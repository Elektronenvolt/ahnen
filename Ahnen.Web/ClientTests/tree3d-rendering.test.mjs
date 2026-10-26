import assert from 'node:assert/strict';
import test, { after } from 'node:test';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { buildSync } from 'esbuild';

const directory = mkdtempSync(join(tmpdir(), 'ahnen-3d-rendering-tests-'));
after(() => rmSync(directory, { recursive: true, force: true }));
const output = join(directory, 'rendering.mjs');
const source = fileURLToPath(new URL('../ClientSrc/tree3d.ts', import.meta.url));
buildSync({
  stdin: {
    contents: readFileSync(source, 'utf8') + `
      export function setupRenderingTest(width = 800, height = 600) {
        scene = new THREE.Scene();
        camera = new THREE.PerspectiveCamera(42, 800 / 600, 0.1, 2000);
        camera.position.set(15, 20, 25);
        camera.lookAt(0, 0, 0);
        camera.updateMatrixWorld();
        controls = { target: new THREE.Vector3(), update() {} };
        renderer = { domElement: { clientWidth: width, clientHeight: height } };
        container = { getBoundingClientRect: () => ({ left: 0, top: 0, width, height }) };
        labels = {
          root: 'Root', person: 'Person', rootGeneration: 'Root generation',
          above: 'Ancestors {0}', below: 'Descendants {0}', separate: 'Separate',
          self: 'Self', parent: 'Parent', child: 'Child', partner: 'Partner'
        };
        data = {
          indis: [{ id: 'parent', firstName: 'Ada', lastName: 'Miller' }, { id: 'partner' }, { id: 'child' }],
          fams: [{ id: 'family', husb: 'parent', wife: 'partner', children: ['child'] }]
        };
        rootId = 'parent';
        selectedId = 'parent';
        buildTree();
        content.updateMatrixWorld(true);
        return { scene, camera, controls, content, layout, personGroups, roleSprites, focusRings, linkGroups };
      }
      export { sizeReadableLabels, focusPerson, personAtPointer };
    `,
    resolveDir: fileURLToPath(new URL('../ClientSrc/', import.meta.url)),
    loader: 'ts'
  },
  bundle: true, platform: 'node', format: 'esm', outfile: output
});
const rendering = await import(pathToFileURL(output).href);

function withCanvas(run, width = 800, height = 600) {
  const previous = globalThis.document;
  globalThis.document = {
    createElement: () => ({
      width: 0, height: 0,
      drawnText: [],
      getContext() {
        const drawnText = this.drawnText;
        return {
          beginPath() {}, roundRect() {}, fill() {}, stroke() {},
          fillText(text) { drawnText.push({ text, font: this.font }); },
          measureText: text => ({ width: text.length * 16 })
        };
      }
    })
  };
  try {
    return run(rendering.setupRenderingTest(width, height));
  } finally {
    if (previous === undefined) delete globalThis.document;
    else globalThis.document = previous;
  }
}

const close = (actual, expected) => assert(Math.abs(actual - expected) < 1e-6, `${actual} != ${expected}`);

test('3D given name and surname use 50% larger text without enlarging initials or captions', () => {
  withCanvas(({ personGroups }) => {
    const card = personGroups.get('parent').children.find(child => child.geometry?.type === 'PlaneGeometry');
    const drawnText = card.material.map.image.drawnText;
    assert.deepEqual(drawnText.find(item => item.text === 'Ada Miller'),
      { text: 'Ada Miller', font: 'bold 57px system-ui, sans-serif' });
    assert.equal(drawnText.find(item => item.text === 'AM').font, 'bold 82px system-ui, sans-serif');
    assert.equal(drawnText.find(item => item.text === 'Root').font, '22px system-ui, sans-serif');
  });
});

test('person geometry, card text surface, and selection ring are exactly twice their original size', () => {
  withCanvas(({ personGroups, focusRings, content }) => {
    for (const group of personGroups.values()) {
      assert.deepEqual(group.scale.toArray(), [2, 2, 2]);
      const card = group.children.find(child => child.geometry?.type === 'PlaneGeometry');
      close(card.geometry.parameters.width * group.scale.x, 5.8);
      close(card.geometry.parameters.height * group.scale.z, 1.92);
      assert.equal(card.material.map.image.width, 768);
      assert.equal(card.material.map.image.height, 256);
      close(card.position.x * group.scale.x, 0);
      close(card.position.z * group.scale.z, 2.26);
      const head = group.children.find(child => child.geometry?.type === 'SphereGeometry');
      close(head.geometry.parameters.radius * group.scale.x, 0.4);
    }
    const ring = focusRings.get('parent');
    assert.equal(ring.visible, true);
    close(ring.geometry.parameters.radius * ring.parent.scale.x, 1.1);
    assert.deepEqual(content.scale.toArray(), [1, 1, 1]);
    for (const object of content.children.filter(child => !child.userData.personId && !child.isSprite))
      assert.deepEqual(object.scale.toArray(), [1, 1, 1]);
  });
});

test('cards are horizontally centered on individual and shared platforms without moving their rows', () => {
  withCanvas(({ personGroups, layout, content }) => {
    assert(layout.clusters.some(cluster => cluster.people.length > 1));
    for (const cluster of layout.clusters) {
      const platform = content.children.find(object =>
        object.geometry?.type === 'BoxGeometry' &&
        object.position.x === cluster.x && object.position.z === cluster.z);
      assert(platform);
      close(platform.geometry.parameters.width, 8.8);
      for (const id of cluster.people) {
        const group = personGroups.get(id);
        const card = group.children.find(object => object.geometry?.type === 'PlaneGeometry');
        const center = card.getWorldPosition(group.position.clone());
        close(center.x, platform.position.x);
        close(center.z, layout.positions.get(id).z + 2.26);
        close(group.position.x, cluster.x);
      }
    }
  });
});

test('connections and focus target track enlarged figures without enlarging arrows or junctions', () => {
  withCanvas(({ personGroups, linkGroups, layout, controls }) => {
    const parent = layout.positions.get('parent');
    const child = layout.positions.get('child');
    const partnerLine = linkGroups[0].children[0].geometry.attributes.position;
    close(partnerLine.getX(0), parent.x + 1);
    const branch = linkGroups.find(group => group.userData.childId === 'child');
    const arrow = branch.children.find(object => object.geometry.type === 'ConeGeometry');
    close(arrow.position.x, child.x - 1.3);
    close(arrow.geometry.parameters.radius, 0.12);
    const diamond = linkGroups[0].children.find(object => object.geometry.type === 'OctahedronGeometry');
    close(diamond.geometry.parameters.radius, 0.19);
    rendering.focusPerson('parent', false);
    close(controls.target.y, personGroups.get('parent').position.y + 0.96);
  });
});

test('role badges and headings retain a 30-pixel world-space height across frames', () => {
  withCanvas(({ camera, content, roleSprites }) => {
    for (const sprite of roleSprites.values())
      close(sprite.scale.x * sprite.parent.scale.x, 2.4);
    const previousPositions = new Map();
    for (let frame = 0; frame < 2; frame++) {
      content.updateMatrixWorld(true);
      rendering.sizeReadableLabels();
      content.updateMatrixWorld(true);
      const headings = content.children.filter(object => object.userData.kind === 'heading');
      for (const sprite of [...headings, ...roleSprites.values()]) {
        const anchor = sprite.userData.anchor.clone();
        sprite.parent.localToWorld(anchor);
        const distance = camera.position.distanceTo(anchor);
        const worldPerPixel = 2 * distance * Math.tan(camera.fov * Math.PI / 360) / 600;
        const worldScale = sprite.scale.clone();
        sprite.getWorldScale(worldScale);
        close(worldScale.y / worldPerPixel, 30);
        if (sprite.visible) {
          const worldPosition = sprite.position.clone();
          sprite.parent.localToWorld(worldPosition);
          const delta = worldPosition.sub(anchor);
          const screenUp = camera.up.clone().setFromMatrixColumn(camera.matrixWorld, 1);
          const shift = delta.dot(screenUp) / worldPerPixel;
          const allowedShifts = sprite.userData.kind === 'heading'
            ? [0, 36, -36, 72, -72, 108, -108] : [0, 32];
          assert(allowedShifts.some(value => Math.abs(value - shift) < 1e-6));
          close(delta.clone().cross(screenUp).length(), 0);
        }
        if (frame === 0) previousPositions.set(sprite, sprite.position.clone());
        else close(sprite.position.distanceTo(previousPositions.get(sprite)), 0);
      }
    }
  });
});

test('picking still hits the enlarged figure outside its original footprint', () => {
  withCanvas(({ camera, personGroups, content }) => {
    const group = personGroups.get('parent');
    camera.position.copy(group.position).add({ x: 0.75, y: 10, z: 0 });
    camera.lookAt(group.position.clone().add({ x: 0.75, y: 0, z: 0 }));
    camera.updateMatrixWorld();
    content.updateMatrixWorld(true);
    assert.equal(rendering.personAtPointer({ clientX: 400, clientY: 300 }), 'parent');
  });
});

test('fit-to-view contains enlarged figures and platforms on desktop and narrow viewports', () => {
  for (const width of [800, 360]) {
    withCanvas(({ camera, controls, content }) => {
      rendering.fit3d();
      camera.lookAt(controls.target);
      camera.updateMatrixWorld();
      content.updateMatrixWorld(true);
      content.traverse(object => {
        if (!object.geometry || object.isSprite || object.userData.excludeFromBounds) return;
        object.geometry.computeBoundingBox();
        const bounds = object.geometry.boundingBox.clone().applyMatrix4(object.matrixWorld);
        for (const x of [bounds.min.x, bounds.max.x])
          for (const y of [bounds.min.y, bounds.max.y])
            for (const z of [bounds.min.z, bounds.max.z]) {
              const point = camera.position.clone().set(x, y, z).project(camera);
              assert(Math.abs(point.x) <= 1 && Math.abs(point.y) <= 1 && Math.abs(point.z) <= 1);
            }
      });
    }, width);
  }
});
