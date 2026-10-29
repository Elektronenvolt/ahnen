import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import type { JsonGedcomData, JsonIndi } from 'topola';
import {
  createTreeLayout, generationIdentifier, neighborhood, PLATFORM_WIDTH, PERSON_SCALE, PERSON_LABEL,
  type PersonPosition, type TreeLayout, type PersonRole
} from './tree3d-layout';
import type { DotNetRef } from './shared';

interface DisplayLabels {
  unavailable: string;
  canvas: string;
  person: string;
  root: string;
  rootGeneration: string;
  above: string;
  below: string;
  separate: string;
  cycle: string;
  conflictingLevels: string;
  selected: string;
  preview: string;
  parent: string;
  partner: string;
  child: string;
  self: string;
  noRelations: string;
}

interface Init3dOptions {
  containerId: string;
  statusId: string;
  tooltipId: string;
  summaryId: string;
  data: JsonGedcomData;
  rootId: string | null;
  selectedId: string | null;
  callback: DotNetRef;
  labels: DisplayLabels;
}

interface CameraTransition {
  fromPosition: THREE.Vector3;
  toPosition: THREE.Vector3;
  fromTarget: THREE.Vector3;
  toTarget: THREE.Vector3;
  startTime: number;
  duration: number;
}

const FAMILY_COLORS = [0x5b74b8, 0x8c5ab4, 0x3c8a79, 0xc0783f, 0xb74f63];

let container: HTMLElement | null = null;
let status: HTMLElement | null = null;
let tooltip: HTMLElement | null = null;
let summary: HTMLElement | null = null;
let scene: THREE.Scene | null = null;
let camera: THREE.PerspectiveCamera | null = null;
let renderer: THREE.WebGLRenderer | null = null;
let controls: OrbitControls | null = null;
let content: THREE.Group | null = null;
let resizeObserver: ResizeObserver | null = null;
let animationFrame = 0;
let cameraTransition: CameraTransition | null = null;
let data: JsonGedcomData | null = null;
let layout: TreeLayout | null = null;
let rootId: string | null = null;
let selectedId: string | null = null;
let hoveredId: string | null = null;
let dotnet: DotNetRef | null = null;
let labels: DisplayLabels;
let cardTextures: THREE.CanvasTexture[] = [];
let personMeshes = new Map<string, THREE.Mesh[]>();
let focusRings = new Map<string, THREE.Mesh>();
let personGroups = new Map<string, THREE.Group>();
let roleSprites = new Map<string, THREE.Sprite>();
let linkGroups: THREE.Group[] = [];
let hasFitted = false;
let pointerDown: { x: number; y: number } | null = null;
const raycaster = new THREE.Raycaster();
const pointer = new THREE.Vector2();

function personName(person: JsonIndi): string {
  return [person.firstName, person.lastName].filter(Boolean).join(' ') || person.id;
}

function eventDate(event: JsonIndi['birth']): string {
  return event?.date?.text ?? '';
}

function dateYear(date: string): string {
  return date.match(/\d{3,4}/)?.[0] ?? '';
}

function lifeYears(person: JsonIndi): string {
  const birth = dateYear(eventDate(person.birth));
  const death = dateYear(eventDate(person.death));
  if (!birth && !death) return '';
  return `${birth || '?'}–${person.death ? death || '?' : ''}`.replace(/–$/, '');
}

