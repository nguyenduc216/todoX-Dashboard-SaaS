// Renders the AutoEdit composition for todox-editvideo.exe.
//   node render.mjs <job.json>
// job.json: { "bundle": "<bundle dir>", "output": "<file.mp4>", "props": { ...AutoEditProps with local file paths in every `src` } }
// Local media is exposed to the headless browser through a loopback-only server that serves exactly the
// files named in the job. Progress is printed as one JSON object per line on stdout.
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { ensureBrowser, renderMedia, selectComposition } from '@remotion/renderer';

const emit = (obj) => process.stdout.write(JSON.stringify(obj) + '\n');

const MIME = {
  '.mp4': 'video/mp4', '.mov': 'video/quicktime', '.m4v': 'video/mp4', '.webm': 'video/webm', '.mkv': 'video/x-matroska',
  '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.m4a': 'audio/mp4', '.aac': 'audio/aac', '.ogg': 'audio/ogg',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.webp': 'image/webp',
};

/** Replace every `src` that is a local path with a URL on the loopback server. */
const mapMedia = (value, files) => {
  if (Array.isArray(value)) return value.map((v) => mapMedia(v, files));
  if (value && typeof value === 'object') {
    const out = {};
    for (const [k, v] of Object.entries(value)) {
      if (k === 'src' && typeof v === 'string' && !/^https?:/i.test(v)) {
        files.push(path.resolve(v));
        out[k] = `__MEDIA__/${files.length - 1}${path.extname(v).toLowerCase()}`;
      } else {
        out[k] = mapMedia(v, files);
      }
    }
    return out;
  }
  return value;
};

const serve = (files) =>
  new Promise((resolve) => {
    const server = http.createServer((req, res) => {
      const m = /^\/m\/(\d+)/.exec(req.url ?? '');
      const file = m ? files[Number(m[1])] : undefined;
      if (!file || !fs.existsSync(file)) {
        res.writeHead(404).end();
        return;
      }
      const size = fs.statSync(file).size;
      const type = MIME[path.extname(file).toLowerCase()] ?? 'application/octet-stream';
      const range = /bytes=(\d*)-(\d*)/.exec(req.headers.range ?? '');
      if (range) {
        const start = range[1] ? Number(range[1]) : 0;
        const end = range[2] ? Number(range[2]) : size - 1;
        res.writeHead(206, { 'Content-Type': type, 'Content-Length': end - start + 1, 'Content-Range': `bytes ${start}-${end}/${size}`, 'Accept-Ranges': 'bytes' });
        fs.createReadStream(file, { start, end }).pipe(res);
      } else {
        res.writeHead(200, { 'Content-Type': type, 'Content-Length': size, 'Accept-Ranges': 'bytes' });
        fs.createReadStream(file).pipe(res);
      }
    });
    server.listen(0, '127.0.0.1', () => resolve(server));
  });

const main = async () => {
  const job = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
  const files = [];
  const mapped = mapMedia(job.props, files);
  const server = await serve(files);
  const base = `http://127.0.0.1:${server.address().port}/m`;
  const inputProps = JSON.parse(JSON.stringify(mapped).replaceAll('__MEDIA__', base));

  try {
    emit({ type: 'stage', stage: 'browser' });
    await ensureBrowser({ onBrowserDownload: () => ({ onProgress: ({ percent }) => emit({ type: 'browser_download', percent }) }) });

    emit({ type: 'stage', stage: 'compose' });
    const composition = await selectComposition({ serveUrl: job.bundle, id: 'AutoEdit', inputProps });

    emit({ type: 'stage', stage: 'render', frames: composition.durationInFrames, width: composition.width, height: composition.height });
    let last = -1;
    await renderMedia({
      composition,
      serveUrl: job.bundle,
      codec: 'h264',
      outputLocation: job.output,
      inputProps,
      crf: job.crf ?? 20,
      onProgress: ({ progress, renderedFrames, encodedFrames }) => {
        const pct = Math.floor(progress * 100);
        if (pct !== last) {
          last = pct;
          emit({ type: 'progress', percent: pct, renderedFrames, encodedFrames });
        }
      },
    });
    emit({ type: 'done', output: job.output, seconds: composition.durationInFrames / composition.fps });
  } finally {
    server.close();
  }
};

main().catch((err) => {
  emit({ type: 'error', message: String(err?.message ?? err) });
  process.exit(1);
});
