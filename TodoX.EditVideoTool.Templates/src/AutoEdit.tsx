import React from 'react';
import {
  AbsoluteFill,
  Audio,
  OffthreadVideo,
  interpolate,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from 'remotion';
import { TransitionSeries, linearTiming } from '@remotion/transitions';
import { fade } from '@remotion/transitions/fade';
import { slide } from '@remotion/transitions/slide';
import type { AutoEditProps, Caption, Clip, Style } from './types';

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

/** A clip scene with a slow push-in/out so still-ish footage keeps moving. */
const ClipScene: React.FC<{ clip: Clip; fps: number; frames: number; index: number; zoom: number }> = ({ clip, fps, frames, index, zoom }) => {
  const frame = useCurrentFrame();
  const zoomIn = index % 2 === 0;
  const scale = interpolate(frame, [0, frames], zoomIn ? [1, 1 + zoom] : [1 + zoom, 1], { extrapolateRight: 'clamp' });
  return (
    <AbsoluteFill style={{ backgroundColor: '#000000', overflow: 'hidden' }}>
      <OffthreadVideo
        src={clip.src}
        startFrom={Math.round(clip.from * fps)}
        muted={!clip.keepAudio}
        style={{ width: '100%', height: '100%', objectFit: 'cover', transform: `scale(${scale})` }}
      />
    </AbsoluteFill>
  );
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
  p.clips.forEach((clip, i) => {
    const frames = Math.round(clip.duration * fps);
    scenes.push({ key: `clip-${i}`, frames, node: <ClipScene clip={clip} fps={fps} frames={frames} index={i} zoom={style.zoomAmount} /> });
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
            {i > 0 ? <TransitionSeries.Transition presentation={transitionFor(i)} timing={linearTiming({ durationInFrames: tf })} /> : null}
            <TransitionSeries.Sequence durationInFrames={s.frames}>{s.node}</TransitionSeries.Sequence>
          </React.Fragment>
        ))}
      </TransitionSeries>
      <Captions captions={p.captions} style={style} accent={p.accentColor} />
      {p.music ? <Audio src={p.music.src} volume={p.voiceover ? p.music.volume * 0.35 : p.music.volume} /> : null}
      {p.voiceover ? <Audio src={p.voiceover.src} volume={p.voiceover.volume} /> : null}
    </AbsoluteFill>
  );
};