function makePersonLabel(person: JsonIndi): THREE.CanvasTexture {
  const canvas = document.createElement('canvas');
  canvas.width = 768;
  canvas.height = 256;
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Canvas 2D context is unavailable for 3D person labels.');

  ctx.fillStyle = 'rgba(255,255,255,.94)';
  ctx.strokeStyle = 'rgba(51,65,85,.18)';
  ctx.lineWidth = 5;
  ctx.beginPath();
  ctx.roundRect(3, 3, canvas.width - 6, canvas.height - 6, 24);
  ctx.fill();
  ctx.stroke();

  const photoX = 18;
  const photoY = 18;
  const photoSize = 220;
  const name = personName(person);
  const initials = name.split(/\s+/).map(part => part[0]).slice(0, 2).join('').toUpperCase();
  ctx.fillStyle = person.sex === 'F' ? '#bc789a' : person.sex === 'M' ? '#7393bd' : '#7f8997';
  ctx.beginPath();
  ctx.roundRect(photoX, photoY, photoSize, photoSize, 24);
  ctx.fill();
  ctx.fillStyle = '#ffffff';
  ctx.font = 'bold 82px system-ui, sans-serif';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(initials || '?', photoX + photoSize / 2, photoY + photoSize / 2);

  ctx.fillStyle = '#1f2937';
  ctx.textAlign = 'left';
  ctx.font = 'bold 57px system-ui, sans-serif';
  let shownName = name;
  while (ctx.measureText(shownName).width > 480 && shownName.length > 8)
    shownName = `${shownName.slice(0, -4)}…`;
  ctx.fillText(shownName, 266, 99, 480);
  const years = lifeYears(person);
  if (years) {
    ctx.fillStyle = '#5b6472';
    ctx.font = '30px system-ui, sans-serif';
    ctx.fillText(years, 266, 163, 470);
  }
  ctx.fillStyle = '#8993a1';
  ctx.font = '22px system-ui, sans-serif';
  ctx.fillText(person.id === rootId ? labels.root : labels.person, 266, 211, 470);

  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  cardTextures.push(texture);

  const imageUrl = person.images?.[0]?.url;
  if (imageUrl) {
    const image = new Image();
    image.onload = () => {
      if (!cardTextures.includes(texture)) return;
      ctx.save();
      ctx.beginPath();
      ctx.roundRect(photoX, photoY, photoSize, photoSize, 24);
      ctx.clip();
      ctx.drawImage(image, photoX, photoY, photoSize, photoSize);
      ctx.restore();
      texture.needsUpdate = true;
    };
    image.onerror = () => console.warn(`Could not load family-tree photo for ${person.id}: ${imageUrl}`);
    image.src = imageUrl;
  }
  return texture;
}

function textSprite(text: string, color = '#344054', width = 4): THREE.Sprite {
  const canvas = document.createElement('canvas');
  canvas.height = 64;
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Canvas 2D context is unavailable for 3D headings.');
  ctx.font = '600 32px system-ui, sans-serif';
  canvas.width = Math.ceil(ctx.measureText(text).width) + 40;
  ctx.fillStyle = 'rgba(255,255,255,.96)';
  ctx.beginPath();
  ctx.roundRect(2, 2, canvas.width - 4, 60, 12);
  ctx.fill();
  ctx.fillStyle = color;
  ctx.font = '600 32px system-ui, sans-serif';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(text, canvas.width / 2, 32);
  const map = new THREE.CanvasTexture(canvas);
  map.colorSpace = THREE.SRGBColorSpace;
  cardTextures.push(map);
  const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map, depthTest: false, depthWrite: false }));
  sprite.renderOrder = 10;
  sprite.userData.aspect = canvas.width / canvas.height;
  sprite.userData.pixelHeight = 30;
  sprite.userData.requestedVisible = true;
  sprite.scale.set(width, width / sprite.userData.aspect, 1);
  return sprite;
}

function makeLine(points: THREE.Vector3[], color: number, opacity = 0.9): THREE.Line {
  const line = new THREE.Line(
    new THREE.BufferGeometry().setFromPoints(points),
    new THREE.LineBasicMaterial({ color, transparent: opacity < 1, opacity })
  );
  line.renderOrder = 1;
  return line;
}

function disposeContent(): void {
  if (!content) return;
  content.traverse(object => {
    if (object instanceof THREE.Mesh || object instanceof THREE.Line || object instanceof THREE.Sprite) {
      object.geometry.dispose();
      if (Array.isArray(object.material)) object.material.forEach(material => material.dispose());
      else object.material.dispose();
    }
  });
  scene?.remove(content);
  cardTextures.forEach(texture => texture.dispose());
  cardTextures = [];
  personMeshes.clear();
  focusRings.clear();
  personGroups.clear();
  roleSprites.clear();
  linkGroups = [];
  content = null;
}

