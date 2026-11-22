// Real-estate location video on TodoX's map: fly-in, glowing plot boundaries with land use / area / facing,
// amenities with distances, glowing routes, compass and an end card. Scenes without data are skipped.
import React from 'react';
import { AbsoluteFill, interpolate, spring, useCurrentFrame, useVideoConfig } from 'remotion';
import { PERSPECTIVE, camera, keyframes, metresPerPixel, plane, project, timeline, type Cam, type Timeline } from './geo';
import { TodoxMap } from './TodoxMap';
import type { GeoLayerProps, LonLat, PoiKind } from './types';

const FONT = "'Segoe UI', 'Segoe UI Semibold', Arial, sans-serif";
const clamp = { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' } as const;
const fade = (f: number, a: number, b: number, c: number, d: number) => interpolate(f, [a, b, c, d], [0, 1, 1, 0], clamp);
const svgPath = (pts: [number, number][], close = false) =>
  pts.map((p, i) => `${i ? 'L' : 'M'}${p[0].toFixed(1)},${p[1].toFixed(1)}`).join('') + (close ? 'Z' : '');

const along = (pts: [number, number][], t: number): [number, number] => {
  const seg: number[] = [];
  let total = 0;
  for (let i = 1; i < pts.length; i++) {
    const l = Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]);
    seg.push(l);
    total += l;
  }
  let d = total * Math.min(1, Math.max(0, t));
  for (let i = 0; i < seg.length; i++) {
    if (d <= seg[i]) {
      const k = seg[i] ? d / seg[i] : 0;
      return [pts[i][0] + (pts[i + 1][0] - pts[i][0]) * k, pts[i][1] + (pts[i + 1][1] - pts[i][1]) * k];
    }
    d -= seg[i];
  }
  return pts[pts.length - 1];
};

const centroid = (ring: LonLat[]): LonLat => [ring.reduce((s, p) => s + p[0], 0) / ring.length, ring.reduce((s, p) => s + p[1], 0) / ring.length];

// ---------- icons (own drawings) ----------
const ICON: Record<PoiKind, { bg: string; draw: React.ReactNode }> = {
  metro: { bg: '#18c5e8', draw: <text x="0" y="9" textAnchor="middle" fontSize="26" fontWeight="800" fill="#fff" fontFamily={FONT}>M</text> },
  school: { bg: '#4c7dff', draw: <g fill="#fff"><path d="M-16,-3 L0,-11 L16,-3 L0,5 Z" /><path d="M-9,1 L-9,8 Q0,13 9,8 L9,1 L0,5 Z" /><rect x="13" y="-3" width="2.5" height="11" /></g> },
  uni: { bg: '#a05cff', draw: <g fill="#fff"><path d="M-15,-6 L0,-13 L15,-6 Z" /><rect x="-12" y="-4" width="4" height="12" /><rect x="-2" y="-4" width="4" height="12" /><rect x="8" y="-4" width="4" height="12" /><rect x="-15" y="9" width="30" height="3" /></g> },
  market: { bg: '#ff8a2a', draw: <g fill="#fff"><path d="M-14,-4 L14,-4 L10,10 L-10,10 Z" /><path d="M-7,-4 L-2,-13 M7,-4 L2,-13" stroke="#fff" strokeWidth="3" strokeLinecap="round" /></g> },
  health: { bg: '#ff3b5c', draw: <g fill="#fff"><rect x="-4" y="-13" width="8" height="26" rx="1.5" /><rect x="-13" y="-4" width="26" height="8" rx="1.5" /></g> },
  park: { bg: '#22c57a', draw: <g fill="#fff"><circle cy="-4" r="10" /><rect x="-2" y="4" width="4" height="10" /></g> },
  mall: { bg: '#ff5fb0', draw: <g fill="none" stroke="#fff" strokeWidth="3"><path d="M-11,-4 L11,-4 L13,12 L-13,12 Z" fill="#fff" /><path d="M-6,-4 Q-6,-13 0,-13 Q6,-13 6,-4" /></g> },
  bank: { bg: '#2fb8a6', draw: <g fill="#fff"><path d="M-15,-5 L0,-13 L15,-5 Z" /><rect x="-12" y="-3" width="4" height="11" /><rect x="-2" y="-3" width="4" height="11" /><rect x="8" y="-3" width="4" height="11" /><rect x="-15" y="9" width="30" height="3" /></g> },
  star: { bg: '#FFC83D', draw: <path fill="#1a1406" d="M0,-14 L4,-4 L14,-4 L6,3 L9,13 L0,7 L-9,13 L-6,3 L-14,-4 L-4,-4 Z" /> },
};

