// The browser's IndexedDB store for Topo's data cache (Topo/Services/IndexedDbCacheStore.cs). One object store of entries by key.
window.topoCache = (function () {
    let opening = null;

    function open() {
        if (!opening) {
            opening = new Promise(function (resolve, reject) {
                const request = indexedDB.open("topo-cache", 1);
                request.onupgradeneeded = function () {
                    request.result.createObjectStore("entries", { keyPath: "key" });
                };
                request.onsuccess = function () {
                    resolve(request.result);
                };
                request.onerror = function () {
                    opening = null;
                    reject(request.error);
                };
            });
        }
        return opening;
    }

    // Runs one piece of work in a transaction and resolves with the request's result once the transaction has finished
    async function run(mode, work) {
        const db = await open();
        return new Promise(function (resolve, reject) {
            const transaction = db.transaction("entries", mode);
            const request = work(transaction.objectStore("entries"));
            transaction.oncomplete = function () {
                resolve(request ? request.result : undefined);
            };
            transaction.onerror = function () {
                reject(transaction.error);
            };
            transaction.onabort = function () {
                reject(transaction.error);
            };
        });
    }

    return {
        get: async function (key) {
            return (await run("readonly", function (store) { return store.get(key); })) || null;
        },
        put: async function (entry) {
            await run("readwrite", function (store) { return store.put(entry); });
        },
        // Asks the browser not to throw the store away when it is short of space; true when it agrees
        persist: async function () {
            return !!(navigator.storage && navigator.storage.persist) && await navigator.storage.persist();
        },
        clear: async function () {
            await run("readwrite", function (store) { return store.clear(); });
        },
        removeFetchedBefore: async function (fetchedAt) {
            await run("readwrite", function (store) {
                const cursorRequest = store.openCursor();
                cursorRequest.onsuccess = function () {
                    const cursor = cursorRequest.result;
                    if (cursor) {
                        if (cursor.value.fetchedAt < fetchedAt && !cursor.value.keep) {
                            cursor.delete();
                        }
                        cursor.continue();
                    }
                };
                return null;
            });
        }
    };
})();