function addPersonFigure(person: JsonIndi, position: PersonPosition): void {
  if (!content) return;
  const figure = new THREE.Group();
  figure.scale.setScalar(PERSON_SCALE);
  figure.position.set(position.x, position.y + 0.03, position.z);
  figure.userData.personId = person.id;

  const personColor = person.sex === 'F' ? 0xb65a8e : person.sex === 'M' ? 0x5479b7 : 0x788394;
  const base = new THREE.Mesh(
    new THREE.CylinderGeometry(0.38, 0.44, 0.12, 24),
    new THREE.MeshStandardMaterial({ color: personColor, roughness: 0.62 })
  );
  base.position.y = 0.06;
  base.castShadow = true;
  base.userData.personId = person.id;
  figure.add(base);

  const body = new THREE.Mesh(
    new THREE.CapsuleGeometry(0.22, 0.38, 5, 10),
    new THREE.MeshStandardMaterial({ color: personColor, roughness: 0.65 })
  );
  body.position.y = 0.49;
  body.castShadow = true;
  body.userData.personId = person.id;
  figure.add(body);

  const head = new THREE.Mesh(
    new THREE.SphereGeometry(0.2, 18, 14),
    new THREE.MeshStandardMaterial({ color: 0xe5bd91, roughness: 0.78 })
  );
  head.position.y = 0.92;
  head.castShadow = true;
  head.userData.personId = person.id;
  figure.add(head);

  const focusRing = new THREE.Mesh(
    new THREE.TorusGeometry(0.55, 0.045, 8, 36),
    new THREE.MeshBasicMaterial({ color: 0xffbf47, transparent: true, opacity: 0.95 })
  );
  focusRing.rotation.x = -Math.PI / 2;
  focusRing.position.y = 0.035;
  focusRing.visible = person.id === selectedId || person.id === rootId;
  figure.add(focusRing);
  focusRings.set(person.id, focusRing);

  const label = new THREE.Mesh(
    new THREE.PlaneGeometry(PERSON_LABEL.width, PERSON_LABEL.depth),
    new THREE.MeshBasicMaterial({
      map: makePersonLabel(person),
      transparent: true,
      side: THREE.DoubleSide,
      depthWrite: false
    })
  );
  label.rotation.x = -Math.PI / 2;
  label.position.set(PERSON_LABEL.x, 0.015, PERSON_LABEL.z);
  figure.add(label);

  personMeshes.set(person.id, [base, body, head]);
  personGroups.set(person.id, figure);
  content.add(figure);
}

function addFamilyLinks(tree: TreeLayout): void {
  if (!content) return;
  tree.families.forEach((family, familyIndex) => {
    const { partners, children } = family;
    const color = FAMILY_COLORS[familyIndex % FAMILY_COLORS.length];
    if (partners.length === 0) return;
    const parents = partners.map(id => tree.positions.get(id)!);
    const junction = new THREE.Vector3(
      Math.max(...parents.map(pos => pos.x)) + PLATFORM_WIDTH / 2 - 0.45,
      Math.max(...parents.map(pos => pos.y)) + 0.23,
      parents.reduce((sum, pos) => sum + pos.z, 0) / parents.length
    );
    const partnerGroup = new THREE.Group();
    partnerGroup.userData.familyId = family.id;
    partnerGroup.userData.people = partners;
    for (const parent of parents) {
      partnerGroup.add(makeLine([
        new THREE.Vector3(parent.x + 0.5 * PERSON_SCALE, parent.y + 0.23, parent.z),
        new THREE.Vector3(junction.x, parent.y + 0.23, parent.z),
        new THREE.Vector3(junction.x, junction.y, parent.z),
        junction
      ], color, 1));
    }
    const diamond = new THREE.Mesh(
      new THREE.OctahedronGeometry(0.19),
      new THREE.MeshBasicMaterial({ color })
    );
    diamond.position.copy(junction);
    partnerGroup.add(diamond);
    linkGroups.push(partnerGroup);
    content!.add(partnerGroup);

    // Each child has one directed branch from the union, rather than one per parent.
    children.forEach((childId, index) => {
      const child = tree.positions.get(childId)!;
      const childPort = new THREE.Vector3(child.x - 0.65 * PERSON_SCALE, child.y + 0.24, child.z);
      const laneX = junction.x + 0.65 + (index % 4) * 0.12;
      const branch = new THREE.Group();
      branch.userData.familyId = family.id;
      branch.userData.people = [...partners, childId];
      branch.userData.childId = childId;
      branch.add(makeLine([
        junction,
        new THREE.Vector3(laneX, junction.y, junction.z),
        new THREE.Vector3(laneX, junction.y, child.z),
        new THREE.Vector3(laneX, childPort.y, child.z),
        new THREE.Vector3(child.x - PLATFORM_WIDTH / 2 + 0.2, childPort.y, child.z),
        childPort
      ], color, 1));
      const arrow = new THREE.Mesh(
        new THREE.ConeGeometry(0.12, 0.34, 10),
        new THREE.MeshBasicMaterial({ color })
      );
      arrow.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), new THREE.Vector3(1, 0, 0));
      arrow.position.copy(childPort);
      branch.add(arrow);
      linkGroups.push(branch);
      content!.add(branch);
    });
  });
}