const Badge: React.FC<{ kind: PoiKind; size?: number; accent: string }> = ({ kind, size = 64, accent }) => {
  const icon = ICON[kind] ?? ICON.star;
  return (
    <svg width={size} height={size} viewBox="-32 -32 64 64" style={{ display: 'block', filter: 'drop-shadow(0 6px 14px rgba(0,0,0,.55))' }}>
      <circle r="29" fill={kind === 'star' ? accent : icon.bg} stroke="#fff" strokeWidth="3" />
      {icon.draw}
    </svg>
  );
};

/** Something standing up on the tilted map at a geo point (faces the camera). */
const Standing: React.FC<{ at: LonLat; cam: Cam; W: number; H: number; anchor?: 'bottom' | 'center'; dx?: number; dy?: number; children: React.ReactNode }> = ({
  at, cam, W, H, children, anchor = 'bottom', dx = 0, dy = 0,
}) => {
  const { sx, sy, k } = project(at, cam, W, H);
  const depth = Math.min(1.1, Math.max(0.7, 0.55 + 0.45 * k));
  return (
    <div style={{ position: 'absolute', left: sx + dx, top: sy + dy, width: 0, height: 0 }}>
      <div style={{ position: 'absolute', left: 0, top: 0, transform: `${anchor === 'bottom' ? 'translate(-50%,-100%)' : 'translate(-50%,-50%)'} scale(${depth})`,
        transformOrigin: anchor === 'bottom' ? '50% 100%' : '50% 50%' }}>
        {children}
      </div>
    </div>
  );
};

const Pill: React.FC<{ title: string; sub?: string; color: string; scale?: number }> = ({ title, sub, color, scale = 1 }) => (
  <div style={{
    transform: `scale(${scale})`, transformOrigin: '50% 100%', background: 'rgba(8,14,24,.82)', border: `2px solid ${color}`,
    borderRadius: 18, padding: '12px 20px', color: '#fff', fontFamily: FONT, whiteSpace: 'nowrap', textAlign: 'center',
    boxShadow: `0 0 24px ${color}66, 0 10px 30px rgba(0,0,0,.5)`,
  }}>
    <div style={{ fontSize: 30, fontWeight: 700, letterSpacing: 0.3 }}>{title}</div>
    {sub && <div style={{ fontSize: 25, color, fontWeight: 600, marginTop: 2 }}>{sub}</div>}
  </div>
);

const Glow: React.FC<{ d: string; color: string; progress: number; width?: number; run?: number; close?: boolean }> = ({ d, color, progress, width = 6, run = 0, close }) => (
  <g>
    <path d={d} pathLength={1} fill="none" stroke={color} strokeWidth={width * 4} strokeOpacity={0.35} strokeLinejoin="round" strokeLinecap="round"
      strokeDasharray="1 1" strokeDashoffset={1 - progress} filter="url(#geoBlur)" />
    <path d={d} pathLength={1} fill="none" stroke={color} strokeWidth={width} strokeLinejoin="round" strokeLinecap="round" strokeDasharray="1 1" strokeDashoffset={1 - progress} />
    <path d={d} pathLength={1} fill="none" stroke="#fff" strokeOpacity={0.85} strokeWidth={width * 0.35} strokeLinejoin="round" strokeLinecap="round"
      strokeDasharray="1 1" strokeDashoffset={1 - progress} />
    {run > 0 && (
      <path d={d} pathLength={1} fill="none" stroke="#fff" strokeWidth={width * 1.1} strokeLinecap="round" filter="url(#geoSoft)"
        strokeDasharray={close ? '0.12 0.88' : '0.06 1'} strokeDashoffset={-run} strokeOpacity={0.9} />
    )}
  </g>
);

