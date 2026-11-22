// Real-estate location video ("geo layer") drawn on TodoX's self-hosted map. Built by todox-editvideo.exe
// (todox_geo_video) and rendered by the GeoLayer composition.

export type LonLat = [number, number];

export type PoiKind = 'metro' | 'school' | 'uni' | 'market' | 'health' | 'park' | 'mall' | 'bank' | 'star';

export type GeoPlot = {
  /** Short id shown on the label, e.g. "A". */
  id: string;
  /** Land use, e.g. "Đất ở liền kề (ODT)". */
  name: string;
  /** Area text, e.g. "8,1 ha" or "120 m²". */
  area: string;
  /** Facing, e.g. "Hướng Đông Nam". */
  direction: string;
  color: string;
  ring: LonLat[];
};

export type GeoPoi = { kind: PoiKind; name: string; position: LonLat; distance: string };

export type GeoRoute = { name: string; coords: LonLat[]; distance: string; duration: string; color: string };

export type GeoLayerProps = {
  width: number;
  height: number;
  fps: number;
  /** MapLibre style URL (TodoX map: https://dashboard.todox.vn/maps/styles/todox-dark.json). */
  styleUrl: string;
  accent: string;
  kicker: string;
  title: string;
  /** Place line under the title and on the site label, e.g. "Phước Long · TP. Thủ Đức". */
  place: string;
  site: { name: string; position: LonLat };
  plots: GeoPlot[];
  pois: GeoPoi[];
  routes: GeoRoute[];
  headings: { plots: string; pois: string; routes: string };
  /** Shown at the bottom while plots are on screen, e.g. "Số liệu quy hoạch tham khảo". */
  plotNote: string;
  endCard: { title: string; subtitle: string; rows: [string, string][]; hotline: string } | null;
  attribution: string;
};

export const defaultGeoProps: GeoLayerProps = {
  width: 1080,
  height: 1920,
  fps: 30,
  styleUrl: 'https://dashboard.todox.vn/maps/styles/todox-dark.json',
  accent: '#FFC83D',
  kicker: 'VỊ TRÍ DỰ ÁN',
  title: 'VỊ TRÍ\nĐẮC ĐỊA',
  place: 'Phước Long · TP. Thủ Đức',
  site: { name: 'DỰ ÁN', position: [106.765, 10.817] },
  plots: [],
  pois: [],
  routes: [],
  headings: { plots: 'QUY HOẠCH\nPHÂN LÔ', pois: 'TIỆN ÍCH\nXUNG QUANH', routes: 'KẾT NỐI\nGIAO THÔNG' },
  plotNote: '',
  endCard: null,
  attribution: 'Bản đồ TodoX · © OpenStreetMap contributors',
};
