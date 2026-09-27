const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const script = fs.readFileSync(path.resolve(
    __dirname, "../../RoleValidation.Web/wwwroot/js/application-users.js"), "utf8");

function loadFilter(width, height, anchor) {
    const windowEvents = {};
    const documentEvents = {};
    const elements = {};
    for (const name of ["toggle", "panel", "search", "clear", "mode", "select-all",
        "select-all-label", "counter", "summary"]) {
        elements[name] = {
            dataset: {}, style: {}, events: {}, attributes: {}, hidden: name === "panel",
            addEventListener(type, handler) { this.events[type] = handler; },
            setAttribute(name, value) { this.attributes[name] = value; },
            focus() { this.focused = true; }
        };
    }
    elements.toggle.getBoundingClientRect = () => anchor;
    elements.panel.getBoundingClientRect = () => ({
        height: Math.min(360, parseFloat(elements.panel.style.maxHeight) || 360)
    });
    const filter = {
        dataset: {},
        querySelector(selector) { return elements[selector.slice(13, -1)]; },
        querySelectorAll() { return []; },
        contains(target) { return Object.values(elements).includes(target); }
    };
    const page = {
        querySelector() { return null; },
        querySelectorAll(selector) {
            return selector === "[data-multi-select-filter]" ? [filter] : [];
        }
    };
    const viewport = {
        width, height, offsetLeft: 0, offsetTop: 0, events: {},
        addEventListener(type, handler) { this.events[type] = handler; }
    };
    const window = {
        innerHeight: height, visualViewport: viewport,
        addEventListener(type, handler) { windowEvents[type] = handler; }
    };
    const document = {
        documentElement: { clientWidth: width },
        querySelector() { return page; },
        addEventListener(type, handler) { documentEvents[type] = handler; }
    };
    vm.runInNewContext(script, { window, document });
    return { elements, viewport, windowEvents, documentEvents };
}

for (const width of [375, 768, 1150, 1200, 1274, 1440]) {
    test("popup stays inside viewport at " + width + "px", () => {
        const { elements } = loadFilter(width, 800, {
            left: width - 210, top: 220, bottom: 262
        });
        elements.toggle.events.click();
        const left = parseFloat(elements.panel.style.left);
        const panelWidth = parseFloat(elements.panel.style.width);
        assert.ok(left >= 12);
        assert.ok(left + panelWidth <= width - 12);
        assert.equal(elements.panel.hidden, false);
        assert.equal(elements.toggle.attributes["aria-expanded"], "true");
    });
}

test("popup opens above a low trigger and tracks viewport resize", () => {
    const { elements, viewport } = loadFilter(1200, 800, {
        left: 1040, top: 680, bottom: 722
    });
    elements.toggle.events.click();
    assert.ok(parseFloat(elements.panel.style.top) < 680);
    assert.ok(parseFloat(elements.panel.style.maxHeight) <= 776);
    viewport.width = 375;
    viewport.height = 420;
    viewport.events.resize();
    assert.equal(elements.panel.hidden, true);
});

test("Escape closes the popup and restores trigger focus", () => {
    const { elements } = loadFilter(375, 800, { left: 35, top: 200, bottom: 242 });
    elements.toggle.events.click();
    elements.panel.events.keydown({ key: "Escape", preventDefault() {} });
    assert.equal(elements.panel.hidden, true);
    assert.equal(elements.toggle.attributes["aria-expanded"], "false");
    assert.equal(elements.toggle.focused, true);
});

function loadNavigation({
    href = "https://rolevalidation.example.test/portal/ApplicationUsers?applicationId=1",
    action = "",
    applicationId = "2",
    sortKey = "EmployeeNumber",
    sortDirection = "Descending"
} = {}) {
    const navigations = [];
    const loadingMask = { hidden: true };
    const applicationSelect = {
        value: applicationId,
        form: { action },
        events: {},
        addEventListener(type, handler) { this.events[type] = handler; }
    };
    const sortButton = {
        dataset: { sortKey, nextSortDirection: sortDirection },
        events: {},
        addEventListener(type, handler) { this.events[type] = handler; }
    };
    const elements = {
        "[data-loading-mask]": loadingMask,
        "[data-application-select]": applicationSelect,
        "[data-user-table]": {
            querySelectorAll() { return [sortButton]; }
        }
    };
    const page = {
        querySelector(selector) { return elements[selector] || null; },
        querySelectorAll() { return []; }
    };
    const window = {
        location: {
            href,
            origin: new URL(href).origin,
            assign(url) { navigations.push(String(url)); }
        }
    };
    vm.runInNewContext(script, {
        window, document: { querySelector() { return page; } }, URL
    });
    return {
        navigations,
        loadingMask,
        changeApplication() { applicationSelect.events.change(); },
        sort() { sortButton.events.click(); }
    };
}

