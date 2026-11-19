// Extracts a bbox from a remote PMTiles v3 archive into a local PMTiles v3 file (no dependencies).
//   node extract.mjs <sourceUrl> <out.pmtiles> <minLon,minLat,maxLon,maxLat> <maxZoom> [--plan]
import fs from 'node:fs';
import zlib from 'node:zlib';

const [src, outPath, bboxArg, maxZArg] = process.argv.slice(2);
const PLAN = process.argv.includes('--plan');
const [minLon, minLat, maxLon, maxLat] = bboxArg.split(',').map(Number);
const MAXZ = Number(maxZArg);

// ---------- helpers ----------
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let bytesFetched = 0, requests = 0;
async function range(offset, length) {
  for (let attempt = 0; ; attempt++) {
    try {
      const res = await fetch(src, { headers: { Range: `bytes=${offset}-${offset + length - 1}`, 'User-Agent': 'todox-map-extract/1.0' } });
      if (res.status !== 206 && res.status !== 200) throw new Error('HTTP ' + res.status);
      const buf = Buffer.from(await res.arrayBuffer());
      if (buf.length !== length) throw new Error(`short read ${buf.length}/${length}`);
      bytesFetched += length; requests++;
      return buf;
    } catch (e) {
      if (attempt >= 6) throw e;
      await sleep(1000 * (attempt + 1));
    }
  }
}
const decompress = (buf, c) => (c === 2 ? zlib.gunzipSync(buf) : c === 1 ? buf : (() => { throw new Error('compression ' + c); })());

function readVarint(buf, pos) {
  let result = 0, mul = 1, b;
  do { b = buf[pos.i++]; result += (b & 0x7f) * mul; mul *= 128; } while (b & 0x80);
  return result;
}
function writeVarint(arr, v) {
  while (v >= 128) { arr.push((v % 128) | 0x80); v = Math.floor(v / 128); }
  arr.push(v);
}
const u64 = (b, o) => Number(b.readBigUInt64LE(o));

function parseDir(buf) {
  const pos = { i: 0 };
  const n = readVarint(buf, pos);
  const e = Array.from({ length: n }, () => ({ tileId: 0, runLength: 0, length: 0, offset: 0 }));
  let last = 0;
  for (let i = 0; i < n; i++) { last += readVarint(buf, pos); e[i].tileId = last; }
  for (let i = 0; i < n; i++) e[i].runLength = readVarint(buf, pos);
  for (let i = 0; i < n; i++) e[i].length = readVarint(buf, pos);
  for (let i = 0; i < n; i++) {
    const v = readVarint(buf, pos);
    e[i].offset = v === 0 && i > 0 ? e[i - 1].offset + e[i - 1].length : v - 1;
  }
  return e;
}
function serializeDir(entries) {
  const a = [];
  writeVarint(a, entries.length);
  let last = 0;
  for (const e of entries) { writeVarint(a, e.tileId - last); last = e.tileId; }
  for (const e of entries) writeVarint(a, e.runLength);
  for (const e of entries) writeVarint(a, e.length);
  entries.forEach((e, i) => writeVarint(a, i > 0 && e.offset === entries[i - 1].offset + entries[i - 1].length ? 0 : e.offset + 1));
  return zlib.gzipSync(Buffer.from(a), { level: 9 });
}

// Hilbert tile id (PMTiles v3)
function zxyToId(z, x, y) {
  let acc = (Math.pow(4, z) - 1) / 3;
  const n = Math.pow(2, z);
  let d = 0;
  for (let s = n / 2; s >= 1; s /= 2) {
    const rx = (x & s) > 0 ? 1 : 0, ry = (y & s) > 0 ? 1 : 0;
    d += s * s * ((3 * rx) ^ ry);
    if (ry === 0) {
      if (rx === 1) { x = n - 1 - x; y = n - 1 - y; }
      const t = x; x = y; y = t;
    }
  }
  return acc + d;
}
const lon2x = (lon, z) => Math.floor(((lon + 180) / 360) * Math.pow(2, z));
const lat2y = (lat, z) => { const r = (lat * Math.PI) / 180; return Math.floor(((1 - Math.log(Math.tan(r) + 1 / Math.cos(r)) / Math.PI) / 2) * Math.pow(2, z)); };