function generationColor(generation: number): THREE.Color {
  if (generation === 0) return new THREE.Color('#cfb2ee');
  const hue = generation < 0 ? (0.06 + Math.abs(generation) * 0.045) % 0.3 : (0.53 + generation * 0.035) % 0.72;
  return new THREE.Color().setHSL(hue, 0.62, 0.72);
}

function generationTitle(generation: number, component: number): string {
  const identifier = generationIdentifier(generation);
  if (component !== 0) return `${labels.separate} · ${identifier}`;
  if (generation === 0) return `${identifier} · ${labels.rootGeneration}`;
  const description = (generation < 0 ? labels.above : labels.below).replace('{0}', String(Math.abs(generation)));
  return `${identifier} · ${description}`;
}

function buildTree(): void {
  if (!data || !scene) return;
  disposeContent();
  content = new THREE.Group();
  layout = createTreeLayout(data, rootId);
  rootId = layout.rootId;
  const { positions, clusters } = layout;
  const headings = new Set<string>();

  clusters.forEach(cluster => {
    const platform = new THREE.Mesh(
      new THREE.BoxGeometry(PLATFORM_WIDTH, 0.42, cluster.depth),
      new THREE.MeshStandardMaterial({
        color: generationColor(cluster.generation),
        roughness: 0.82,
        metalness: 0
      })
    );
    platform.position.set(cluster.x, cluster.y - 0.22, cluster.z);
    platform.receiveShadow = true;
    platform.castShadow = true;
    content!.add(platform);

    const edge = new THREE.LineSegments(
      new THREE.EdgesGeometry(new THREE.BoxGeometry(PLATFORM_WIDTH, 0.42, cluster.depth)),
      new THREE.LineBasicMaterial({ color: 0x64748b, transparent: true, opacity: 0.38 })
    );
    edge.position.copy(platform.position);
    content!.add(edge);

    for (const id of cluster.people)
      addPersonFigure(layout!.people.get(id)!, positions.get(id)!);

    const key = `${cluster.component}:${cluster.generation}`;
    if (!headings.has(key)) {
      headings.add(key);
      const heading = textSprite(generationTitle(cluster.generation, cluster.component), '#344054', 5);
      heading.position.set(cluster.x, cluster.y + 1.7, cluster.z - cluster.depth / 2 - 0.4);
      heading.userData.generation = cluster.generation;
      heading.userData.kind = 'heading';
      content!.add(heading);
    }
  });

  addFamilyLinks(layout);
  const gridSize = Math.min(300, Math.max(40, data.indis.length * 2));
  const grid = new THREE.GridHelper(gridSize, gridSize / 2, 0xcbd5e1, 0xe2e8f0);
  grid.position.y = -0.435;
  grid.userData.excludeFromBounds = true;
  content.add(grid);
  scene.add(content);
  if (status) {
    status.textContent = [...layout.warnings].map(warning =>
      warning === 'cycle' ? labels.cycle : labels.conflictingLevels).join(' ');
    status.hidden = layout.warnings.size === 0;
  }
  updateSelection();
}

