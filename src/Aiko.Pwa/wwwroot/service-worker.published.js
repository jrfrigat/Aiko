self.importScripts('./service-worker-assets.js');

const cacheNamePrefix = 'aiko-cache-';
// The app shell is asked of the daemon first, so an updated daemon is picked up by the next reload instead of
// being answered from a cache that still holds the previous client. Everything else stays cache-first: those
// files are content-hashed, so a cached copy is the right copy for the page that asked for it.
const shellPaths = ['/index.html', '/_framework/blazor.webassembly.js'];

self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

async function onInstall() {
    const assetsManifest = self.assetsManifest;
    const cache = await caches.open(cacheNamePrefix + assetsManifest.version);
    const assets = assetsManifest.assets
        .filter(asset => /\.(?:dll|pdb|wasm|html|js|json|css|woff2|svg)$/.test(asset.url))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await cache.addAll(assets);
    // A new worker takes over at once, so the next reload serves the new client instead of waiting for every
    // tab to be closed - which is what made an updated daemon look like it had changed nothing.
    await self.skipWaiting();
}

async function onActivate() {
    const currentCache = cacheNamePrefix + self.assetsManifest.version;
    const keys = await caches.keys();
    await Promise.all(keys.filter(key => key.startsWith(cacheNamePrefix) && key !== currentCache)
        .map(key => caches.delete(key)));
    await self.clients.claim();
}

async function onFetch(event) {
    if (event.request.method !== 'GET' || new URL(event.request.url).pathname.startsWith('/api/')) {
        return fetch(event.request);
    }

    const cache = await caches.open(cacheNamePrefix + self.assetsManifest.version);
    const path = new URL(event.request.url).pathname;
    if (event.request.mode === 'navigate' || shellPaths.includes(path)) {
        try {
            return await fetch(event.request);
        } catch {
            return (await cache.match(event.request)) || (await cache.match('/index.html')) || Response.error();
        }
    }

    return await cache.match(event.request) || await fetch(event.request);
}