for (const action of [
    "https://untrusted.example.test/collect",
    "//untrusted.example.test/collect",
    "https://rolevalidation.example.test.untrusted.example.test/collect",
    "https://rolevalidation.example.test@untrusted.example.test/collect",
    "https://rolevalidation.example.test:444/collect",
    "http://rolevalidation.example.test/collect",
    "javascript:alert(1)",
    "data:text/html,unexpected",
    "blob:https://rolevalidation.example.test/unexpected"
]) {
    test("application change rejects unsafe destination " + action, () => {
        const page = loadNavigation({ action });
        page.changeApplication();
        assert.deepEqual(page.navigations, []);
        assert.equal(page.loadingMask.hidden, true);
    });
}

test("application change clears previous filters and retains the virtual directory and hash", () => {
    const page = loadNavigation({
        href: "https://rolevalidation.example.test/portal/ApplicationUsers?applicationId=1&sourceRoleKeys=A&sourceRoleKeys=B&mappedRoleIds=3&sortBy=FullName&page=4#users"
    });
    page.changeApplication();
    assert.deepEqual(page.navigations, [
        "https://rolevalidation.example.test/portal/ApplicationUsers?applicationId=2#users"
    ]);
    assert.equal(page.loadingMask.hidden, false);
});

test("application change supports a relative form action on the current origin", () => {
    const page = loadNavigation({ action: "./ApplicationUsers?applicationId=1&page=4" });
    page.changeApplication();
    assert.deepEqual(page.navigations, [
        "https://rolevalidation.example.test/portal/ApplicationUsers?applicationId=2"
    ]);
});

test("application ID containing a URL remains query data", () => {
    const page = loadNavigation({ applicationId: "https://untrusted.example.test/collect" });
    page.changeApplication();
    assert.deepEqual(page.navigations, [
        "https://rolevalidation.example.test/portal/ApplicationUsers?applicationId=https%3A%2F%2Funtrusted.example.test%2Fcollect"
    ]);
});

for (const direction of ["Ascending", "Descending"]) {
    test("sorting " + direction + " preserves repeated filters and resets the page", () => {
        const page = loadNavigation({
            href: "https://rolevalidation.example.test/portal/ApplicationUsers?applicationId=2&sourceRoleKeys=A&sourceRoleKeys=B&mappedRoleIds=3&page=4&sortBy=FullName&sortDirection=Ascending#users",
            sortDirection: direction
        });
        page.sort();
        assert.deepEqual(page.navigations, [
            "https://rolevalidation.example.test/portal/ApplicationUsers?applicationId=2&sourceRoleKeys=A&sourceRoleKeys=B&mappedRoleIds=3&page=1&sortBy=EmployeeNumber&sortDirection=" + direction + "#users"
        ]);
        assert.equal(page.loadingMask.hidden, false);
    });
}

test("sorting treats a URL-shaped sort key as query data", () => {
    const page = loadNavigation({ sortKey: "//untrusted.example.test/collect" });
    page.sort();
    assert.deepEqual(page.navigations, [
        "https://rolevalidation.example.test/portal/ApplicationUsers?applicationId=1&sortBy=%2F%2Funtrusted.example.test%2Fcollect&sortDirection=Descending&page=1"
    ]);
});

test("HTTP development URLs with a custom port still support navigation", () => {
    const page = loadNavigation({ href: "http://localhost:5040/ApplicationUsers?applicationId=1" });
    page.changeApplication();
    page.sort();
    assert.deepEqual(page.navigations, [
        "http://localhost:5040/ApplicationUsers?applicationId=2",
        "http://localhost:5040/ApplicationUsers?applicationId=1&sortBy=EmployeeNumber&sortDirection=Descending&page=1"
    ]);
});
