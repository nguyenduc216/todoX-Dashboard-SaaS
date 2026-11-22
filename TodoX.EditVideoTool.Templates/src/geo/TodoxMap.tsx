// TodoX's self-hosted vector map (dashboard.todox.vn/maps) drawn flat with MapLibre; the parent tilts it in 3D.
// Every frame waits until the map has drawn all its tiles, so the video never shows half-loaded map.
import React, { useEffect, useRef, useState } from 'react';
import { continueRender, delayRender } from 'remotion';
import maplibregl from 'maplibre-gl';
import { Protocol } from 'pmtiles';
import 'maplibre-gl/dist/maplibre-gl.css';
import { planeRect, toLonLat, type Cam } from './geo';

let protocolAdded = false;

/** Canvas covering the plane the camera can see at any tilt used (the far part is capped; the sky fade hides it). */
export const mapBox = (W: number, H: number) => {
  const tilted = planeRect(42, W, H);
  const width = Math.min(W * 2.6, Math.max(W + 160, tilted.right * 2));
  const top = Math.max(-1.35 * H, tilted.top);
  const bottom = H / 2 + 80;
  return { width, top, bottom, height: bottom - top };
};

export const TodoxMap: React.FC<{ cam: Cam; styleUrl: string; W: number; H: number }> = ({ cam, styleUrl, W, H }) => {
  const box = useRef<HTMLDivElement>(null);
  const map = useRef<maplibregl.Map | null>(null);
  const [ready] = useState(() => delayRender('TodoX map style', { timeoutInMilliseconds: 120000 }));
  const b = mapBox(W, H);

  useEffect(() => {
    if (!protocolAdded) {
      maplibregl.addProtocol('pmtiles', new Protocol().tile);
      protocolAdded = true;
    }
    const m = new maplibregl.Map({
      container: box.current!, style: styleUrl, interactive: false, attributionControl: false, fadeDuration: 0,
      preserveDrawingBuffer: true, pixelRatio: 1, center: [106.7, 10.8], zoom: 12, maxTileCacheSize: 2000,
    } as maplibregl.MapOptions);
    m.once('idle', () => continueRender(ready));
    m.on('error', () => undefined); // a missing tile must not stop the video
    map.current = m;
    return () => m.remove();
  }, []);

  useEffect(() => {
    const m = map.current;
    if (!m) return;
    const handle = delayRender('TodoX map frame', { timeoutInMilliseconds: 120000 });
    // Container centre in plane px (relative to the camera centre) -> lon/lat; MapLibre tiles are 512 px, ours 256.
    const scale = 256 * Math.pow(2, cam.z);
    const center = toLonLat(cam.x, cam.y + (b.top + b.bottom) / 2 / scale);
    let done = false;
    const finish = () => {
      if (!done) {
        done = true;
        continueRender(handle);
      }
    };
    const go = () => {
      m.jumpTo({ center, zoom: cam.z - 1, bearing: 0, pitch: 0 });
      m.once('idle', finish);
      m.triggerRepaint();
    };
    if (m.isStyleLoaded()) go();
    else m.once('load', go);
    setTimeout(finish, 90000); // never hang the render on one slow tile
  }, [cam.x, cam.y, cam.z]);

  return <div ref={box} style={{ position: 'absolute', left: -b.width / 2, top: b.top, width: b.width, height: b.height, background: '#0a0e15' }} />;
};