// ---------- read source header ----------
const head = await range(0, 16384);
if (head.toString('ascii', 0, 7) !== 'PMTiles' || head[7] !== 3) throw new Error('not PMTiles v3');
const H = {
  rootOff: u64(head, 8), rootLen: u64(head, 16), metaOff: u64(head, 24), metaLen: u64(head, 32),
  leafOff: u64(head, 40), leafLen: u64(head, 48), dataOff: u64(head, 56), dataLen: u64(head, 64),
  internalC: head[97], tileC: head[98], tileType: head[99], minZ: head[100], maxZ: head[101],
};
console.log('source', JSON.stringify(H));

// ---------- wanted tile ids ----------
const wantedList = [];
for (let z = 0; z <= MAXZ; z++) {
  const x0 = lon2x(minLon, z), x1 = lon2x(maxLon, z), y0 = lat2y(maxLat, z), y1 = lat2y(minLat, z);
  for (let x = x0; x <= x1; x++) for (let y = y0; y <= y1; y++) wantedList.push(zxyToId(z, x, y));
}
const wanted = Float64Array.from(wantedList).sort();
console.log('wanted tiles', wanted.length);

// ---------- resolve every wanted id to (offset,length) in source tile data ----------
const srcOff = new Float64Array(wanted.length).fill(-1), srcLen = new Float64Array(wanted.length);
const findEntry = (entries, id) => {
  let lo = 0, hi = entries.length - 1, best = -1;
  while (lo <= hi) { const m = (lo + hi) >> 1; if (entries[m].tileId <= id) { best = m; lo = m + 1; } else hi = m - 1; }
  return best;
};
async function resolve(entries, idxs) {
  const leaves = new Map(); // entry index -> list of wanted indexes
  for (const i of idxs) {
    const k = findEntry(entries, wanted[i]);
    if (k < 0) continue;
    const e = entries[k];
    if (e.runLength === 0) { if (!leaves.has(k)) leaves.set(k, []); leaves.get(k).push(i); }
    else if (wanted[i] < e.tileId + e.runLength) { srcOff[i] = e.offset; srcLen[i] = e.length; }
  }
  const jobs = [...leaves.entries()];
  let next = 0;
  await Promise.all(Array.from({ length: 8 }, async () => {
    while (next < jobs.length) {
      const [k, list] = jobs[next++];
      const e = entries[k];
      const leaf = parseDir(decompress(await range(H.leafOff + e.offset, e.length), H.internalC));
      await resolve(leaf, list);
    }
  }));
  return leaves.size;
}
const root = parseDir(decompress(head.subarray(H.rootOff, H.rootOff + H.rootLen), H.internalC));
const all = Array.from({ length: wanted.length }, (_, i) => i);
const leafCount = await resolve(root, all);
let present = 0, total = 0;
const contents = new Map(); // srcOffset -> length
for (let i = 0; i < wanted.length; i++) if (srcOff[i] >= 0) { present++; contents.set(srcOff[i], srcLen[i]); }
for (const l of contents.values()) total += l;
console.log(`leaf dirs read ${leafCount}, tiles present ${present}, unique contents ${contents.size}, data ${(total / 1048576).toFixed(1)} MB`);
if (PLAN) process.exit(0);

