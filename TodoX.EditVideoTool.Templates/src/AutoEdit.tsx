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
import type { AutoEditProps, Caption, Clip } from './types';

const FONT = '"Segoe UI", "Helvetica Neue", Arial, sans-serif';

const Intro: React.FC<{ title: string; subtitle: string; accent: string }> = ({ title, subtitle, accent }) => {
  const frame = useCurrentFrame();
  const { fps, width, height } = useVideoConfig();
  const pop = spring({ frame, fps, config: { damping: 14, mass: 0.8 } });
  const bar = interpolate(frame, [4, 18], [0, 1], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' });
  const sub = interpolate(frame, [12, 24], [0, 1], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' });
  const unit = Math.min(width, height) / 1080;
  return (
    <AbsoluteFill style={{ backgroundColor: '#0F1117', justifyContent: 'center', alignItems: 'center', fontFamily: FONT }}>
      <div style={{ transform: `scale(${0.8 + 0.2 * pop})`, opacity: pop, textAlign: 'center', padding: 60 * unit }}>
        <div style={{ color: '#FFFFFF', fontSize: 96 * unit, fontWeight: 800, lineHeight: 1.1 }}>{title}</div>
        <div style={{ height: 10 * unit, width: `${bar * 60}%`, margin: `${28 * unit}px auto`, background: accent, borderRadius: 99 }} />
        <div style={{ color: '#C9CEDB', fontSize: 44 * unit, opacity: sub, transform: `translateY(${(1 - sub) * 20}px)` }}>{subtitle}</div>
      </div>
    </AbsoluteFill>
  );
};

const Outro: React.FC<{ text: string; accent: string }> = ({ text, accent }) => {
  const frame = useCurrentFrame();
  const { width, height } = useVideoConfig();
  const unit = Math.min(width, height) / 1080;
  const o = interpolate(frame, [0, 12], [0, 1], { extrapolateRight: 'clamp' });
  return (
    <AbsoluteFill style={{ backgroundColor: accent, justifyContent: 'center', alignItems: 'center', fontFamily: FONT }}>
      <div style={{ color: '#FFFFFF', fontSize: 72 * unit, fontWeight: 800, opacity: o, textAlign: 'center', padding: 80 * unit }}>{text}</div>
    </AbsoluteFill>
  );
};

/** A clip scene with a slow push-in so still-ish footage keeps moving. */
const ClipScene: React.FC<{ clip: Clip; fps: number; frames: number; index: number }> = ({ clip, fps, frames, index }) => {
  const frame = useCurrentFrame();
  const zoomIn = index % 2 === 0;
  const scale = interpolate(frame, [0, frames], zoomIn ? [1, 1.08] : [1.08, 1], { extrapolateRight: 'clamp' });
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

/** Karaoke-style captions: the active line, with words lighting up as it progresses. */
const Captions: React.FC<{ captions: Caption[]; accent: string }> = ({ captions, accent }) => {
  const frame = useCurrentFrame();
  const { fps, width, height } = useVideoConfig();
  const t = frame / fps;
  const active = captions.find((c) => t >= c.start && t < c.end);
  if (!active) {
    return null;
  }

  const unit = Math.min(width, height) / 1080;
  const words = active.text.split(/\s+/).filter(Boolean);
  const progress = (t - active.start) / Math.max(0.01, active.end - active.start);
  const lit = Math.ceil(progress * words.length);
  const enter = interpolate(t - active.start, [0, 0.2], [0, 1], { extrapolateRight: 'clamp' });
  return (
    <AbsoluteFill style={{ justifyContent: 'flex-end', alignItems: 'center', paddingBottom: height * 0.14 }}>
      <div
        style={{
          maxWidth: '86%',
          textAlign: 'center',
          fontFamily: FONT,
          fontWeight: 800,
          fontSize: 58 * unit,
          lineHeight: 1.25,
          padding: `${14 * unit}px ${26 * unit}px`,
          borderRadius: 20 * unit,
          background: 'rgba(10,12,18,0.62)',
          opacity: enter,
          transform: `translateY(${(1 - enter) * 16}px)`,
        }}
      >
        {words.map((w, i) => (
          <span key={i} style={{ color: i < lit ? '#FFFFFF' : 'rgba(255,255,255,0.55)', textShadow: i < lit ? `0 0 ${18 * unit}px ${accent}` : 'none' }}>
            {w}{i < words.length - 1 ? ' ' : ''}
          </span>
        ))}
      </div>
    </AbsoluteFill>
  );
};

export const AutoEdit: React.FC<AutoEditProps> = (p) => {
  const { fps } = useVideoConfig();
  const tf = Math.max(1, Math.round(p.transitionSeconds * fps));
  const scenes: { key: string; frames: number; node: React.ReactNode }[] = [];
  if (p.intro) {
    scenes.push({ key: 'intro', frames: Math.round(p.intro.seconds * fps), node: <Intro title={p.intro.title} subtitle={p.intro.subtitle} accent={p.accentColor} /> });
  }
  p.clips.forEach((clip, i) => {
    const frames = Math.round(clip.duration * fps);
    scenes.push({ key: `clip-${i}`, frames, node: <ClipScene clip={clip} fps={fps} frames={frames} index={i} /> });
  });
  if (p.outro) {
    scenes.push({ key: 'outro', frames: Math.round(p.outro.seconds * fps), node: <Outro text={p.outro.text} accent={p.accentColor} /> });
  }

  return (
    <AbsoluteFill style={{ backgroundColor: '#000000' }}>
      <TransitionSeries>
        {scenes.map((s, i) => (
          <React.Fragment key={s.key}>
            {i > 0 ? (
              <TransitionSeries.Transition
                presentation={i % 3 === 0 ? slide({ direction: 'from-right' }) : fade()}
                timing={linearTiming({ durationInFrames: tf })}
              />
            ) : null}
            <TransitionSeries.Sequence durationInFrames={s.frames}>{s.node}</TransitionSeries.Sequence>
          </React.Fragment>
        ))}
      </TransitionSeries>
      <Captions captions={p.captions} accent={p.accentColor} />
      {p.music ? <Audio src={p.music.src} volume={p.voiceover ? p.music.volume * 0.35 : p.music.volume} /> : null}
      {p.voiceover ? <Audio src={p.voiceover.src} volume={p.voiceover.volume} /> : null}
    </AbsoluteFill>
  );
};
