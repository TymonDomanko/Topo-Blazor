// A lock shared by every tab of Topo in the browser (the Web Locks API), for the work Terrain allows only one of at a time: an assumed Scout
// profile, and the account's selected calendars (Topo/Services/TerrainLocks.cs). A browser without the API gets no shared lock.
window.topoLock = (function () {
    const held = {};
    let next = 1;
    return {
        // Resolves, once the lock is this tab's, with the number to release it by
        acquire: function (name) {
            if (!navigator.locks) {
                return Promise.resolve(0);
            }
            return new Promise(function (resolve) {
                navigator.locks.request("topo-" + name, function () {
                    return new Promise(function (release) {
                        const id = next++;
                        held[id] = release;
                        resolve(id);
                    });
                });
            });
        },
        release: function (id) {
            const release = held[id];
            if (release) {
                delete held[id];
                release();
            }
        }
    };
})();