const RING_METRES = [500, 1000];

// ---------- on the map plane ----------
const Overlay: React.FC<{ f: number; cam: Cam; p: GeoLayerProps; tl: Timeline }> = ({ f, cam, p, tl }) => {
  const P = (ll: LonLat) => plane(ll, cam);
  const site = P(p.site.position);
  const scale = 1 / metresPerPixel(p.site.position[1], cam.z);
  const pulse = (f % 45) / 45;
  const out: React.ReactNode[] = [];

  if (f < tl.intro[1] + 40) {
    [0, 0.5].forEach((k) => {
      const q = (pulse + k) % 1;
      out.push(<circle key={`pulse${k}`} cx={site[0]} cy={site[1]} r={20 + q * 140} fill="url(#geoPulse)" opacity={(1 - q) * fade(f, 25, 45, tl.intro[1] + 10, tl.intro[1] + 40)} />);
    });
  }

  if (tl.plots) {
    const [a, b] = tl.plots;
    const dim = interpolate(f, [b - 20, b + 20], [1, 0.35], clamp);
    p.plots.forEach((pl, i) => {
      const s = a + 20 + i * 45;
      const prog = interpolate(f, [s, s + 50], [0, 1], { ...clamp, easing: (t) => 1 - Math.pow(1 - t, 3) });
      if (prog <= 0) return;
      const d = svgPath(pl.ring.map(P), true);
      out.push(
        <g key={`plot${i}`} opacity={dim}>
          <path d={d} fill={pl.color} fillOpacity={interpolate(f, [s + 35, s + 65], [0, 0.3], clamp)} />
          <Glow d={d} color={pl.color} progress={prog} width={6} run={f > s + 50 ? (f - s - 50) * 0.012 : 0} close />
        </g>,
      );
    });
  }

  if (tl.pois) {
    const [a, b] = tl.pois;
    const gone = interpolate(f, [b - 20, b + 10], [1, 0], clamp);
    if (f > a - 5 && f < b + 15) {
      RING_METRES.forEach((m, i) => {
        const r = m * scale;
        out.push(
          <circle key={`ring${m}`} cx={site[0]} cy={site[1]} r={r} fill={i ? 'none' : `${p.accent}12`} stroke={p.accent} strokeWidth={3} strokeDasharray="14 12"
            opacity={interpolate(f, [a + i * 18, a + 50 + i * 18], [0, 1], clamp) * gone} transform={`rotate(${-90 + f * 0.15} ${site[0]} ${site[1]})`} />,
        );
      });
    }
    p.pois.forEach((poi, i) => {
      const s = a + 40 + i * 14;
      const k = interpolate(f, [s, s + 18], [0, 1], clamp) * gone;
      if (k <= 0) return;
      const q = P(poi.position);
      out.push(<line key={`lead${i}`} x1={site[0]} y1={site[1]} x2={site[0] + (q[0] - site[0]) * k} y2={site[1] + (q[1] - site[1]) * k}
        stroke={(ICON[poi.kind] ?? ICON.star).bg} strokeWidth={3} strokeDasharray="6 8" opacity={0.9} />);
    });
  }

  if (tl.routes) {
    const [a] = tl.routes;
    p.routes.forEach((r, i) => {
      const s = a + 20 + i * 25;
      const prog = interpolate(f, [s, s + 110], [0, 1], { ...clamp, easing: (t) => 1 - Math.pow(1 - t, 2) });
      if (prog <= 0) return;
      const pts = r.coords.map(P);
      out.push(<Glow key={`route${i}`} d={svgPath(pts)} color={r.color} progress={prog} width={i === 0 ? 7 : 5} run={f > s + 110 ? (f - s - 110) * 0.006 : 0} />);
      if (prog < 1) {
        const h = along(pts, prog);
        out.push(<circle key={`head${i}`} cx={h[0]} cy={h[1]} r={13} fill="#fff" filter="url(#geoSoft)" />);
      }
    });
  }

  return (
    <svg width={1} height={1} style={{ position: 'absolute', left: 0, top: 0, overflow: 'visible', transform: 'translateZ(1px)' }}>
      <defs>
        <filter id="geoBlur" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="9" /></filter>
        <filter id="geoSoft" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="3" /></filter>
        <radialGradient id="geoPulse"><stop offset="0" stopColor={p.accent} stopOpacity="0" /><stop offset="1" stopColor={p.accent} stopOpacity=".7" /></radialGradient>
      </defs>
      {out}
    </svg>
  );
};

