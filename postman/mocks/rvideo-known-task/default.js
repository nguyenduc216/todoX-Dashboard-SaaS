// Contract limited to Ai79TaskClient.GetStatusAsync: POST /video with videoId,
// one Processing status (1), then SUCCESS with the same id_base and an output URL.
// Every submission endpoint is deliberately unsupported and counted as a test failure.
const http = require('http');
const counts = new Map();
let submits = 0;
http.createServer(async (req, res) => {
  const json = value => { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(value)); };
  if (req.method === 'POST' && req.url === '/reset') { counts.clear(); submits = 0; return json({ reset: true }); }
  if (req.method === 'POST' && req.url === '/video') {
    const chunks = [];
    if (!req.body) for await (const chunk of req) chunks.push(chunk);
    const raw = req.body || Buffer.concat(chunks).toString();
    const form = typeof raw === 'string' || Buffer.isBuffer(raw)
      ? Object.fromEntries(new URLSearchParams(raw.toString())) : raw;
    const id = form.videoId;
    if (!id || !/^existing-base-\d+$/.test(id)) { res.statusCode = 400; return json({ error: 'wrong persisted videoId' }); }
    const count = (counts.get(id) || 0) + 1; counts.set(id, count);
    const host = req.headers.host;
    return json({ videoInfo: { id_base: id, status: count === 1 ? 1 : 'SUCCESS',
      download_url: count === 1 ? null : `http://${host}/provider/assets/${id}.mp4` } });
  }
  if (req.method === 'GET' && req.url.startsWith('/assets/')) {
    res.setHeader('Content-Type', 'video/mp4');
    return res.end(Buffer.from('000000186674797069736f6d0000020069736f6d6d703432000000086d646174', 'hex'));
  }
  if (req.method === 'GET' && req.url === '/metrics') return json({ polls: Object.fromEntries(counts), submits });
  submits++; res.statusCode = 501; json({ error: 'Submission is forbidden in this poll-only simulation' });
}).listen(process.env.PORT || 4500);
