import assert from 'node:assert/strict';
import { existsSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { importPropCatalogBindings } from '../src/shared/prop-catalog-import';
import { DEFAULT_WALL_SETTINGS, placeWallMounts, wallMountIssues } from '../src/shared/wall-mounts';
import type { AssetBinding, GridMap, MapFixture, PrefabAsset, Vec3, WallMountSettings } from '../src/shared/types';

const projectRoot = process.argv[3] ?? 'C:/Users/Yoon Youngseok/Documents/GitHub/Four-Tiger-Sword/Four-Tiger-Sword';
const catalogPath = process.argv[2] ?? path.join(projectRoot, 'BaekhoPipeCable2xRegistration/PipeCableKit2x-wall-catalog.json');
assert.ok(existsSync(catalogPath), `Exported catalog must exist before validation: ${catalogPath}`);
const raw = JSON.parse(readFileSync(catalogPath, 'utf8'));
const wanted = new Set(raw.assets.map((entry: { guid: string }) => entry.guid.toLowerCase()));
const assets: PrefabAsset[] = [];
function scan(relative: string): void {
  const directory = path.join(projectRoot, relative);
  if (!existsSync(directory)) return;
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const child = `${relative}/${entry.name}`;
    if (entry.isDirectory()) scan(child);
    else if (entry.name.endsWith('.prefab')) {
      const guid = readFileSync(path.join(projectRoot, `${child}.meta`), 'utf8').match(/^guid: ([a-f0-9]{32})$/m)?.[1];
      if (guid && wanted.has(guid.toLowerCase())) assets.push({ guid, name: entry.name.replace(/\.prefab$/, ''), path: child } as PrefabAsset);
    }
  }
}
for (const folder of ['Assets/PipeCableKit2x/Prefabs', 'Assets/PipeCableTeeKit2x/Prefabs', 'Assets/Baekho']) scan(folder);
assert.equal(new Set(assets.map(asset => asset.guid)).size, 40, 'All 40 catalog identities resolve to actual prefabs');
const imported = importPropCatalogBindings([], raw, assets, 'decoration');
assert.equal(imported.added, 40, '24 original parts, 8 supporting end caps and 8 native 2m straights import');
assert.equal(imported.unmatched, 0);
const families = [...new Set(imported.bindings.map(binding => binding.wallMount!.family))].sort();
assert.equal(families.length, 8);
const near = (a: Vec3, b: Vec3) => Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z) < 1e-5;
function room(cellSize: number): GridMap {
  return { version: 5, name: 'PipeCableKit2x read-only validation', width: 12, height: 10, cellSize, maxStepHeight: .4,
    rows: ['############', ...Array(8).fill('#..........#'), '############'],
    elevations: Array.from({ length: 10 }, () => Array(12).fill(0)), ramps: [], connections: [], assetOverrides: [], headroom: 2 };
}
function verifyGraph(map: GridMap, bindings: AssetBinding[]): void {
  assert.deepEqual(wallMountIssues(map, bindings), [], 'Final graph has no geometry, occupancy or metadata issues');
  const fixtures = map.fixtures ?? [];
  assert.ok(fixtures.length > 0, 'Actual prefab assembly was placed');
  for (const fixture of fixtures) {
    assert.ok(fixture.wall?.runId && !fixture.wall.placeholder, 'Every fixture is an actual prefab in a connected run');
    for (const socket of fixture.wall.sockets) {
      const peers = fixtures.flatMap(other => other === fixture ? [] : other.wall!.sockets
        .filter(peer => near(socket.position, peer.position) && near(socket.direction, { x: -peer.direction.x, y: -peer.direction.y, z: -peer.direction.z })).map(() => other));
      assert.equal(peers.length, 1, `${fixture.id} has exactly one peer per Socket`);
      assert.equal(peers[0].wall!.runId, fixture.wall.runId, 'Socket peers share run identity');
    }
  }
}
const scenarios: { name: string; cellSize: number; orientation: WallMountSettings['runOrientation']; omit?: string; requireKind?: string }[] = [
  { name: 'horizontal-1m-grid', cellSize: 1, orientation: 'horizontal' },
  { name: 'vertical-1m-grid', cellSize: 1, orientation: 'vertical' },
  { name: 'horizontal-2m-grid', cellSize: 2, orientation: 'horizontal' },
  { name: 'vertical-2m-grid', cellSize: 2, orientation: 'vertical' },
  { name: 'mixed-complete-kit', cellSize: 2, orientation: 'both' },
  { name: 'mixed-elbow', cellSize: 2, orientation: 'both', omit: 'tee', requireKind: 'elbow' },
  { name: 'mixed-tee', cellSize: 2, orientation: 'both', omit: 'elbow', requireKind: 'tee' },
];
const reports: unknown[] = [], failures: unknown[] = [];
function manualTeeGraph(bindings: AssetBinding[]): GridMap {
  const map = room(2), zero = { x: 0, y: 0, z: 0 };
  const tee = bindings.find(binding => binding.wallMount!.kind === 'tee')!;
  const straight = bindings.find(binding => binding.wallMount!.kind === 'straight' && binding.prefabPath.endsWith('_Straight_1m.prefab'))!;
  const cap = bindings.find(binding => binding.wallMount!.kind === 'endCap')!;
  const railZ = -8 - tee.wallMount!.mounts[0].position.z;
  const transform = (v: Vec3, yaw: number, roll: number): Vec3 => {
    const rz = roll * Math.PI / 180, ry = yaw * Math.PI / 180;
    const x = v.x * Math.cos(rz) - v.y * Math.sin(rz), y = v.x * Math.sin(rz) + v.y * Math.cos(rz);
    return { x: x * Math.cos(ry) + v.z * Math.sin(ry), y, z: -x * Math.sin(ry) + v.z * Math.cos(ry) };
  };
  const part = (binding: AssetBinding, id: string, x: number, y: number, yaw = 0, roll = 0): MapFixture => {
    const position = { x, y, z: railZ };
    const moved = (v: Vec3) => { const out = transform(v, yaw, roll); return { x: position.x + out.x, y: position.y + out.y, z: position.z + out.z }; };
    const bounds = binding.wallMount!.collisionBoxes.map(box => {
      const rz = roll * Math.PI / 180, ry = yaw * Math.PI / 180;
      const rolled = { x: box.size.x * Math.abs(Math.cos(rz)) + box.size.y * Math.abs(Math.sin(rz)), y: box.size.y * Math.abs(Math.cos(rz)) + box.size.x * Math.abs(Math.sin(rz)), z: box.size.z };
      return { center: moved(box.center), size: { x: rolled.x * Math.abs(Math.cos(ry)) + rolled.z * Math.abs(Math.sin(ry)), y: rolled.y, z: rolled.z * Math.abs(Math.cos(ry)) + rolled.x * Math.abs(Math.sin(ry)) } };
    });
    const support = Array.from({ length: 10 }, (_, i) => i + 1).filter(column => bounds.some(box => box.center.x - box.size.x / 2 < (column - 5.5) * 2 + 1 - 1e-5 && box.center.x + box.size.x / 2 > (column - 5.5) * 2 - 1 + 1e-5)).map(column => ({ x: column, row: 9, direction: 0 as const }));
    return { id, role: 'decoration', x: support[0].x, row: 8, direction: yaw as 0 | 180, offset: { ...zero }, bindingId: binding.id, anchor: 'wall',
      wall: { position, rotationY: yaw, ...(roll ? { rotationZ: roll } : {}), support, bounds,
        sockets: binding.wallMount!.sockets.map(socket => ({ position: moved(socket.position), direction: transform(socket.direction, yaw, roll) })), site: 'wall', runId: 'manual-native-tee', authored: true,
        bindingReference: { scale: { ...binding.scale }, rotationY: binding.rotationY, offset: { ...binding.offset }, prefabGuid: binding.prefabGuid, prefabPath: binding.prefabPath } } };
  };
  map.fixtures = [part(tee, 'manual-tee', 0, 3.65, 0, 180), part(straight, 'manual-left', -2, 3.65), part(straight, 'manual-right', 1, 3.65),
    part(straight, 'manual-down', 0, 1.65, 0, 90), part(cap, 'manual-cap-left', -2, 3.65, 180), part(cap, 'manual-cap-right', 2, 3.65), part(cap, 'manual-cap-bottom', 0, 1.65, 0, 270)];
  return map;
}
for (const family of families) {
  const familyBindings = imported.bindings.filter(binding => binding.wallMount!.family === family);
  assert.deepEqual(familyBindings.map(binding => binding.wallMount!.kind).sort(), ['elbow', 'endCap', 'straight', 'straight', 'tee']);
  try {
    const assembled = manualTeeGraph(familyBindings);
    verifyGraph(assembled, familyBindings);
    reports.push({ family, scenario: 'manual-native-tee', total: assembled.fixtures!.length, counts: { tee: 1, straight: 3, endCap: 3 }, warnings: [] });
  } catch (error) { failures.push({ family, scenario: 'manual-native-tee', error: String(error) }); }
  for (const scenario of scenarios) {
    try {
      const bindings = familyBindings.filter(binding => binding.wallMount!.kind !== scenario.omit);
      const source = room(scenario.cellSize), before = structuredClone(source);
      const placed = placeWallMounts(source, { ...DEFAULT_WALL_SETTINGS, enabled: true, singles: false, runDensity: 40,
        runOrientation: scenario.orientation, connectRuns: true, placeholderFallback: false }, bindings);
      assert.deepEqual(source, before, 'Source map remains unchanged');
      if (scenario.name === 'vertical-1m-grid' && /_Pipe_D(156|260|390)$/.test(family)) {
        assert.equal(placed.map.fixtures!.length, 0, 'Thick low vertical rail is excluded from a 1m character corridor');
        assert.deepEqual(wallMountIssues(placed.map, bindings), []);
        assert.ok(placed.issues.some(issue => issue.code === 'wall-vertical-fit'));
        reports.push({ family, scenario: scenario.name, total: 0, expectedExclusion: 'diameter + .02m wall clearance exceeds .15m character corridor margin', warnings: [...new Set(placed.issues.map(issue => issue.code))] });
        continue;
      }
      verifyGraph(placed.map, bindings);
      const counts: Record<string, number> = {};
      for (const fixture of placed.map.fixtures ?? []) {
        const kind = bindings.find(binding => binding.id === fixture.bindingId)!.wallMount!.kind;
        counts[kind] = (counts[kind] ?? 0) + 1;
      }
      if (scenario.requireKind) assert.ok(counts[scenario.requireKind] > 0, `Actual ${scenario.requireKind} connected horizontal and vertical arms`);
      if (scenario.orientation === 'vertical') assert.ok(placed.map.fixtures!.every(fixture => fixture.wall!.sockets.every(socket => Math.abs(socket.direction.y) > .99999)));
      const junctions = placed.map.fixtures!.filter(fixture => ['elbow', 'tee'].includes(bindings.find(binding => binding.id === fixture.bindingId)!.wallMount!.kind));
      for (const joint of junctions) {
        const network = placed.map.fixtures!.filter(fixture => fixture.wall!.runId === joint.wall!.runId);
        assert.ok(network.some(fixture => fixture.wall!.sockets.length === 2 && fixture.wall!.sockets.every(socket => Math.abs(socket.direction.y) > .99999)));
        assert.ok(network.some(fixture => fixture.wall!.sockets.length === 2 && fixture.wall!.sockets.every(socket => Math.abs(socket.direction.y) < .00001)));
      }
      reports.push({ family, scenario: scenario.name, total: placed.map.fixtures!.length, counts, warnings: [...new Set(placed.issues.map(issue => issue.code))] });
    } catch (error) { failures.push({ family, scenario: scenario.name, error: String(error) }); }
  }
}
const result = { ok: failures.length === 0, catalogPath, registered: imported.added, scenarios: reports.length, reports, failures };
const reportPath = path.join(path.dirname(catalogPath), 'app-smoke-validation.json');
writeFileSync(reportPath, JSON.stringify(result, null, 2));
console.log(JSON.stringify({ ...result, reportPath }, null, 2));
process.exitCode = failures.length ? 1 : 0;