// ---------- standing markers (screen space) ----------
const Markers: React.FC<{ f: number; cam: Cam; fps: number; p: GeoLayerProps; tl: Timeline }> = ({ f, cam, fps, p, tl }) => {
  const W = p.width, H = p.height;
  const pop = (s: number) => spring({ frame: f - s, fps, config: { damping: 12, stiffness: 140 } });
  const out: React.ReactNode[] = [];

  /** Horizontal shift that keeps a label of this text inside the frame. */
  const keepInside = (at: LonLat, title: string, sub: string) => {
    const w = Math.max(title.length * 17, sub.length * 14) + 60;
    const x = project(at, cam, W, H).sx;
    return Math.min(W - 28 - w / 2, Math.max(28 + w / 2, x)) - x;
  };
  // Site pin with its name: shown in the intro and again at the end (hidden while plots are labelled).
  const plotsOn = tl.plots ? interpolate(f, [tl.plots[0], tl.plots[0] + 20], [0, 1], clamp) - interpolate(f, [tl.plots[1] - 10, tl.plots[1] + 10], [0, 1], clamp) : 0;
  const pinIn = pop(22);
  const pinOpacity = 1 - Math.max(0, plotsOn);
  if (pinOpacity > 0.01) {
    const showName = f < tl.intro[1] + 30 || (tl.routes ? f > tl.routes[0] : f > tl.end[0]);
    out.push(
      <Standing key="pin" at={p.site.position} cam={cam} W={W} H={H}>
        <div style={{ opacity: Math.min(1, pinOpacity), transform: `translateY(${(1 - pinIn) * -160}px)`, textAlign: 'center' }}>
          {showName && <div style={{ transform: `translateX(${keepInside(p.site.position, p.site.name, p.place)}px)` }}><Pill title={p.site.name} sub={p.place} color={p.accent} /></div>}
          <svg width="70" height="90" viewBox="-35 -10 70 90" style={{ display: 'block', margin: '4px auto 0' }}>
            <path d="M0,78 C-6,60 -30,44 -30,22 A30,30 0 1 1 30,22 C30,44 6,60 0,78 Z" fill={p.accent} stroke="#fff" strokeWidth="3" />
            <circle cy="22" r="11" fill="#1a1406" />
          </svg>
        </div>
      </Standing>,
    );
  }

  if (tl.plots) {
    const [a, b] = tl.plots;
    const offsets = [[0, -30], [-0.14 * W, 60], [0.14 * W, 60], [0, 120], [-0.14 * W, -90], [0.14 * W, -90]];
    p.plots.forEach((pl, i) => {
      const s = a + 20 + i * 45 + 38;
      const k = pop(s) * interpolate(f, [b - 30, b], [1, 0], clamp);
      if (f < s || k <= 0.01) return;
      const [dx, dy] = offsets[i % offsets.length];
      out.push(
        <Standing key={`plotlabel${i}`} at={centroid(pl.ring)} cam={cam} W={W} H={H} dx={dx} dy={dy}>
          <div style={{ opacity: Math.min(1, k * 1.5) }}>
            <Pill scale={k} color={pl.color} title={`LÔ ${pl.id} · ${pl.name}`} sub={[pl.area, pl.direction].filter(Boolean).join(' · ')} />
          </div>
        </Standing>,
      );
    });
  }

  if (tl.pois) {
    const [a, b] = tl.pois;
    const site = project(p.site.position, cam, W, H);
    p.pois.forEach((poi, i) => {
      const s = a + 52 + i * 14;
      const k = pop(s) * interpolate(f, [b - 30, b], [1, 0], clamp);
      if (f < s || k <= 0.01) return;
      const c = (ICON[poi.kind] ?? ICON.star).bg;
      const at = project(poi.position, cam, W, H);
      // Label pushed away from the site so callouts fan out, clamped inside the frame.
      const w = Math.max(poi.name.length, poi.distance.length) * 13 + 40;
      const side = at.sx >= site.sx ? 1 : -1;
      const dy = (at.sy >= site.sy ? 1 : -1) * (58 + (i % 3) * 44);
      const left = Math.min(W - 28 - w, Math.max(28, at.sx + side * 40 - w / 2)) - at.sx;
      out.push(
        <Standing key={`poi${i}`} at={poi.position} cam={cam} W={W} H={H} anchor="center">
          <div style={{ position: 'relative', width: 60, height: 60, transform: `scale(${k})` }}>
            <svg width="1" height="1" style={{ position: 'absolute', left: 30, top: 30, overflow: 'visible' }}>
              <line x1="0" y1="0" x2={left + w / 2} y2={dy} stroke={c} strokeWidth="3" />
            </svg>
            <div style={{ position: 'absolute', left: 30 + left, top: 30 + dy, transform: 'translate(0,-50%)', textAlign: 'center', width: w,
              background: 'rgba(8,14,24,.88)', color: '#fff', fontFamily: FONT, fontSize: 24, fontWeight: 600, padding: '7px 10px', borderRadius: 12,
              boxSizing: 'border-box', whiteSpace: 'nowrap', border: `2px solid ${c}`, boxShadow: `0 0 16px ${c}55` }}>
              {poi.name}{poi.distance && <div style={{ color: p.accent, fontSize: 23 }}>{poi.distance}</div>}
            </div>
            <Badge kind={poi.kind} size={60} accent={p.accent} />
          </div>
        </Standing>,
      );
    });
  }

  if (tl.routes) {
    const [a] = tl.routes;
    p.routes.forEach((r, i) => {
      const s = a + 20 + i * 25 + 100;
      if (f < s) return;
      const d = pop(s);
      const end = r.coords[r.coords.length - 1];
      out.push(
        <Standing key={`dest${i}`} at={end} cam={cam} W={W} H={H}>
          <div style={{ transform: `scale(${d})`, transformOrigin: '50% 100%' }}><Badge kind="star" size={i === 0 ? 62 : 50} accent={p.accent} /></div>
        </Standing>,
        <Standing key={`destlabel${i}`} at={end} cam={cam} W={W} H={H} anchor="center" dy={i % 2 ? -110 : 110} dx={keepInside(end, r.name, [r.distance, r.duration].join(' · '))}>
          <div style={{ transform: `scale(${d})` }}><Pill title={r.name} sub={[r.distance, r.duration].filter(Boolean).join(' · ')} color={r.color} /></div>
        </Standing>,
      );
    });
  }
  return <>{out}</>;
};

