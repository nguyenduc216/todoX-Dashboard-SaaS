import React from 'react';
import {
  AbsoluteFill,
  Audio,
  Img,
  OffthreadVideo,
  Sequence,
  Easing,
  interpolate,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from 'remotion';
import { TransitionSeries, linearTiming } from '@remotion/transitions';
import { fade } from '@remotion/transitions/fade';
import { slide } from '@remotion/transitions/slide';

import type { AutoEditProps, Caption, Clip, Headline, Logo, Overlay, Style } from './types';

const useUnit = () => {
  const { width, height } = useVideoConfig();
  return Math.min(width, height) / 1080;
};

const Intro: React.FC<{ title: string; subtitle: string; style: Style; accent: string }> = ({ title, subtitle, style, accent }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const unit = useUnit();
  const pop = spring({ frame, fps, config: { damping: 14, mass: 0.8 } });
  const bar = interpolate(frame, [4, 18], [0, 1], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' });
  const sub = interpolate(frame, [12, 24], [0, 1], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' });
  return (
    <AbsoluteFill style={{ backgroundColor: style.background, justifyContent: 'center', alignItems: 'center', fontFamily: style.fontFamily }}>
      <div style={{ transform: `scale(${0.8 + 0.2 * pop})`, opacity: pop, textAlign: 'center', padding: 60 * unit }}>
        <div style={{ color: style.text, fontSize: 96 * unit, fontWeight: 800, lineHeight: 1.1 }}>{title}</div>
        <div style={{ height: 10 * unit, width: `${bar * 60}%`, margin: `${28 * unit}px auto`, background: accent, borderRadius: 99 }} />
        <div style={{ color: style.text, opacity: 0.8 * sub, fontSize: 44 * unit, transform: `translateY(${(1 - sub) * 20}px)` }}>{subtitle}</div>
      </div>
    </AbsoluteFill>
  );
};

const Outro: React.FC<{ text: string; style: Style; accent: string }> = ({ text, style, accent }) => {
  const frame = useCurrentFrame();
  const unit = useUnit();
  const o = interpolate(frame, [0, 12], [0, 1], { extrapolateRight: 'clamp' });
  return (
    <AbsoluteFill style={{ backgroundColor: style.background, justifyContent: 'center', alignItems: 'center', fontFamily: style.fontFamily }}>
      <div style={{ opacity: o, textAlign: 'center', padding: 80 * unit }}>
        <div style={{ color: accent, fontSize: 72 * unit, fontWeight: 800 }}>{text}</div>
        <div style={{ height: 8 * unit, width: '40%', margin: `${24 * unit}px auto 0`, background: accent, borderRadius: 99, opacity: o }} />
      </div>
    </AbsoluteFill>
  );
};

const FX_IN = 8;   // frames of the entry effect
const FX_OUT = 5;  // frames of the exit swipe before a whip cut

/** A clip scene with a slow push-in/out so still-ish footage keeps moving; optional cut effects in and out. */
const ClipScene: React.FC<{ clip: Clip; fps: number; frames: number; index: number; zoom: number; nextFx?: string }> = ({ clip, fps, frames, index, zoom, nextFx }) => {
  const frame = useCurrentFrame();
  const { width } = useVideoConfig();
  const zoomIn = index % 2 === 0;
  // A punch-in scene starts closer and drifts a little; otherwise the slow push-in/out.
  let scale = clip.zoom && clip.zoom > 0
    ? interpolate(frame, [0, frames], [1 + clip.zoom, 1 + clip.zoom + 0.02], { extrapolateRight: 'clamp' })
    : interpolate(frame, [0, frames], zoomIn ? [1, 1 + zoom] : [1 + zoom, 1], { extrapolateRight: 'clamp' });
  let x = 0, y = 0, blur = 0, rot = 0;
  const dir = index % 2 === 0 ? 1 : -1;
  const inK = interpolate(frame, [0, FX_IN], [1, 0], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp', easing: Easing.out(Easing.cubic) });
  if (clip.fx === 'whip') {
    // Small shift with an over-scale so the picture always covers the frame; blur and the streak sell the speed.
    x += dir * 0.12 * width * inK; scale *= 1 + 0.26 * inK; blur += 24 * inK;
  } else if (clip.fx === 'zoom') {
    scale *= 1 + 0.35 * inK; blur += 18 * inK; rot += dir * 3 * inK;
  } else if (clip.fx === 'flash') {
    scale *= 1 + 0.08 * inK;
  } else if (clip.fx === 'shake' && frame < FX_IN) {
    x += Math.sin(frame * 2.7) * 22 * inK; y += Math.cos(frame * 3.1) * 16 * inK; rot += Math.sin(frame * 1.9) * 1.5 * inK;
  }
  if (nextFx === 'whip' && frames > FX_OUT * 3) {
    const out = interpolate(frame, [frames - FX_OUT, frames], [0, 1], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp', easing: Easing.in(Easing.cubic) });
    // Leave in the opposite direction of the next scene's arrival so the motion reads as one swipe.
    x -= (index % 2 === 0 ? -1 : 1) * 0.12 * width * out; scale *= 1 + 0.26 * out; blur += 24 * out;
  }
  return (
    <AbsoluteFill style={{ backgroundColor: '#000000', overflow: 'hidden' }}>
      <OffthreadVideo
        src={clip.src}
        startFrom={Math.round(clip.from * fps)}
        muted={!clip.keepAudio}
        style={{ width: '100%', height: '100%', objectFit: 'cover',
          transform: `translate(${x}px, ${y}px) rotate(${rot}deg) scale(${scale})`, filter: blur > 0.3 ? `blur(${blur}px)` : undefined }}
      />
    </AbsoluteFill>
  );
};

/** A cutaway over the main scenes: cropped to drop burned-in text, optional speed, entry effect (whip also leaves by a swipe). */
const OverlayScene: React.FC<{ overlay: Overlay; fps: number; frames: number; index: number }> = ({ overlay, fps, frames, index }) => {
  const frame = useCurrentFrame();
  const { width } = useVideoConfig();
  const keep = overlay.crop && overlay.crop !== 'none' ? Math.min(1, Math.max(0.5, overlay.keep ?? 0.75)) : 1;
  const grow = 1 / keep;
  const inner: React.CSSProperties = { position: 'absolute', width: `${grow * 100}%`, height: `${grow * 100}%`, left: `${-(grow - 1) * 50}%` };
  if (overlay.crop === 'top') inner.top = 0;
  else if (overlay.crop === 'bottom') inner.bottom = 0;
  else inner.top = `${-(grow - 1) * 50}%`;

  let x = 0, y = 0, blur = 0, rot = 0;
  let scale = interpolate(frame, [0, frames], index % 2 === 0 ? [1, 1.05] : [1.05, 1], { extrapolateRight: 'clamp' });
  const dir = index % 2 === 0 ? 1 : -1;
  const inK = interpolate(frame, [0, FX_IN], [1, 0], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp', easing: Easing.out(Easing.cubic) });
  if (overlay.fx === 'whip') {
    x += dir * 0.12 * width * inK; scale *= 1 + 0.26 * inK; blur += 24 * inK;
    const out = interpolate(frame, [frames - FX_OUT, frames], [0, 1], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp', easing: Easing.in(Easing.cubic) });
    x -= dir * 0.12 * width * out; scale *= 1 + 0.26 * out; blur += 24 * out;
  } else if (overlay.fx === 'zoom') {
    scale *= 1 + 0.35 * inK; blur += 18 * inK; rot += dir * 3 * inK;
  } else if (overlay.fx === 'flash') {
    scale *= 1 + 0.08 * inK;
  } else if (overlay.fx === 'shake' && frame < FX_IN) {
    x += Math.sin(frame * 2.7) * 22 * inK; y += Math.cos(frame * 3.1) * 16 * inK; rot += Math.sin(frame * 1.9) * 1.5 * inK;
  }
  return (
    <AbsoluteFill style={{ backgroundColor: '#000000', overflow: 'hidden' }}>
      <div style={{ position: 'absolute', inset: 0, transform: `translate(${x}px, ${y}px) rotate(${rot}deg) scale(${scale})`, filter: blur > 0.3 ? `blur(${blur}px)` : undefined }}>
        <div style={inner}>
          <OffthreadVideo src={overlay.src} startFrom={Math.round(overlay.from * fps)} playbackRate={overlay.speed ?? 1} muted
            style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
        </div>
      </div>
    </AbsoluteFill>
  );
};

/** Overlays on the cuts: a white blink for flash/zoom, a light streak sweeping across for whip. */
const CutFx: React.FC<{ cuts: { frame: number; fx: string; index: number }[] }> = ({ cuts }) => {
  const frame = useCurrentFrame();
  const { width } = useVideoConfig();
  const cut = cuts.find((c) => frame >= c.frame - FX_OUT && frame < c.frame + FX_IN);
  if (!cut) {
    return null;
  }
  const d = frame - cut.frame;
  if (cut.fx === 'flash' || cut.fx === 'zoom') {
    const o = interpolate(d, [-2, 0, 6], [0, cut.fx === 'flash' ? 0.9 : 0.55, 0], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' });
    return <AbsoluteFill style={{ background: 'radial-gradient(circle at 50% 45%, #FFFFFF 0%, #FFF4C2 55%, #FFE11A 100%)', opacity: o }} />;
  }
  if (cut.fx === 'whip') {
    const dir = cut.index % 2 === 0 ? -1 : 1;  // streak runs the way the picture moves
    const p = interpolate(d, [-FX_OUT, FX_IN], [0, 1], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' });
    const pos = dir > 0 ? -0.6 + 2.2 * p : 1.6 - 2.2 * p;
    const o = interpolate(d, [-FX_OUT, 0, FX_IN], [0, 0.85, 0], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' });
    return (
      <AbsoluteFill style={{ overflow: 'hidden', mixBlendMode: 'screen', opacity: o }}>
        <div style={{ position: 'absolute', top: '-20%', height: '140%', width: width * 0.55, left: pos * width - width * 0.275,
          transform: 'skewX(-18deg)', filter: 'blur(18px)',
          background: 'linear-gradient(90deg, rgba(255,255,255,0) 0%, rgba(255,236,150,0.9) 40%, #FFFFFF 50%, rgba(255,236,150,0.9) 60%, rgba(255,255,255,0) 100%)' }} />
      </AbsoluteFill>
    );
  }
  return null;
};

/** Words wrapped in *stars* are keywords (coloured); returns the words without the stars. */
const keywords = (text: string) => {
  let inside = false;
  return text.split(/\s+/).filter(Boolean).map((w) => {
    const opens = w.startsWith('*');
    const closes = w.endsWith('*') && (w.length > 1 || inside);
    const hot = inside || opens;
    if (opens) inside = true;
    if (closes) inside = false;
    return { word: w.replace(/\*/g, ''), hot };
  }).filter((w) => w.word.length > 0);
};

/** TikTok-style captions: big heavy white words with a dark outline, the spoken word pops, keywords in yellow. */
const TikTokCaptions: React.FC<{ captions: Caption[]; style: Style; accent: string }> = ({ captions, style }) => {
  const frame = useCurrentFrame();
  const { fps, height } = useVideoConfig();
  const unit = useUnit();
  const t = frame / fps;
  const active = captions.find((c) => t >= c.start && t < c.end);
  if (!active) {
    return null;
  }

  const words = keywords(active.text);
  const progress = (t - active.start) / Math.max(0.01, active.end - active.start);
  const current = Math.min(words.length - 1, Math.floor(progress * words.length));
  const enter = spring({ frame: Math.round((t - active.start) * fps), fps, config: { damping: 12, mass: 0.5 } });
  return (
    <AbsoluteFill style={{ justifyContent: 'flex-end', alignItems: 'center', paddingBottom: height * 0.26 }}>
      <div style={{ maxWidth: '88%', textAlign: 'center', fontFamily: `"Segoe UI Black", ${style.fontFamily}`, fontWeight: 900,
        fontSize: 66 * unit, lineHeight: 1.18, textTransform: 'uppercase', transform: `scale(${0.9 + 0.1 * enter})` }}>
        {words.map((w, i) => {
          const pop = i === current ? 1.12 : 1;
          return (
            <span key={i} style={{
              display: 'inline-block', margin: `0 ${14 * unit}px`, transform: `scale(${pop})`,
              color: w.hot ? '#FFE11A' : '#FFFFFF',
              WebkitTextStroke: `${8 * unit}px #0B0B0B`, paintOrder: 'stroke fill',
              textShadow: `0 ${6 * unit}px ${14 * unit}px rgba(0,0,0,0.55)`,
            }}>{w.word}</span>
          );
        })}
      </div>
    </AbsoluteFill>
  );
};

/** A 4-point twinkle star. */
const Star: React.FC<{ size: number; color: string }> = ({ size, color }) => (
  <svg width={size} height={size} viewBox="-10 -10 20 20" style={{ overflow: 'visible' }}>
    <path d="M0,-10 C1,-2 2,-1 10,0 C2,1 1,2 0,10 C-1,2 -2,1 -10,0 C-2,-1 -1,-2 0,-10 Z" fill={color} />
  </svg>
);

const seeded = (n: number) => {
  const x = Math.sin(n * 12.9898) * 43758.5453;
  return x - Math.floor(x);
};

/**
 * Sparkle captions: each line sits in a white sticker box with a dark border and a coloured offset shadow, pops in with a
 * small tilt; *keywords* get a yellow marker box; the spoken word grows; gold and white stars twinkle around the box.
 */
const SparkleCaptions: React.FC<{ captions: Caption[]; style: Style; accent: string }> = ({ captions, style, accent }) => {
  const frame = useCurrentFrame();
  const { fps, height } = useVideoConfig();
  const unit = useUnit();
  const t = frame / fps;
  const index = captions.findIndex((c) => t >= c.start && t < c.end);
  if (index < 0) {
    return null;
  }

  const active = captions[index];
  const words = keywords(active.text);
  const local = Math.round((t - active.start) * fps);
  const progress = (t - active.start) / Math.max(0.01, active.end - active.start);
  const current = Math.min(words.length - 1, Math.floor(progress * words.length));
  const pop = spring({ frame: local, fps, config: { damping: 11, mass: 0.55, stiffness: 170 } });
  const tilt = (index % 2 === 0 ? -1.6 : 1.6) * (0.4 + 0.6 * pop);
  const shadow = accent && accent.toUpperCase() !== '#FFFFFF' ? accent : '#FFE11A';

  // Stars around the box: positions are stable per caption line, each twinkles on its own phase.
  const stars = Array.from({ length: 6 }, (_, i) => {
    const r = seeded(index * 7.31 + i * 3.17);
    const side = i % 2 === 0 ? -1 : 1;
    const x = 50 + side * (44 + r * 8);            // % from left: just outside the left/right edge
    const y = [-18, 50, 112][Math.floor(i / 2)] + (r - 0.5) * 24; // % from top: above, middle, below
    const phase = seeded(i * 5.7 + index) * Math.PI * 2;
    const tw = 0.5 + 0.5 * Math.sin(t * 7 + phase);
    const size = (26 + r * 30) * unit * (0.55 + 0.45 * tw) * pop;
    const color = i % 3 === 1 ? '#FFFFFF' : '#FFD84A';
    return (
      <div key={i} style={{ position: 'absolute', left: `${x}%`, top: `${y}%`, transform: `translate(-50%, -50%) rotate(${t * 60 + i * 40}deg)`,
        opacity: 0.35 + 0.65 * tw, filter: `drop-shadow(0 0 ${10 * unit}px ${color})` }}>
        <Star size={size} color={color} />
      </div>
    );
  });

  return (
    <AbsoluteFill style={{ justifyContent: 'flex-end', alignItems: 'center', paddingBottom: height * 0.11 }}>
      <div style={{ position: 'relative', maxWidth: '86%', transform: `scale(${0.55 + 0.45 * pop}) rotate(${tilt}deg)` }}>
        <div style={{
          background: 'linear-gradient(180deg, #FFFFFF 0%, #FFF7DA 100%)', border: `${6 * unit}px solid #111111`, borderRadius: 26 * unit,
          boxShadow: `${10 * unit}px ${12 * unit}px 0 ${shadow}, 0 ${18 * unit}px ${30 * unit}px rgba(0,0,0,0.45)`,
          padding: `${14 * unit}px ${26 * unit}px`, textAlign: 'center', lineHeight: 1.3,
          fontFamily: `"Segoe UI Black", ${style.fontFamily}`, fontWeight: 900, fontSize: 58 * unit, textTransform: 'uppercase', color: '#111111',
        }}>
          {words.map((w, i) => {
            const on = i === current;
            return (
              <span key={i} style={{
                display: 'inline-block', margin: `${4 * unit}px ${13 * unit}px`, padding: w.hot ? `0 ${12 * unit}px` : 0,
                background: w.hot ? '#FFE11A' : 'transparent', borderRadius: 12 * unit,
                boxShadow: w.hot ? `inset 0 -${6 * unit}px 0 rgba(215,20,26,0.25)` : undefined,
                color: w.hot ? '#C8102E' : '#111111', transform: `scale(${on ? 1.12 : 1}) rotate(${w.hot ? -2 : 0}deg)`,
              }}>{w.word}</span>
            );
          })}
        </div>
        {stars}
      </div>
    </AbsoluteFill>
  );
};

/** Big headlines: heavy yellow letters with a thick red outline, popping in over the video. */
const Headlines: React.FC<{ headlines: Headline[]; style: Style }> = ({ headlines, style }) => {
  const frame = useCurrentFrame();
  const { fps, height } = useVideoConfig();
  const unit = useUnit();
  const t = frame / fps;
  const h = headlines.find((x) => t >= x.start && t < x.end);
  if (!h) {
    return null;
  }

  const local = Math.round((t - h.start) * fps);
  const pop = spring({ frame: local, fps, config: { damping: 9, mass: 0.7, stiffness: 160 } });
  const out = interpolate(t, [h.end - 0.2, h.end], [1, 0], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' });
  const letters: React.CSSProperties = {
    fontFamily: `"Segoe UI Black", ${style.fontFamily}`, fontWeight: 900, textTransform: 'uppercase', lineHeight: 1.05,
    WebkitTextStroke: `${12 * unit}px #D7141A`, paintOrder: 'stroke fill',
    textShadow: `0 ${8 * unit}px 0 #7A0A0D, 0 ${14 * unit}px ${24 * unit}px rgba(0,0,0,0.5)`,
  };
  return (
    <AbsoluteFill style={{ justifyContent: 'flex-start', alignItems: 'center', paddingTop: height * (style.captionStyle === 'sparkle' ? 0.09 : 0.2) }}>
      <div style={{ textAlign: 'center', maxWidth: '92%', opacity: out, transform: `scale(${0.4 + 0.6 * pop}) rotate(${(1 - pop) * -6}deg)` }}>
        <div style={{ ...letters, color: '#FFE11A', fontSize: 104 * unit }}>{h.text}</div>
        {h.sub ? (
          <div style={{ display: 'inline-block', marginTop: 18 * unit, background: '#D7141A', color: '#FFFFFF', borderRadius: 14 * unit,
            padding: `${8 * unit}px ${22 * unit}px`, fontFamily: style.fontFamily, fontWeight: 800, fontSize: 44 * unit, textTransform: 'uppercase' }}>{h.sub}</div>
        ) : null}
      </div>
    </AbsoluteFill>
  );
};

const LogoMark: React.FC<{ logo: Logo }> = ({ logo }) => {
  const unit = useUnit();
  const pad = 40 * unit;
  const pos: React.CSSProperties = {
    position: 'absolute', width: 190 * unit, height: 120 * unit, objectFit: 'contain',
    ...(logo.corner.includes('bottom') ? { bottom: pad } : { top: pad }),
    ...(logo.corner.includes('left') ? { left: pad } : { right: pad }),
    filter: `drop-shadow(0 ${4 * unit}px ${10 * unit}px rgba(0,0,0,0.45))`,
  };
  return <Img src={logo.src} style={pos} />;
};

/** Captions in the style's look: karaoke (words light up), box, pop (bouncy), plain. */
const Captions: React.FC<{ captions: Caption[]; style: Style; accent: string }> = ({ captions, style, accent }) => {
  const frame = useCurrentFrame();
  const { fps, height } = useVideoConfig();
  const unit = useUnit();
  const t = frame / fps;
  const active = captions.find((c) => t >= c.start && t < c.end);
  if (!active) {
    return null;
  }

  const words = active.text.split(/\s+/).filter(Boolean);
  const progress = (t - active.start) / Math.max(0.01, active.end - active.start);
  const lit = style.captionStyle === 'karaoke' ? Math.ceil(progress * words.length) : words.length;
  const enter = interpolate(t - active.start, [0, 0.2], [0, 1], { extrapolateRight: 'clamp' });
  const bounce = style.captionStyle === 'pop'
    ? spring({ frame: Math.round((t - active.start) * fps), fps, config: { damping: 9, mass: 0.6 } })
    : enter;
  const boxed = style.captionStyle !== 'plain';
  return (
    <AbsoluteFill style={{ justifyContent: 'flex-end', alignItems: 'center', paddingBottom: height * 0.14 }}>
      <div
        style={{
          maxWidth: '86%',
          textAlign: 'center',
          fontFamily: style.fontFamily,
          fontWeight: 800,
          fontSize: 58 * unit,
          lineHeight: 1.25,
          padding: `${14 * unit}px ${26 * unit}px`,
          borderRadius: 20 * unit,
          background: boxed ? (style.captionStyle === 'box' ? accent : 'rgba(10,12,18,0.62)') : 'transparent',
          textShadow: boxed ? 'none' : `0 ${3 * unit}px ${10 * unit}px rgba(0,0,0,0.8)`,
          opacity: enter,
          transform: `translateY(${(1 - enter) * 16}px) scale(${style.captionStyle === 'pop' ? 0.85 + 0.15 * bounce : 1})`,
        }}
      >
        {words.map((w, i) => {
          const on = i < lit;
          const color = style.captionStyle === 'box' ? style.background : on ? style.text : 'rgba(255,255,255,0.55)';
          return (
            <span key={i} style={{ color, textShadow: style.captionStyle === 'karaoke' && on ? `0 0 ${18 * unit}px ${accent}` : undefined }}>
              {w}{i < words.length - 1 ? ' ' : ''}
            </span>
          );
        })}
      </div>
    </AbsoluteFill>
  );
};

export const AutoEdit: React.FC<AutoEditProps> = (p) => {
  const { fps } = useVideoConfig();
  const style = p.style;
  const tf = Math.max(1, Math.round(p.transitionSeconds * fps));
  const scenes: { key: string; frames: number; node: React.ReactNode }[] = [];
  if (p.intro) {
    scenes.push({ key: 'intro', frames: Math.round(p.intro.seconds * fps), node: <Intro title={p.intro.title} subtitle={p.intro.subtitle} style={style} accent={p.accentColor} /> });
  }
  const cuts: { frame: number; fx: string; index: number }[] = [];
  let at = scenes.reduce((n, s) => n + s.frames, 0) - (p.transitionSeconds > 0 ? scenes.length * tf : 0);
  p.clips.forEach((clip, i) => {
    const frames = Math.round(clip.duration * fps);
    if (i > 0 && p.transitionSeconds > 0) at -= tf;
    if (clip.fx && i > 0) cuts.push({ frame: at, fx: clip.fx, index: i });
    scenes.push({ key: `clip-${i}`, frames, node: <ClipScene clip={clip} fps={fps} frames={frames} index={i} zoom={style.zoomAmount} nextFx={p.clips[i + 1]?.fx} /> });
    at += frames;
  });
  (p.overlays ?? []).forEach((o, i) => {
    if (!o.fx) return;
    cuts.push({ frame: Math.round(o.start * fps), fx: o.fx, index: i });
    if (o.fx === 'whip') cuts.push({ frame: Math.round((o.start + o.duration) * fps), fx: 'whip', index: i + 1 });
  });
  if (p.outro) {
    scenes.push({ key: 'outro', frames: Math.round(p.outro.seconds * fps), node: <Outro text={p.outro.text} style={style} accent={p.accentColor} /> });
  }

  const transitionFor = (i: number) =>
    style.transition === 'slide' || (style.transition === 'mix' && i % 3 === 0) ? slide({ direction: 'from-right' }) : fade();

  return (
    <AbsoluteFill style={{ backgroundColor: '#000000' }}>
      <TransitionSeries>
        {scenes.map((s, i) => (
          <React.Fragment key={s.key}>
            {i > 0 && p.transitionSeconds > 0 ? <TransitionSeries.Transition presentation={transitionFor(i)} timing={linearTiming({ durationInFrames: tf })} /> : null}
            <TransitionSeries.Sequence durationInFrames={s.frames}>{s.node}</TransitionSeries.Sequence>
          </React.Fragment>
        ))}
      </TransitionSeries>
      {(p.overlays ?? []).map((o, i) => {
        const frames = Math.max(1, Math.round(o.duration * fps));
        return (
          <Sequence key={`overlay-${i}`} from={Math.round(o.start * fps)} durationInFrames={frames} layout="none">
            <OverlayScene overlay={o} fps={fps} frames={frames} index={i} />
          </Sequence>
        );
      })}
      {cuts.length ? <CutFx cuts={cuts} /> : null}
      {style.captionStyle === 'tiktok'
        ? <TikTokCaptions captions={p.captions} style={style} accent={p.accentColor} />
        : style.captionStyle === 'sparkle'
          ? <SparkleCaptions captions={p.captions} style={style} accent={p.accentColor} />
          : <Captions captions={p.captions} style={style} accent={p.accentColor} />}
      {p.headlines?.length ? <Headlines headlines={p.headlines} style={style} /> : null}
      {p.logo ? <LogoMark logo={p.logo} /> : null}
      {p.music ? <Audio src={p.music.src} volume={p.voiceover ? p.music.volume * 0.35 : p.music.volume} /> : null}
      {p.voiceover ? <Audio src={p.voiceover.src} volume={p.voiceover.volume} /> : null}
    </AbsoluteFill>
  );
};
