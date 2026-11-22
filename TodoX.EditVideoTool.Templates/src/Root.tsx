import React from 'react';
import { Composition } from 'remotion';
import { AutoEdit } from './AutoEdit';
import { defaultProps, totalFrames, type AutoEditProps } from './types';
import { GeoLayer, geoTotalFrames } from './geo/GeoLayer';
import { defaultGeoProps, type GeoLayerProps } from './geo/types';

export const Root: React.FC = () => (
  <>
  <Composition
    id="AutoEdit"
    component={AutoEdit as unknown as React.FC<Record<string, unknown>>}
    defaultProps={defaultProps as unknown as Record<string, unknown>}
    width={defaultProps.width}
    height={defaultProps.height}
    fps={defaultProps.fps}
    durationInFrames={totalFrames(defaultProps)}
    calculateMetadata={({ props }) => {
      const p = props as unknown as AutoEditProps;
      return { width: p.width, height: p.height, fps: p.fps, durationInFrames: totalFrames(p) };
    }}
  />
  <Composition
    id="GeoLayer"
    component={GeoLayer as unknown as React.FC<Record<string, unknown>>}
    defaultProps={defaultGeoProps as unknown as Record<string, unknown>}
    width={defaultGeoProps.width}
    height={defaultGeoProps.height}
    fps={defaultGeoProps.fps}
    durationInFrames={geoTotalFrames(defaultGeoProps)}
    calculateMetadata={({ props }) => {
      const p = props as unknown as GeoLayerProps;
      return { width: p.width, height: p.height, fps: p.fps, durationInFrames: geoTotalFrames(p) };
    }}
  />
  </>
);
