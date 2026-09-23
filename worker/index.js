const MAX_BYTES = 10 * 1024 * 1024;
const RANDOM_LENGTH = 16; // 62^16 ≈ 2^95 — not worth guessing at
const ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
const CUTOFF = 256 - (256 % ALPHABET.length); // reject above this to keep it uniform
const KEY_PATTERN = /^[0-9A-Za-z]{16,64}$/;
const THUMB_PREFIX = "thumb/";
const COOKIE = "snipjar_session";
const PAGE = 24;

// R2 only lists lexicographically, so the key carries an inverted timestamp:
// sorting the keys ascending gives newest first, and no separate index is needed.
// The 16 random characters are what make a key unguessable.
function newKey() {
  const stamp = String(9999999999999 - Date.now()); // always 13 digits
  let random = "";
  while (random.length < RANDOM_LENGTH) {
    const raw = crypto.getRandomValues(new Uint8Array(RANDOM_LENGTH));
    for (const byte of raw) {
      if (byte >= CUTOFF) continue;
      random += ALPHABET[byte % ALPHABET.length];
      if (random.length === RANDOM_LENGTH) break;
    }
  }
  return stamp + random;
}

function takenAt(key) {
  const stamp = Number(key.slice(0, 13));
  if (!Number.isFinite(stamp)) return null;
  const when = 9999999999999 - stamp;
  return when > 0 && when < 4102444800000 ? new Date(when).toISOString() : null;
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

function text(body, status, extra) {
  const headers = { "content-type": "text/plain; charset=utf-8" };
  return new Response(body, { status: status, headers: Object.assign(headers, extra || {}) });
}

function json(value, status, extra) {
  const headers = { "content-type": "application/json; charset=utf-8" };
  return new Response(JSON.stringify(value), {
    status: status || 200,
    headers: Object.assign(headers, extra || {}),
  });
}

function bearer(request, env) {
  const auth = request.headers.get("authorization") || "";
  return auth.startsWith("Bearer ") && tokenMatches(auth.slice(7), env.UPLOAD_TOKEN);
}

function signedIn(request, env) {
  const jar = request.headers.get("cookie") || "";
  for (const part of jar.split(";")) {
    const [name, ...rest] = part.trim().split("=");
    if (name !== COOKIE) continue;
    try {
      return tokenMatches(decodeURIComponent(rest.join("=")), env.UPLOAD_TOKEN);
    } catch {
      return false;
    }
  }
  return false;
}

function isPng(bytes) {
  const sig = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  if (bytes.byteLength < sig.length) return false;
  return sig.every((b, i) => bytes[i] === b);
}

function isJpeg(bytes) {
  return bytes.byteLength > 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff;
}

async function readBody(request) {
  const declared = Number(request.headers.get("content-length"));
  if (declared > MAX_BYTES) return { tooLarge: true };
  const bytes = new Uint8Array(await request.arrayBuffer());
  if (bytes.byteLength > MAX_BYTES) return { tooLarge: true };
  return { bytes: bytes };
}

async function upload(request, env, url) {
  if (!env.UPLOAD_TOKEN) return text("server not configured\n", 500);
  if (!bearer(request, env)) return text("unauthorized\n", 401);

  const type = (request.headers.get("content-type") || "").split(";")[0].trim();
  if (type !== "image/png") return text("image/png only\n", 415);

  const body = await readBody(request);
  if (body.tooLarge) return text("too large\n", 413);
  if (body.bytes.byteLength === 0) return text("empty body\n", 400);
  if (!isPng(body.bytes)) return text("not a png\n", 415);

  const key = newKey();
  await env.SHOTS.put(key, body.bytes, { httpMetadata: { contentType: "image/png" } });

  // Key first, URL second: the client reads line 1 to post the thumbnail.
  return text(key + "\n" + url.origin + "/" + key + ".png\n", 201);
}

async function uploadThumb(request, env, key) {
  if (!env.UPLOAD_TOKEN) return text("server not configured\n", 500);
  if (!bearer(request, env)) return text("unauthorized\n", 401);

  const body = await readBody(request);
  if (body.tooLarge) return text("too large\n", 413);
  if (!isJpeg(body.bytes)) return text("not a jpeg\n", 415);

  const original = await env.SHOTS.head(key);
  if (!original) return text("no such shot\n", 404);

  await env.SHOTS.put(THUMB_PREFIX + key, body.bytes, {
    httpMetadata: { contentType: "image/jpeg" },
  });
  return text("ok\n", 201);
}

async function serve(storageKey, contentType, downloadName, env, method) {
  const object =
    method === "HEAD" ? await env.SHOTS.head(storageKey) : await env.SHOTS.get(storageKey);
  if (!object) return text("not found\n", 404);

  const headers = new Headers();
  headers.set("content-type", contentType);
  headers.set("content-length", String(object.size));
  headers.set("cache-control", "public, max-age=31536000, immutable");
  headers.set("x-content-type-options", "nosniff");
  headers.set("content-disposition", 'inline; filename="' + downloadName + '"');
  headers.set("etag", object.httpEtag);
  if (method === "HEAD") return new Response(null, { headers: headers });
  return new Response(object.body, { headers: headers });
}

async function list(request, env, url) {
  const cursor = url.searchParams.get("cursor") || undefined;

  // Shot keys never contain "/", thumbnails all live under "thumb/": listing with
  // a delimiter rolls the thumbnails up out of the way instead of interleaving them.
  const page = await env.SHOTS.list({ limit: PAGE, cursor: cursor, delimiter: "/" });

  const items = page.objects.map((object) => ({
    key: object.key,
    size: object.size,
    taken: takenAt(object.key) || object.uploaded.toISOString(),
    url: url.origin + "/" + object.key + ".png",
    thumb: url.origin + "/t/" + object.key + ".jpg",
  }));

  return json({
    items: items,
    cursor: page.truncated ? page.cursor : null,
  });
}

async function remove(request, env) {
  let body;
  try {
    body = await request.json();
  } catch {
    return text("bad request\n", 400);
  }
  const key = body && body.key;
  if (typeof key !== "string" || !KEY_PATTERN.test(key)) return text("bad key\n", 400);

  await env.SHOTS.delete(key);
  await env.SHOTS.delete(THUMB_PREFIX + key);
  return json({ deleted: key });
}

async function signIn(request, env, url) {
  const form = await request.formData();
  const given = form.get("token");
  if (typeof given !== "string" || !tokenMatches(given, env.UPLOAD_TOKEN)) {
    return new Response(loginPage("トークンが違います"), {
      status: 401,
      headers: { "content-type": "text/html; charset=utf-8" },
    });
  }

  const secure = url.protocol === "https:" ? " Secure;" : "";
  return new Response(null, {
    status: 303,
    headers: {
      location: "/gallery",
      "set-cookie":
        COOKIE +
        "=" +
        encodeURIComponent(given) +
        "; Path=/;" +
        secure +
        " HttpOnly; SameSite=Strict; Max-Age=31536000",
    },
  });
}

function html(body) {
  return new Response(body, { headers: { "content-type": "text/html; charset=utf-8" } });
}

const STYLE = `
:root { color-scheme: light dark; --bg:#f6f7f9; --card:#fff; --ink:#1b1d21; --sub:#6b7180;
        --line:#dfe2e8; --accent:#2f6fed; --danger:#c8412f; }
@media (prefers-color-scheme: dark) {
  :root { --bg:#131417; --card:#1c1e22; --ink:#e9eaee; --sub:#9aa0ac; --line:#2c2f36;
          --accent:#6aa4ff; --danger:#ef7a68; }
}
* { box-sizing: border-box; }
body { margin:0; background:var(--bg); color:var(--ink); font:15px/1.6 "Yu Gothic UI",
       -apple-system, "Segoe UI", system-ui, sans-serif; }
header { padding:22px 16px 10px; max-width:1180px; margin:0 auto; }
h1 { font-size:19px; margin:0; letter-spacing:.02em; }
.count { color:var(--sub); font-size:13px; margin-top:4px; }
main { max-width:1180px; margin:0 auto; padding:10px 16px 60px; }
.grid { display:grid; gap:14px; grid-template-columns:repeat(auto-fill, minmax(230px, 1fr)); }
.tile { background:var(--card); border:1px solid var(--line); border-radius:10px;
        overflow:hidden; display:flex; flex-direction:column; }
.shot { display:block; width:100%; aspect-ratio:16/10; object-fit:cover; background:var(--bg);
        border-bottom:1px solid var(--line); }
.meta { padding:9px 11px; font-size:12px; color:var(--sub);
        display:flex; justify-content:space-between; gap:8px; }
.acts { display:flex; border-top:1px solid var(--line); }
.acts button { flex:1; border:0; background:none; color:var(--accent); font:inherit;
               font-size:13px; padding:9px 0; cursor:pointer; }
.acts button + button { border-left:1px solid var(--line); color:var(--danger); }
.acts button:hover { background:rgba(127,127,127,.09); }
.more { display:block; margin:26px auto 0; padding:10px 22px; font:inherit;
        background:var(--card); color:var(--ink); border:1px solid var(--line);
        border-radius:8px; cursor:pointer; }
.empty { color:var(--sub); padding:40px 0; text-align:center; }
form.login { max-width:380px; margin:14vh auto; padding:0 16px; }
form.login input { width:100%; padding:11px 12px; font:inherit; border-radius:8px;
                   border:1px solid var(--line); background:var(--card); color:var(--ink); }
form.login button { width:100%; margin-top:10px; padding:11px; font:inherit; border:0;
                    border-radius:8px; background:var(--accent); color:#fff; cursor:pointer; }
.error { color:var(--danger); font-size:13px; margin-bottom:8px; }
.note { color:var(--sub); font-size:12px; margin-top:12px; }
@media (max-width:560px) { .grid { grid-template-columns:repeat(auto-fill, minmax(150px,1fr)); } }
`;

function loginPage(error) {
  return `<!doctype html><html lang="ja"><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Snipjar</title><link rel="icon" href="data:image/svg+xml,%3Csvg%20xmlns='http://www.w3.org/2000/svg'%20viewBox='0%200%20256%20256'%20width='256'%20height='256'%3E%20%3Ctitle%3ESnipjar%3C/title%3E%20%3Crect%20x='8'%20y='8'%20width='240'%20height='240'%20rx='58'%20fill='%231b1d21'/%3E%20%3Cg%20fill='none'%20stroke='%23f2f4f7'%20stroke-width='19'%20stroke-linecap='round'%20stroke-linejoin='round'%3E%20%3Cpath%20d='M62%2096V62h34'/%3E%20%3Cpath%20d='M194%2096V62h-34'/%3E%20%3Cpath%20d='M62%20160v34h34'/%3E%20%3Cpath%20d='M194%20160v34h-34'/%3E%20%3C/g%3E%20%3Crect%20x='99'%20y='99'%20width='58'%20height='58'%20rx='13'%20fill='%235aa9ff'/%3E%20%3C/svg%3E"><style>${STYLE}</style>
<form class="login" method="post" action="/gallery/auth">
  <h1>Snipjar</h1>
  ${error ? `<p class="error">${error}</p>` : ""}
  <input type="password" name="token" placeholder="アップロード用トークン" autofocus required>
  <button type="submit">開く</button>
  <p class="note">config.ini の token と同じ値。この端末にだけ保存されます。</p>
</form>`;
}

function galleryPage() {
  return `<!doctype html><html lang="ja"><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Snipjar</title><link rel="icon" href="data:image/svg+xml,%3Csvg%20xmlns='http://www.w3.org/2000/svg'%20viewBox='0%200%20256%20256'%20width='256'%20height='256'%3E%20%3Ctitle%3ESnipjar%3C/title%3E%20%3Crect%20x='8'%20y='8'%20width='240'%20height='240'%20rx='58'%20fill='%231b1d21'/%3E%20%3Cg%20fill='none'%20stroke='%23f2f4f7'%20stroke-width='19'%20stroke-linecap='round'%20stroke-linejoin='round'%3E%20%3Cpath%20d='M62%2096V62h34'/%3E%20%3Cpath%20d='M194%2096V62h-34'/%3E%20%3Cpath%20d='M62%20160v34h34'/%3E%20%3Cpath%20d='M194%20160v34h-34'/%3E%20%3C/g%3E%20%3Crect%20x='99'%20y='99'%20width='58'%20height='58'%20rx='13'%20fill='%235aa9ff'/%3E%20%3C/svg%3E"><style>${STYLE}</style>
<header><h1>Snipjar</h1><div class="count" id="count">読み込み中…</div></header>
<main><div class="grid" id="grid"></div><div id="tail"></div></main>
<script>
const grid = document.getElementById('grid');
const tail = document.getElementById('tail');
const count = document.getElementById('count');
let cursor = null, loaded = 0, first = true;

// navigator.clipboard needs a focused document and a granted permission, and it is
// refused often enough (embedded views, older browsers) to need the old path too.
async function copyText(text) {
  try { await navigator.clipboard.writeText(text); return true; } catch (e) {}
  try {
    const box = document.createElement('textarea');
    box.value = text;
    box.setAttribute('readonly', '');
    box.style.cssText = 'position:fixed;top:-1000px;opacity:0';
    document.body.appendChild(box);
    box.select();
    box.setSelectionRange(0, text.length);
    const ok = document.execCommand('copy');
    box.remove();
    return ok;
  } catch (e) { return false; }
}

const when = iso => new Date(iso).toLocaleString('ja-JP',
  { month:'numeric', day:'numeric', hour:'2-digit', minute:'2-digit' });
const size = n => n > 1048576 ? (n/1048576).toFixed(1)+' MB' : Math.round(n/1024)+' KB';

function tile(item) {
  const el = document.createElement('div');
  el.className = 'tile';
  const img = document.createElement('img');
  img.className = 'shot'; img.loading = 'lazy'; img.src = item.thumb; img.alt = '';
  img.onerror = () => { img.onerror = null; img.src = item.url; };  // サムネ未作成なら原寸
  img.onclick = () => window.open(item.url, '_blank', 'noopener');
  const meta = document.createElement('div');
  meta.className = 'meta';
  meta.innerHTML = '<span>' + when(item.taken) + '</span><span>' + size(item.size) + '</span>';
  const acts = document.createElement('div');
  acts.className = 'acts';
  const copy = document.createElement('button');
  copy.textContent = 'URL をコピー';
  copy.onclick = async () => {
    copy.textContent = await copyText(item.url) ? 'コピーしました' : 'コピーできません';
    setTimeout(() => { copy.textContent = 'URL をコピー'; }, 1400);
  };
  const del = document.createElement('button');
  del.textContent = '削除';
  del.onclick = async () => {
    if (!confirm('この1枚を削除します。元に戻せません。')) return;
    del.disabled = true;
    const res = await fetch('/gallery/api/delete', {
      method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ key: item.key }),
    });
    if (res.ok) { el.remove(); loaded--; say(); } else { del.disabled = false; del.textContent = '失敗'; }
  };
  acts.append(copy, del);
  el.append(img, meta, acts);
  return el;
}

function say() { count.textContent = loaded + ' 枚' + (cursor ? '（続きあり）' : ''); }

async function load() {
  tail.textContent = '';
  const res = await fetch('/gallery/api/list' + (cursor ? '?cursor=' + encodeURIComponent(cursor) : ''));
  if (!res.ok) { location.href = '/gallery'; return; }
  const page = await res.json();
  for (const item of page.items) { grid.append(tile(item)); loaded++; }
  cursor = page.cursor;
  if (first && loaded === 0) grid.innerHTML = '<p class="empty">まだ1枚もありません。</p>';
  first = false;
  say();
  if (cursor) {
    const more = document.createElement('button');
    more.className = 'more'; more.textContent = 'もっと見る';
    more.onclick = () => { more.disabled = true; load(); };
    tail.append(more);
  }
}
load();
</script>`;
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const path = url.pathname;
    const method = request.method;

    if (path === "/upload") {
      if (method !== "POST") return text("method not allowed\n", 405);
      return upload(request, env, url);
    }

    const thumbUpload = path.match(/^\/upload\/thumb\/([0-9A-Za-z]{16,64})$/);
    if (thumbUpload) {
      if (method !== "POST") return text("method not allowed\n", 405);
      return uploadThumb(request, env, thumbUpload[1]);
    }

    if (path === "/gallery/auth") {
      if (method !== "POST") return text("method not allowed\n", 405);
      return signIn(request, env, url);
    }

    if (path === "/gallery" || path.startsWith("/gallery/")) {
      if (!signedIn(request, env)) {
        if (path === "/gallery") return html(loginPage(null));
        return text("unauthorized\n", 401);
      }
      if (path === "/gallery") return html(galleryPage());
      if (path === "/gallery/api/list" && method === "GET") return list(request, env, url);
      if (path === "/gallery/api/delete" && method === "POST") return remove(request, env);
      return text("not found\n", 404);
    }

    if (method === "GET" || method === "HEAD") {
      const thumb = path.match(/^\/t\/([0-9A-Za-z]{16,64})(?:\.jpg)?$/);
      if (thumb) return serve(THUMB_PREFIX + thumb[1], "image/jpeg", thumb[1] + ".jpg", env, method);

      const shot = path.match(/^\/([0-9A-Za-z]{16,64})(?:\.png)?$/);
      if (shot) return serve(shot[1], "image/png", shot[1] + ".png", env, method);

      if (path === "/") return text("snipjar\n", 200);
    }

    return text("not found\n", 404);
  },
};
