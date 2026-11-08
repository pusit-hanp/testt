(function (root, factory) {
    "use strict";
    if (typeof module === "object" && module.exports) module.exports = factory();
    else root.PullListMonitoringBoard = factory();
}(this, function () {
    "use strict";
    var MAXIMUM = 50, TTL = 30 * 60 * 1000;
    function validId(value) { return typeof value === "string" && /^[A-Za-z0-9][A-Za-z0-9._\/-]{0,49}$/.test(value); }
    function create(options) {
        var entries = [], storage = options.storage, key = options.key;
        var now = options.now || function () { return Date.now(); };
        var changed = options.changed || function () {};
        var notice = options.notice || function () {};
        var warned = false;
        function storageWarning() {
            if (!warned) notice("เบราว์เซอร์ไม่อนุญาตให้จำรายการ หน้านี้ยังค้นหาได้ แต่ reload แล้วรายการอาจหาย", true);
            warned = true;
        }
        function persist() {
            try {
                if (!storage) throw new Error("No sessionStorage");
                storage.setItem(key, JSON.stringify({ version: 1, editedAt: now(), ids: entries.map(function (x) { return x.id; }) }));
            } catch (e) { storageWarning(); }
        }
        function find(id) {
            for (var i = 0; i < entries.length; i++) if (entries[i].id === id) return entries[i];
            return null;
        }
        function newEntry(id) { return { id: id, data: null, loading: false, error: null, loadedAt: null, sequence: 0 }; }
        function fetchEntries(targets) {
            if (!targets.length) return;
            var tokens = targets.map(function (entry) {
                entry.sequence++; entry.loading = true; entry.error = null;
                return { entry: entry, sequence: entry.sequence };
            });
            changed();
            function current(token) {
                // Identity also protects remove + re-add of the same slip while old requests are pending.
                return find(token.entry.id) === token.entry && token.entry.sequence === token.sequence;
            }
            function fail(message) {
                tokens.forEach(function (token) {
                    if (!current(token)) return;
                    token.entry.loading = false;
                    token.entry.error = message || "โหลดข้อมูลไม่สำเร็จ กรุณาลองใหม่";
                });
                changed();
            }
            try {
                options.request(targets.map(function (x) { return x.id; }), function (response) {
                    if (!response || response.ok !== true || !Array.isArray(response.data)) {
                        fail(response && response.message); return;
                    }
                    tokens.forEach(function (token) {
                        if (!current(token)) return;
                        var entry = token.entry, found = null;
                        for (var i = 0; i < response.data.length; i++) {
                            if (response.data[i] && response.data[i].MislipNo === entry.id) { found = response.data[i]; break; }
                        }
                        entry.loading = false;
                        if (!found) { entry.error = "ไม่ได้รับผลค้นหาของ slip นี้ กรุณาลองใหม่"; return; }
                        entry.data = found;
                        entry.loadedAt = response.loadedAt || new Date(now()).toISOString();
                        entry.error = null;
                    });
                    changed();
                }, fail);
            } catch (e) { fail("โหลดข้อมูลไม่สำเร็จ กรุณาลองใหม่"); }
        }
        return {
            entries: function () { return entries.slice(); },
            search: function (raw) {
                if (typeof raw !== "string" || /[\x00-\x1f\x7f]/.test(raw)) {
                    notice("MISLIP_NO ไม่ถูกต้อง"); return false;
                }
                var id = raw.replace(/^\s+|\s+$/g, "");
                if (!validId(id)) {
                    notice("ระบุ MISLIP_NO แบบเต็ม 1 ถึง 50 ตัว ใช้ตัวอักษร ตัวเลข จุด ขีดกลาง ขีดล่าง หรือ /");
                    return false;
                }
                var entry = find(id);
                if (!entry) {
                    if (entries.length >= MAXIMUM) { notice("ติดตามได้สูงสุด 50 slips กรุณาลบรายการก่อนเพิ่ม"); return false; }
                    entry = newEntry(id); entries.push(entry);
                    persist();
                }
                fetchEntries([entry]); return true;
            },
            remove: function (id) {
                if (!find(id)) return;
                entries = entries.filter(function (entry) { return entry.id !== id; });
                persist(); changed();
            },
            clear: function () { entries = []; persist(); changed(); },
            refresh: function () { fetchEntries(entries.slice()); },
            restore: function () {
                var saved;
                try {
                    if (!storage) throw new Error("No sessionStorage");
                    var raw = storage.getItem(key);
                    if (!raw) return;
                    saved = JSON.parse(raw);
                    var at = now();
                    if (!saved || saved.version !== 1 || typeof saved.editedAt !== "number" || !isFinite(saved.editedAt) ||
                        saved.editedAt > at || at - saved.editedAt >= TTL || !Array.isArray(saved.ids) || saved.ids.length > MAXIMUM) {
                        storage.removeItem(key); return;
                    }
                    var unique = [];
                    for (var i = 0; i < saved.ids.length; i++) {
                        if (!validId(saved.ids[i]) || unique.indexOf(saved.ids[i]) >= 0) { storage.removeItem(key); return; }
                        unique.push(saved.ids[i]);
                    }
                    entries = unique.map(newEntry);
                } catch (e) {
                    // Corruption and storage security exceptions must not stop the page.
                    try { if (storage) storage.removeItem(key); } catch (ignored) {}
                    storageWarning(); return;
                }
                changed(); fetchEntries(entries.slice());
            }
        };
    }
    return { create: create };
}));