function setEmphasis(object: THREE.Object3D, emphasized: boolean): void {
  object.traverse(child => {
    if (child instanceof THREE.Mesh || child instanceof THREE.Line) {
      const materials = Array.isArray(child.material) ? child.material : [child.material];
      for (const material of materials) {
        material.userData.originalOpacity ??= material.opacity;
        material.userData.originalTransparent ??= material.transparent;
        material.transparent = emphasized ? material.userData.originalTransparent as boolean : true;
        material.opacity = (material.userData.originalOpacity as number) * (emphasized ? 1 : 0.5);
      }
    }
  });
}

function updateSelection(): void {
  if (!layout) return;
  const activeId = hoveredId ?? selectedId;
  const focus = neighborhood(layout, activeId);
  for (const [id, group] of personGroups) setEmphasis(group, !activeId || focus.roles.has(id));
  for (const [id, ring] of focusRings) {
    ring.visible = id === selectedId || id === rootId || id === hoveredId;
    (ring.material as THREE.MeshBasicMaterial).color.setHex(
      id === selectedId ? 0xffbf47 : id === hoveredId ? 0xffffff : 0x7a9bd0
    );
  }
  for (const [id, sprite] of roleSprites) {
    sprite.userData.requestedVisible = focus.roles.has(id);
    sprite.visible = focus.roles.has(id);
  }
  for (const [id, roles] of focus.roles) {
    const text = [...roles].map(role => labels[role]).join(' / ');
    let sprite = roleSprites.get(id);
    if (sprite?.userData.text !== text) {
      if (sprite) {
        const map = sprite.material.map;
        if (map) {
          map.dispose();
          cardTextures = cardTextures.filter(texture => texture !== map);
        }
        sprite.material.dispose();
        sprite.removeFromParent();
      }
      sprite = textSprite(text, '#1e3a5f', 2.4);
      sprite.scale.divideScalar(PERSON_SCALE);
      sprite.position.set(0, 1.4, 0);
      sprite.userData.text = text;
      sprite.userData.kind = 'role';
      personGroups.get(id)!.add(sprite);
      roleSprites.set(id, sprite);
    }
    sprite.visible = true;
    sprite.userData.requestedVisible = true;
    sprite.userData.active = id === activeId;
  }
  for (const group of linkGroups) {
    const childId = group.userData.childId as string | undefined;
    const members = group.userData.people as string[];
    const emphasized = !activeId || (
      focus.families.has(group.userData.familyId as string) &&
      (!childId || childId === activeId || members.slice(0, -1).includes(activeId))
    );
    setEmphasis(group, emphasized);
  }
  if (summary) {
    summary.replaceChildren();
    summary.hidden = !activeId;
    const person = activeId ? layout.people.get(activeId) : null;
    if (person) {
      const heading = document.createElement('strong');
      heading.textContent = `${hoveredId ? labels.preview : labels.selected}: ${personName(person)}`;
      summary.append(heading);
      let relationships = 0;
      for (const role of ['parent', 'partner', 'child'] satisfies PersonRole[]) {
        const names = [...focus.roles].filter(([, roles]) => roles.has(role))
          .map(([id]) => personName(layout!.people.get(id)!));
        if (!names.length) continue;
        relationships += names.length;
        const row = document.createElement('div');
        row.textContent = `${labels[role]}: ${names.join(', ')}`;
        row.title = row.textContent;
        summary.append(row);
      }
      if (!relationships) summary.append(document.createTextNode(labels.noRelations));
    }
  }
}

