// Web-mercator camera maths for the GeoLayer composition (plain functions, no React).
import type { GeoLayerProps, LonLat } from './types';

export const PERSPECTIVE = 1500;
const R = Math.PI / 180;

export type Cam = { x: number; y: number; z: number; tilt: number };
export type Key = { f: number; c: LonLat; z: number; t: number };

/** Web-mercator unit square (0..1). */
export const unit = ([lon, lat]: LonLat) => ({
  x: (lon + 180) / 360,
  y: (1 - Math.log(Math.tan(lat * R) + 1 / Math.cos(lat * R)) / Math.PI) / 2,
});

export const toLonLat = (x: number, y: number): LonLat => [x * 360 - 180, (Math.atan(Math.sinh(Math.PI * (1 - 2 * y))) * 180) / Math.PI];

const ease = (t: number) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);

export const camera = (keys: Key[], frame: number): Cam => {
  let i = 0;
  while (i < keys.length - 2 && frame > keys[i + 1].f) i++;
  const a = keys[i], b = keys[Math.min(i + 1, keys.length - 1)];
  const span = Math.max(1, b.f - a.f);
  const p = ease(Math.min(1, Math.max(0, (frame - a.f) / span)));
  const ua = unit(a.c), ub = unit(b.c);
  const z = a.z + (b.z - a.z) * p;
  const dz = b.z - a.z;
  // Even screen motion across big zoom changes: move the centre by how much the scale has changed.
  let u = p;
  if (Math.abs(dz) > 1.5) u = (1 - Math.pow(2, -(z - a.z))) / (1 - Math.pow(2, -dz));
  return { x: ua.x + (ub.x - ua.x) * u, y: ua.y + (ub.y - ua.y) * u, z, tilt: a.t + (b.t - a.t) * p };
};

/** Point on the (untilted) map plane, px relative to the screen centre. */
export const plane = (ll: LonLat, cam: Cam): [number, number] => {
  const u = unit(ll), s = 256 * Math.pow(2, cam.z);
  return [(u.x - cam.x) * s, (u.y - cam.y) * s];
};

/** Plane area the camera sees with the given tilt (CSS rotateX around the centre). */
export const planeRect = (tilt: number, W: number, H: number) => {
  const c = Math.cos(tilt * R), s = Math.sin(tilt * R), P = PERSPECTIVE;
  const yAt = (sy: number) => (sy * P) / (c * P + sy * s);
  const top = yAt(-H / 2), bottom = yAt(H / 2);
  const farScale = P / (P - top * s);
  return { left: -(W / 2) / farScale - 40, right: W / 2 / farScale + 40, top: top - 40, bottom: bottom + 40 };
};

/** Screen position of a ground point seen through the tilted plane. */
export const project = (ll: LonLat, cam: Cam, W: number, H: number) => {
  const [x, y] = plane(ll, cam);
  const t = cam.tilt * R;
  const k = PERSPECTIVE / (PERSPECTIVE - y * Math.sin(t));
  return { sx: W / 2 + x * k, sy: H / 2 + y * Math.cos(t) * k, k };
};

export const metres = (a: LonLat, b: LonLat) => {
  const x = (b[0] - a[0]) * R * Math.cos(((a[1] + b[1]) / 2) * R), y = (b[1] - a[1]) * R;
  return Math.hypot(x, y) * 6371e3;
};

export const metresPerPixel = (lat: number, z: number) => (156543.03 * Math.cos(lat * R)) / Math.pow(2, z);

/** Centre and zoom that show every point inside the given share of the screen. */
export const fit = (pts: LonLat[], W: number, H: number, shareW = 0.72, shareH = 0.42, maxZoom = 17.5): { c: LonLat; z: number } => {
  const us = pts.map(unit);
  const x0 = Math.min(...us.map((u) => u.x)), x1 = Math.max(...us.map((u) => u.x));
  const y0 = Math.min(...us.map((u) => u.y)), y1 = Math.max(...us.map((u) => u.y));
  const dx = Math.max(x1 - x0, 1e-9) * 256, dy = Math.max(y1 - y0, 1e-9) * 256;
  const z = Math.min(maxZoom, Math.log2(Math.min((W * shareW) / dx, (H * shareH) / dy)));
  return { c: toLonLat((x0 + x1) / 2, (y0 + y1) / 2), z };
};

export const FPS = 30;

/** Scenes of the video in frames; a scene without data is left out. */
export type Timeline = {
  total: number;
  intro: [number, number];
  plots: [number, number] | null;
  pois: [number, number] | null;
  routes: [number, number] | null;
  end: [number, number];
};

export const timeline = (p: GeoLayerProps): Timeline => {
  let f = 0;
  const seg = (len: number): [number, number] => {
    const s: [number, number] = [f, f + len];
    f += len;
    return s;
  };
  const intro = seg(230);
  const plots = p.plots.length ? seg(150 + p.plots.length * 45) : null;
  const pois = p.pois.length ? seg(150 + p.pois.length * 14) : null;
  const routes = p.routes.length ? seg(230) : null;
  const end = seg(p.endCard ? 180 : 60);
  return { total: f, intro, plots, pois, routes, end };
};

/** Camera path built from the content: wide view, fly to the site, plots (tilted), amenities, routes. */
export const keyframes = (p: GeoLayerProps, tl: Timeline): Key[] => {
  const W = p.width, H = p.height;
  const site = p.site.position;
  const everything: LonLat[] = [site, ...p.pois.map((x) => x.position), ...p.routes.flatMap((r) => [r.coords[0], r.coords[r.coords.length - 1]])];
  const wide = fit(everything, W, H, 0.6, 0.35, 12.5);
  const wideZ = Math.min(wide.z - 0.6, 11.6);
  const plotPts = p.plots.flatMap((x) => x.ring);
  const close = plotPts.length ? fit(plotPts, W, H, 0.7, 0.32, 17.6) : { c: site, z: 16.5 };
  const poiFit = p.pois.length ? fit([site, ...p.pois.map((x) => x.position)], W, H, 0.78, 0.5, 16.2) : close;
  const routeFit = p.routes.length ? fit([site, ...p.routes.flatMap((r) => r.coords)], W, H, 0.75, 0.45, 15) : poiFit;

  const keys: Key[] = [
    { f: 0, c: wide.c, z: wideZ, t: 0 },
    { f: 70, c: wide.c, z: wideZ + 0.3, t: 0 },
    { f: tl.intro[1] - 20, c: close.c, z: close.z, t: 0 },
  ];
  if (tl.plots) {
    keys.push({ f: tl.plots[0] + 20, c: close.c, z: close.z + 0.05, t: 0 });
    keys.push({ f: tl.plots[1] - 60, c: close.c, z: close.z + 0.35, t: 42 });
    keys.push({ f: tl.plots[1], c: close.c, z: close.z + 0.4, t: 42 });
  }
  if (tl.pois) {
    keys.push({ f: tl.pois[0] + 70, c: poiFit.c, z: poiFit.z, t: 30 });
    keys.push({ f: tl.pois[1], c: poiFit.c, z: poiFit.z + 0.1, t: 30 });
  }
  if (tl.routes) {
    keys.push({ f: tl.routes[0] + 90, c: routeFit.c, z: routeFit.z, t: 22 });
    keys.push({ f: tl.routes[1], c: routeFit.c, z: routeFit.z + 0.15, t: 22 });
  }
  const last = keys[keys.length - 1];
  keys.push({ f: tl.total, c: last.c, z: last.z + 0.05, t: Math.min(last.t, 12) });
  return keys;
};