// ---------- screen UI ----------
const Panel: React.FC<{ children: React.ReactNode; style: React.CSSProperties }> = ({ children, style }) => (
  <div style={{
    position: 'absolute', background: 'rgba(7,12,20,.72)', border: '1px solid rgba(255,255,255,.14)', borderRadius: 26,
    backdropFilter: 'blur(10px)', color: '#fff', fontFamily: FONT, boxShadow: '0 20px 50px rgba(0,0,0,.45)', ...style,
  }}>{children}</div>
);

const Heading: React.FC<{ f: number; from: number; to: number; kicker: string; title: string; accent: string; top: number }> = ({ f, from, to, kicker, title, accent, top }) => {
  const k = fade(f, from, from + 18, to - 18, to);
  if (k <= 0) return null;
  return (
    <div style={{ position: 'absolute', left: 64, top, opacity: k, transform: `translateX(${(1 - k) * -60}px)`, fontFamily: FONT, color: '#fff' }}>
      <div style={{ fontSize: 28, letterSpacing: 6, color: accent, fontWeight: 700 }}>{kicker}</div>
      <div style={{ fontSize: 66, fontWeight: 800, lineHeight: 1.05, textShadow: '0 4px 24px rgba(0,0,0,.8)', whiteSpace: 'pre-line' }}>{title}</div>
      <div style={{ width: 140 * k, height: 6, background: accent, borderRadius: 3, marginTop: 18, boxShadow: `0 0 16px ${accent}` }} />
    </div>
  );
};