function fitTree(): void {
  if (!content || !camera || !controls || !container) return;
  const { width, height } = container.getBoundingClientRect();
  if (!width || !height) return;
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
  const bounds = new THREE.Box3();
  content.traverse(object => {
    if (!object.userData.excludeFromBounds &&
        (object instanceof THREE.Mesh || object instanceof THREE.Line || object instanceof THREE.Sprite))
      bounds.expandByObject(object);
  });
  if (bounds.isEmpty()) return;
  const center = bounds.getCenter(new THREE.Vector3());
  const radius = Math.max(bounds.getSize(new THREE.Vector3()).length() / 2, 2);
  const verticalFov = THREE.MathUtils.degToRad(camera.fov);
  const horizontalFov = 2 * Math.atan(Math.tan(verticalFov / 2) * camera.aspect);
  const distance = radius / Math.sin(Math.min(verticalFov, horizontalFov) / 2) * 1.08;
  controls.target.copy(center);
  camera.position.copy(center).add(new THREE.Vector3(1, 0.95, 1.25).normalize().multiplyScalar(distance));
  camera.near = Math.max(0.1, distance / 1000);
  camera.far = Math.max(1000, distance * 20);
  camera.updateProjectionMatrix();
  controls.update();
  cameraTransition = null;
  hasFitted = true;
}

function focusPerson(personId: string, animate = true): void {
  const figure = personGroups.get(personId);
  if (!figure || !camera || !controls) return;
  selectedId = personId;
  updateSelection();
  const target = figure.position.clone().add(new THREE.Vector3(0, 0.48 * PERSON_SCALE, 0));
  const offset = camera.position.clone().sub(controls.target);
  const targetPosition = target.clone().add(offset);
  cameraTransition = animate ? {
    fromPosition: camera.position.clone(),
    toPosition: targetPosition,
    fromTarget: controls.target.clone(),
    toTarget: target,
    startTime: performance.now(),
    duration: 550
  } : null;
  if (!animate) {
    controls.target.copy(target);
    camera.position.copy(targetPosition);
    controls.update();
  }
}

function personAtPointer(event: PointerEvent): string | null {
  if (!container || !camera) return null;
  const rect = container.getBoundingClientRect();
  if (!rect.width || !rect.height) return null;
  pointer.set(
    ((event.clientX - rect.left) / rect.width) * 2 - 1,
    -((event.clientY - rect.top) / rect.height) * 2 + 1
  );
  raycaster.setFromCamera(pointer, camera);
  const hits = raycaster.intersectObjects([...personMeshes.values()].flat(), false);
  return (hits[0]?.object.userData.personId as string | undefined) ?? null;
}

function onPointerMove(event: PointerEvent): void {
  if (!container || !tooltip) return;
  const id = personAtPointer(event);
  if (id !== hoveredId) {
    hoveredId = id;
    updateSelection();
  }
  container.style.cursor = id ? 'pointer' : 'grab';
  if (!id || !layout) {
    tooltip.hidden = true;
    return;
  }
  const person = layout?.people.get(id);
  if (!person) return;
  const years = lifeYears(person);
  tooltip.textContent = years ? `${personName(person)} · ${years}` : personName(person);
  const bounds = container.getBoundingClientRect();
  tooltip.style.left = `${Math.max(8, Math.min(event.clientX - bounds.left + 14, bounds.width - tooltip.offsetWidth - 10))}px`;
  tooltip.style.top = `${Math.max(8, event.clientY - bounds.top - 34)}px`;
  tooltip.hidden = false;
}

function onPointerDown(event: PointerEvent): void {
  pointerDown = { x: event.clientX, y: event.clientY };
  cameraTransition = null;
}

function onPointerUp(event: PointerEvent): void {
  if (!pointerDown) return;
  const moved = Math.hypot(event.clientX - pointerDown.x, event.clientY - pointerDown.y);
  pointerDown = null;
  if (moved > 5) return;
  const personId = personAtPointer(event);
  if (!personId) return;
  focusPerson(personId);
  void dotnet?.invokeMethodAsync('OnPersonClicked', personId, true);
}

function onPointerLeave(): void {
  hoveredId = null;
  updateSelection();
  if (tooltip) tooltip.hidden = true;
  if (container) container.style.cursor = 'grab';
}

function showFailure(message: string): void {
  if (!status) return;
  status.textContent = message;
  status.hidden = false;
}

