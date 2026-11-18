import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

const html = readFileSync(new URL("../../apps/frontend/public/starting.html", import.meta.url), "utf8");
const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];

function start(hash = "") {
  const timers = new Map();
  const images = [];
  const message = { textContent: "" };
  let nextTimer = 0;
  let redirected;
  const context = {
    URLSearchParams,
    location: { hash, replace: (url) => (redirected = url) },
    document: { getElementById: () => message },
    Image: class {
      constructor() {
        images.push(this);
      }
    },
    setTimeout: (callback, delay) => {
      const id = ++nextTimer;
      timers.set(id, { callback, delay });
      return id;
    },
    clearTimeout: (id) => timers.delete(id),
  };
  vm.runInNewContext(script, context);
  return {
    images,
    message,
    get redirected() {
      return redirected;
    },
    tick(delay) {
      const entry = [...timers.entries()].find(([, timer]) => timer.delay === delay);
      assert.ok(entry, `No ${delay}ms timer`);
      timers.delete(entry[0]);
      entry[1].callback();
    },
  };
}

test("retries until the server responds, then opens the dashboard", () => {
  const page = start("#port=5180");
  assert.match(page.images[0].src, /^http:\/\/localhost:5180\/startup-probe\.svg\?/);
  page.images[0].onerror();
  page.tick(1000);
  assert.equal(page.images.length, 2);
  page.images[1].onload();
  assert.equal(page.redirected, "http://localhost:5180/");
});

test("retries a stalled probe and ignores its late response", () => {
  const page = start("#port=1234&path=changelog");
  page.tick(5000);
  page.images[0].onload();
  assert.equal(page.redirected, undefined);
  page.tick(1000);
  page.images[1].onload();
  assert.equal(page.redirected, "http://localhost:1234/changelog");
});

test("explains a long wait and rejects invalid ports", () => {
  const page = start();
  page.tick(30000);
  assert.match(page.message.textContent, /Still waiting/);
  const invalid = start("#port=99999");
  assert.match(invalid.message.textContent, /invalid/);
  assert.equal(invalid.images.length, 0);
});