// ---------- layout output ----------
const order = [...contents.keys()].sort((a, b) => a - b);
const outOffOf = new Map();
let cursor = 0;
for (const o of order) { outOffOf.set(o, cursor); cursor += contents.get(o); }
const entries = [];
for (let i = 0; i < wanted.length; i++) {
  if (srcOff[i] < 0) continue;
  const off = outOffOf.get(srcOff[i]), last = entries[entries.length - 1];
  if (last && last.offset === off && last.tileId + last.runLength === wanted[i]) last.runLength++;
  else entries.push({ tileId: wanted[i], runLength: 1, length: srcLen[i], offset: off });
}
let rootBuf, leafBuf = Buffer.alloc(0);
const plainRoot = serializeDir(entries);
if (plainRoot.length <= 16384 - 127) rootBuf = plainRoot;
else {
  for (let size = 4096; ; size *= 2) {
    const rootEntries = [], parts = [];
    let lo = 0;
    for (let i = 0; i < entries.length; i += size) {
      const b = serializeDir(entries.slice(i, i + size));
      rootEntries.push({ tileId: entries[i].tileId, runLength: 0, length: b.length, offset: lo });
      parts.push(b); lo += b.length;
    }
    const r = serializeDir(rootEntries);
    if (r.length <= 16384 - 127) { rootBuf = r; leafBuf = Buffer.concat(parts); break; }
  }
}
const meta = zlib.gzipSync(decompress(await range(H.metaOff, H.metaLen), H.internalC));

const header = Buffer.alloc(127);
header.write('PMTiles', 0, 'ascii'); header[7] = 3;
const rootOff = 127, metaOff = rootOff + rootBuf.length, leafOff = metaOff + meta.length, dataOff = leafOff + leafBuf.length;
[[8, rootOff], [16, rootBuf.length], [24, metaOff], [32, meta.length], [40, leafOff], [48, leafBuf.length], [56, dataOff], [64, cursor],
  [72, present], [80, entries.length], [88, contents.size]].forEach(([o, v]) => header.writeBigUInt64LE(BigInt(v), o));
header[96] = 0; header[97] = 2; header[98] = H.tileC; header[99] = H.tileType; header[100] = 0; header[101] = MAXZ;
header.writeInt32LE(Math.round(minLon * 1e7), 102); header.writeInt32LE(Math.round(minLat * 1e7), 106);
header.writeInt32LE(Math.round(maxLon * 1e7), 110); header.writeInt32LE(Math.round(maxLat * 1e7), 114);
header[118] = 6; header.writeInt32LE(Math.round(106.7 * 1e7), 119); header.writeInt32LE(Math.round(10.78 * 1e7), 123);

const tmp = outPath + '.part';
const fd = fs.openSync(tmp, 'w');
fs.writeSync(fd, header); fs.writeSync(fd, rootBuf); fs.writeSync(fd, meta); fs.writeSync(fd, leafBuf);

// ---------- copy tile data in coalesced chunks, in order ----------
const chunks = [];
for (const o of order) {
  const len = contents.get(o), c = chunks[chunks.length - 1];
  if (c && o - (c.start + c.len) < 256 * 1024 && o + len - c.start < 24 * 1048576) { c.len = o + len - c.start; c.items.push(o); }
  else chunks.push({ start: o, len, items: [o] });
}
console.log('chunks', chunks.length);
const results = new Array(chunks.length);
let nextFetch = 0, nextWrite = 0, written = 0;
const t0 = Date.now();
await Promise.all(Array.from({ length: 6 }, async () => {
  while (nextFetch < chunks.length) {
    const ci = nextFetch++;
    if (ci - nextWrite > 40) { nextFetch--; await sleep(200); continue; } // bounded memory
    results[ci] = await range(H.dataOff + chunks[ci].start, chunks[ci].len);
    while (results[nextWrite]) {
      const c = chunks[nextWrite], buf = results[nextWrite];
      for (const o of c.items) { const s = o - c.start; fs.writeSync(fd, buf, s, contents.get(o)); written += contents.get(o); }
      results[nextWrite] = null; results[nextWrite] = undefined;
      chunks[nextWrite].done = true;
      nextWrite++;
      if (nextWrite % 200 === 0) console.log(`chunks ${nextWrite}/${chunks.length}  ${(written / 1048576).toFixed(0)} MB  ${((Date.now() - t0) / 1000).toFixed(0)} s`);
    }
  }
}));
fs.closeSync(fd);
if (written !== cursor) throw new Error(`wrote ${written} expected ${cursor}`);
fs.renameSync(tmp, outPath);
console.log(`DONE ${outPath} ${(fs.statSync(outPath).size / 1048576).toFixed(1)} MB, fetched ${(bytesFetched / 1048576).toFixed(0)} MB in ${requests} requests`);