function animateCamera(now: number): void {
  if (!cameraTransition || !camera || !controls) return;
  const transition = cameraTransition;
  const progress = Math.min((now - transition.startTime) / transition.duration, 1);
  const eased = progress * progress * (3 - 2 * progress);
  camera.position.lerpVectors(transition.fromPosition, transition.toPosition, eased);
  controls.target.lerpVectors(transition.fromTarget, transition.toTarget, eased);
  if (progress === 1) cameraTransition = null;
}

function sizeReadableLabels(): void {
  if (!camera || !renderer || !content) return;
  const height = renderer.domElement.clientHeight;
  const width = renderer.domElement.clientWidth;
  if (!height || !width) return;
  const sprites: THREE.Sprite[] = [];
  content.traverse(object => {
    if (object instanceof THREE.Sprite && object.userData.requestedVisible) sprites.push(object);
  });
  const priority = (sprite: THREE.Sprite) => sprite.userData.kind === 'heading' ? 0 : sprite.userData.active ? 1 : 2;
  sprites.sort((a, b) => priority(a) - priority(b));
  const occupied: { left: number; right: number; top: number; bottom: number }[] = [];
  const screenUp = new THREE.Vector3().setFromMatrixColumn(camera.matrixWorld, 1);
  const parentScale = new THREE.Vector3();
  const localShift = new THREE.Vector3();
  for (const object of sprites) {
    object.userData.anchor ??= object.position.clone();
    object.position.copy(object.userData.anchor as THREE.Vector3);
    const worldPosition = object.getWorldPosition(new THREE.Vector3());
    const distance = camera.position.distanceTo(worldPosition);
    const worldPerPixel = 2 * distance * Math.tan(THREE.MathUtils.degToRad(camera.fov / 2)) / height;
    const aspect = object.userData.aspect as number;
    const pixelHeight = Math.min(object.userData.pixelHeight as number, Math.max(100, width - 24) / aspect);
    const pixelWidth = pixelHeight * aspect;
    object.parent!.getWorldScale(parentScale);
    object.scale.set(
      worldPerPixel * pixelWidth / parentScale.x,
      worldPerPixel * pixelHeight / parentScale.y,
      1 / parentScale.z
    );
    const screenPosition = worldPosition.clone().project(camera);
    const x = (screenPosition.x + 1) * width / 2;
    const y = (1 - screenPosition.y) * height / 2;
    object.visible = false;
    // Keep headings readable; hide colliding role badges at overview scale (the summary retains all roles).
    const shifts = object.userData.kind === 'heading' ? [0, 36, -36, 72, -72, 108, -108] : [0, 32];
    for (const shift of shifts) {
      const rectangle = {
        left: x - pixelWidth / 2 - 3, right: x + pixelWidth / 2 + 3,
        top: y - pixelHeight / 2 - shift - 3, bottom: y + pixelHeight / 2 - shift + 3
      };
      if (rectangle.left < 0 || rectangle.right > width || rectangle.top < 0 || rectangle.bottom > height)
        continue;
      const overlaps = occupied.some(other =>
        rectangle.left < other.right && rectangle.right > other.left &&
        rectangle.top < other.bottom && rectangle.bottom > other.top);
      if (overlaps) continue;
      occupied.push(rectangle);
      localShift.copy(screenUp).multiplyScalar(shift * worldPerPixel);
      object.parent!.worldToLocal(localShift.add(worldPosition));
      object.position.copy(localShift);
      object.visible = true;
      break;
    }
  }
}

