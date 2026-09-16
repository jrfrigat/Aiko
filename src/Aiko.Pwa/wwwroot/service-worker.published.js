self.importScripts('./service-worker-assets.js');

const cacheNamePrefix = 'aiko-cache-';
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
}

async function onActivate() {
    const currentCache = cacheNamePrefix + self.assetsManifest.version;
    const keys = await caches.keys();
    await Promise.all(keys.filter(key => key.startsWith(cacheNamePrefix) && key !== currentCache)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    if (event.request.method !== 'GET' || new URL(event.request.url).pathname.startsWith('/api/')) {
        return fetch(event.request);
    }

    const cache = await caches.open(cacheNamePrefix + self.assetsManifest.version);
    return await cache.match(event.request) || await fetch(event.request);
}
