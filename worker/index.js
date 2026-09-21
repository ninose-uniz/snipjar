const MAX_BYTES = 10 * 1024 * 1024;
const ID_LENGTH = 22; // 62^22 ≈ 2^131 — not worth guessing at
const ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
const CUTOFF = 256 - (256 % ALPHABET.length); // reject above this to keep it uniform

function newId() {
  let out = "";
  while (out.length < ID_LENGTH) {
    const raw = crypto.getRandomValues(new Uint8Array(ID_LENGTH));
    for (const byte of raw) {
      if (byte >= CUTOFF) continue;
      out += ALPHABET[byte % ALPHABET.length];
      if (out.length === ID_LENGTH) break;
    }
  }
  return out;
}

// Length-independent compare so a wrong token leaks nothing through timing.
function tokenMatches(given, expected) {
  if (typeof given !== "string" || typeof expected !== "string") return false;
  const a = new TextEncoder().encode(given);
  const b = new TextEncoder().encode(expected);
  let diff = a.length ^ b.length;
  const len = Math.max(a.length, b.length);
  for (let i = 0; i < len; i++) diff |= (a[i] || 0) ^ (b[i] || 0);
  return diff === 0;
}

function text(body, status) {
  return new Response(body, {
    status: status,
    headers: { "content-type": "text/plain; charset=utf-8" },
  });
}

async function upload(request, env) {
  const expected = env.UPLOAD_TOKEN;
  if (!expected) return text("server not configured\n", 500);

  const auth = request.headers.get("authorization") || "";
  if (!auth.startsWith("Bearer ") || !tokenMatches(auth.slice(7), expected)) {
    return text("unauthorized\n", 401);
  }

  const type = (request.headers.get("content-type") || "").split(";")[0].trim();
  if (type !== "image/png") return text("image/png only\n", 415);

  const declared = Number(request.headers.get("content-length"));
  if (declared > MAX_BYTES) return text("too large\n", 413);

  const body = new Uint8Array(await request.arrayBuffer());
  if (body.byteLength === 0) return text("empty body\n", 400);
  if (body.byteLength > MAX_BYTES) return text("too large\n", 413);
  if (!isPng(body)) return text("not a png\n", 415);

  const id = newId();
  await env.SHOTS.put(id, body, {
    httpMetadata: { contentType: "image/png" },
  });

  const url = new URL(request.url);
  return text(url.origin + "/" + id + ".png\n", 201);
}

function isPng(bytes) {
  const sig = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  if (bytes.byteLength < sig.length) return false;
  return sig.every((b, i) => bytes[i] === b);
}

async function serve(id, env, method) {
  const object = method === "HEAD"
    ? await env.SHOTS.head(id)
    : await env.SHOTS.get(id);
  if (!object) return text("not found\n", 404);

  const headers = new Headers();
  headers.set("content-type", "image/png");
  headers.set("content-length", String(object.size));
  headers.set("cache-control", "public, max-age=31536000, immutable");
  headers.set("x-content-type-options", "nosniff");
  headers.set("content-disposition", 'inline; filename="' + id + '.png"');
  headers.set("etag", object.httpEtag);
  if (method === "HEAD") return new Response(null, { headers: headers });
  return new Response(object.body, { headers: headers });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const path = url.pathname;

    if (path === "/upload") {
      if (request.method !== "POST") return text("method not allowed\n", 405);
      return upload(request, env);
    }

    if (request.method === "GET" || request.method === "HEAD") {
      const match = path.match(/^\/([0-9A-Za-z]{16,32})(?:\.png)?$/);
      if (match) return serve(match[1], env, request.method);
      if (path === "/") return text("shotlink\n", 200);
    }

    return text("not found\n", 404);
  },
};
