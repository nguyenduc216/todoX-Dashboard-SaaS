// Edit decision list produced by todox-editvideo.exe (EditPlanner) and rendered by AutoEdit.
// Times are in seconds; the composition converts them to frames.

export type Clip = {
  /** URL served by render.mjs's local file server. */
  src: string;
  /** Offset into the source clip. */
  from: number;
  duration: number;
  /** Keep the clip's own sound (otherwise muted under voice/music). */
  keepAudio: boolean;
};

export type Caption = {
  text: string;
  start: number;
  end: number;
};

export type Style = {
  id: string;
  name: string;
  accent: string;
  background: string;
  text: string;
  fontFamily: string;
  /** karaoke | box | pop | plain */
  captionStyle: string;
  /** fade | slide | mix */
  transition: string;
  zoomAmount: number;
};

export const defaultStyle: Style = {
  id: 'todox',
  name: 'TodoX',
  accent: '#FFBE2E',
  background: '#0B0D10',
  text: '#FFFFFF',
  fontFamily: '"Segoe UI", "Helvetica Neue", Arial, sans-serif',
  captionStyle: 'karaoke',
  transition: 'mix',
  zoomAmount: 0.08,
};

export type AutoEditProps = {
  width: number;
  height: number;
  fps: number;
  clips: Clip[];
  transitionSeconds: number;
  intro: { title: string; subtitle: string; seconds: number } | null;
  outro: { text: string; seconds: number } | null;
  captions: Caption[];
  voiceover: { src: string; volume: number } | null;
  music: { src: string; volume: number } | null;
  accentColor: string;
  style: Style;
};

export const defaultProps: AutoEditProps = {
  width: 1080,
  height: 1920,
  fps: 30,
  clips: [],
  transitionSeconds: 0.5,
  intro: { title: 'TodoX', subtitle: 'TODO EXCELLENCE', seconds: 2.5 },
  outro: null,
  captions: [],
  voiceover: null,
  music: null,
  accentColor: '#FFBE2E',
  style: defaultStyle,
};

/** Total length in frames, accounting for transition overlaps between consecutive scenes. */
export const totalFrames = (p: AutoEditProps): number => {
  const scenes = sceneSeconds(p);
  const overlap = Math.max(0, scenes.length - 1) * p.transitionSeconds;
  const seconds = scenes.reduce((a, b) => a + b, 0) - overlap;
  return Math.max(1, Math.round(seconds * p.fps));
};

export const sceneSeconds = (p: AutoEditProps): number[] => [
  ...(p.intro ? [p.intro.seconds] : []),
  ...p.clips.map((c) => c.duration),
  ...(p.outro ? [p.outro.seconds] : []),
];