const Compass: React.FC<{ f: number; cam: Cam; from: number; top: number }> = ({ f, cam, from, top }) => (
  <div style={{ position: 'absolute', right: 60, top, width: 120, height: 120, perspective: 400, opacity: interpolate(f, [from, from + 30], [0, 1], clamp) }}>
    <svg width="120" height="120" viewBox="-60 -60 120 120" style={{ transform: `rotateX(${cam.tilt}deg)` }}>
      <circle r="52" fill="rgba(7,12,20,.7)" stroke="rgba(255,255,255,.4)" strokeWidth="2" />
      <path d="M0,-42 L11,0 L0,-6 L-11,0 Z" fill="#ff3b5c" />
      <path d="M0,42 L11,0 L0,6 L-11,0 Z" fill="#cfd6e0" />
      <text y="-20" textAnchor="middle" fontSize="15" fontWeight="800" fill="#fff" fontFamily={FONT} dy="-26">N</text>
    </svg>
  </div>
);

export const GeoLayer: React.FC<GeoLayerProps> = (p) => {
  const f = useCurrentFrame();
  const { fps } = useVideoConfig();
  const W = p.width, H = p.height;
  const tl = timeline(p);
  const cam = camera(keyframes(p, tl), f);
  const portrait = H > W;
  const top = portrait ? 150 : 70;
  const titleK = fade(f, 12, 32, tl.intro[1] - 55, tl.intro[1] - 30);
  const legendK = tl.plots ? fade(f, tl.plots[1] - 150, tl.plots[1] - 128, tl.plots[1] - 30, tl.plots[1] - 5) : 0;
  const endK = p.endCard ? fade(f, tl.end[0], tl.end[0] + 20, tl.total + 30, tl.total + 40) : 0;
  const panelPos: React.CSSProperties = portrait ? { left: 54, right: 54, bottom: 150 } : { right: 60, bottom: 90, width: 760 };

  return (
    <AbsoluteFill style={{ background: '#05080d', overflow: 'hidden' }}>
      <div style={{ position: 'absolute', inset: 0, perspective: PERSPECTIVE, perspectiveOrigin: '50% 50%' }}>
        <div style={{ position: 'absolute', left: W / 2, top: H / 2, width: 0, height: 0, transform: `rotateX(${cam.tilt}deg)`, transformStyle: 'preserve-3d' }}>
          <TodoxMap cam={cam} styleUrl={p.styleUrl} W={W} H={H} />
          <Overlay f={f} cam={cam} p={p} tl={tl} />
        </div>
      </div>
      <Markers f={f} cam={cam} fps={fps} p={p} tl={tl} />
      <div style={{ position: 'absolute', inset: 0, background: `linear-gradient(180deg, rgba(5,8,13,${0.2 + cam.tilt / 60}) 0%, rgba(5,8,13,0) 28%, rgba(5,8,13,0) 70%, rgba(5,8,13,.75) 100%)` }} />
      <div style={{ position: 'absolute', inset: 0, boxShadow: 'inset 0 0 220px rgba(0,0,0,.85)' }} />

      {titleK > 0 && (
        <div style={{ position: 'absolute', left: 64, right: 64, top: portrait ? 230 : 90, opacity: titleK, transform: `translateY(${(1 - titleK) * 40}px)`, fontFamily: FONT, color: '#fff' }}>
          <div style={{ fontSize: 30, letterSpacing: 8, color: p.accent, fontWeight: 700 }}>{p.kicker}</div>
          <div style={{ fontSize: portrait ? 96 : 84, fontWeight: 800, lineHeight: 1, marginTop: 10, textShadow: '0 6px 30px rgba(0,0,0,.9)', whiteSpace: 'pre-line' }}>{p.title}</div>
          <div style={{ fontSize: 40, marginTop: 18, color: '#e8edf3', textShadow: '0 4px 20px rgba(0,0,0,.9)' }}>{p.place}</div>
        </div>
      )}
      {tl.plots && <Heading f={f} from={tl.plots[0]} to={tl.plots[1]} kicker="BƯỚC 1" title={p.headings.plots} accent={p.accent} top={top} />}
      {tl.pois && <Heading f={f} from={tl.pois[0]} to={tl.pois[1]} kicker={tl.plots ? 'BƯỚC 2' : 'BƯỚC 1'} title={p.headings.pois} accent={p.accent} top={top} />}
      {tl.routes && <Heading f={f} from={tl.routes[0]} to={tl.total + 30} kicker={`BƯỚC ${1 + (tl.plots ? 1 : 0) + (tl.pois ? 1 : 0)}`} title={p.headings.routes} accent={p.accent} top={top} />}
      <Compass f={f} cam={cam} from={tl.intro[1] - 10} top={top} />

      {legendK > 0 && (
        <Panel style={{ ...panelPos, padding: '30px 36px', opacity: legendK, transform: `translateY(${(1 - legendK) * 60}px)` }}>
          <div style={{ fontSize: 26, letterSpacing: 4, color: p.accent, fontWeight: 700, marginBottom: 14 }}>CHÚ THÍCH QUY HOẠCH</div>
          {p.plots.map((pl) => (
            <div key={pl.id} style={{ display: 'flex', alignItems: 'center', fontSize: 32, margin: '10px 0' }}>
              <div style={{ width: 46, height: 26, borderRadius: 6, background: `${pl.color}55`, border: `4px solid ${pl.color}`, marginRight: 20, boxShadow: `0 0 14px ${pl.color}` }} />
              <div style={{ flex: 1 }}><b>Lô {pl.id}</b> · {pl.name}</div>
              <div style={{ fontWeight: 700, marginLeft: 16 }}>{pl.area}</div>
            </div>
          ))}
        </Panel>
      )}

      {endK > 0 && p.endCard && (
        <Panel style={{ ...panelPos, padding: '34px 40px', opacity: endK, transform: `translateY(${(1 - endK) * 60}px)` }}>
          <div style={{ fontSize: 50, fontWeight: 800, lineHeight: 1.1 }}>{p.endCard.title}</div>
          {p.endCard.subtitle && <div style={{ fontSize: 30, color: '#cfd6e0', marginBottom: 16 }}>{p.endCard.subtitle}</div>}
          {p.endCard.rows.map(([a, b], i) => {
            const r = interpolate(f, [tl.end[0] + 20 + i * 8, tl.end[0] + 35 + i * 8], [0, 1], clamp);
            return (
              <div key={a + i} style={{ display: 'flex', fontSize: 32, margin: '8px 0', opacity: r, transform: `translateX(${(1 - r) * 40}px)` }}>
                <div style={{ flex: 1 }}>• {a}</div><div style={{ fontWeight: 700, color: p.accent, marginLeft: 16 }}>{b}</div>
              </div>
            );
          })}
          {p.endCard.hotline && (
            <div style={{ marginTop: 22, background: p.accent, color: '#1a1406', borderRadius: 16, padding: '16px 0', textAlign: 'center', fontSize: 36, fontWeight: 800 }}>
              {p.endCard.hotline}
            </div>
          )}
        </Panel>
      )}

      <div style={{ position: 'absolute', left: 0, right: 0, bottom: portrait ? 52 : 24, textAlign: 'center', fontFamily: FONT, fontSize: 22, color: 'rgba(255,255,255,.6)' }}>
        {tl.plots && p.plotNote && f > tl.plots[0] && f < tl.plots[1] ? `${p.plotNote} · ` : ''}{p.attribution}
      </div>
    </AbsoluteFill>
  );
};

export const geoTotalFrames = (p: GeoLayerProps) => timeline(p).total;