export function init3d(options: Init3dOptions): void {
  destroy3d();
  container = document.getElementById(options.containerId);
  status = document.getElementById(options.statusId);
  tooltip = document.getElementById(options.tooltipId);
  summary = document.getElementById(options.summaryId);
  if (!container) throw new Error(`3D family-tree container '${options.containerId}' was not found.`);
  labels = options.labels;
  data = options.data;
  rootId = options.rootId;
  selectedId = options.selectedId;
  dotnet = options.callback;
  if (status) status.hidden = true;

  try {
    scene = new THREE.Scene();
    scene.background = new THREE.Color('#f1f5f9');
    camera = new THREE.PerspectiveCamera(42, 1, 0.1, 2000);
    renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.domElement.setAttribute('aria-label', labels.canvas);
    container.replaceChildren(renderer.domElement);
    scene.add(new THREE.HemisphereLight(0xffffff, 0x9aa8bb, 1));
    const sun = new THREE.DirectionalLight(0xffffff, 1.4);
    sun.position.set(-10, 24, 12);
    sun.castShadow = true;
    sun.shadow.mapSize.set(2048, 2048);
    scene.add(sun);
    controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;
    controls.maxPolarAngle = Math.PI * 0.48;
    controls.minDistance = 4;
    controls.maxDistance = 10000;
    container.addEventListener('pointerdown', onPointerDown);
    container.addEventListener('pointerup', onPointerUp);
    container.addEventListener('pointermove', onPointerMove);
    container.addEventListener('pointerleave', onPointerLeave);
    const resize = () => {
      if (!container || !camera || !renderer) return;
      const { width, height } = container.getBoundingClientRect();
      if (!width || !height) return;
      camera.aspect = width / height;
      camera.updateProjectionMatrix();
      renderer.setSize(width, height);
      if (!hasFitted) fitTree();
    };
    resizeObserver = new ResizeObserver(resize);
    resizeObserver.observe(container);
    resize();
    buildTree();
    fitTree();
    if (selectedId) focusPerson(selectedId, false);

    const animate = (now: number) => {
      if (!renderer || !scene || !camera || !controls) return;
      animationFrame = window.requestAnimationFrame(animate);
      animateCamera(now);
      controls.update();
      sizeReadableLabels();
      renderer.render(scene, camera);
    };
    animate(performance.now());
  } catch (error) {
    console.error('The 3D family-tree view could not be initialized.', error);
    showFailure(labels.unavailable);
    destroy3d();
  }
}

export function update3d(input: JsonGedcomData, preferredRoot: string | null): void {
  if (!renderer || !scene) return;
  const rootChanged = preferredRoot !== rootId;
  data = input;
  rootId = preferredRoot;
  if (selectedId && !input.indis.some(person => person.id === selectedId)) selectedId = null;
  hoveredId = null;
  cameraTransition = null;
  buildTree();
  if (rootChanged) {
    fitTree();
  } else if (selectedId) {
    focusPerson(selectedId, false);
  }
}

export function focus3d(personId: string): void {
  hoveredId = null;
  focusPerson(personId);
}

export function clearSelection3d(): void {
  cameraTransition = null;
  selectedId = null;
  hoveredId = null;
  if (tooltip) tooltip.hidden = true;
  updateSelection();
}

export function zoom3d(direction: number): void {
  if (!camera || !controls) return;
  const offset = camera.position.clone().sub(controls.target);
  offset.multiplyScalar(direction > 0 ? 0.78 : 1.28);
  camera.position.copy(controls.target).add(offset);
  cameraTransition = null;
  controls.update();
}

export function fit3d(): void {
  fitTree();
}

export function destroy3d(): void {
  if (animationFrame) window.cancelAnimationFrame(animationFrame);
  animationFrame = 0;
  resizeObserver?.disconnect();
  resizeObserver = null;
  container?.removeEventListener('pointerdown', onPointerDown);
  container?.removeEventListener('pointerup', onPointerUp);
  container?.removeEventListener('pointermove', onPointerMove);
  container?.removeEventListener('pointerleave', onPointerLeave);
  controls?.dispose();
  if (tooltip) tooltip.hidden = true;
  if (summary) {
    summary.replaceChildren();
    summary.hidden = true;
  }
  disposeContent();
  scene?.traverse(object => {
    if (object instanceof THREE.DirectionalLight) object.shadow.dispose();
  });
  renderer?.dispose();
  renderer?.domElement.remove();
  renderer = null;
  controls = null;
  camera = null;
  scene = null;
  container = null;
  status = null;
  tooltip = null;
  summary = null;
  data = null;
  layout = null;
  rootId = null;
  selectedId = null;
  hoveredId = null;
  dotnet = null;
  pointerDown = null;
  cameraTransition = null;
  hasFitted = false;
}
