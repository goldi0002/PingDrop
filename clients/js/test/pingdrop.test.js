'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const PingDropClient = require('../pingdrop.js');

class FakeES {
  constructor(url, init) {
    this.url = url; this.init = init; this.readyState = 0; this.listeners = {};
    FakeES.instances.push(this);
  }
  addEventListener(n, fn) { (this.listeners[n] = this.listeners[n] || []).push(fn); }
  close() { this.readyState = 2; this.closedByClient = true; }
  open() { this.readyState = 1; this.onopen && this.onopen({}); }
  message(name, data, id) { (this.listeners[name] || []).forEach(f => f({ data, type: name, lastEventId: id === undefined ? '' : String(id) })); }
  fail(closed) { this.readyState = closed ? 2 : 0; this.onerror && this.onerror({ type: 'error' }); }
}
FakeES.instances = [];

function make(extra = {}) {
  FakeES.instances = [];
  const timers = [];
  const client = new PingDropClient(Object.assign({
    EventSourceImpl: FakeES,
    random: () => 0,
    setTimeoutImpl: (fn, ms) => { timers.push({ fn, ms, cleared: false }); return timers.length - 1; },
    clearTimeoutImpl: (i) => { timers[i].cleared = true; },
  }, extra));
  return { client, timers };
}
const last = () => FakeES.instances[FakeES.instances.length - 1];

test('buildUrl adds params and eventType filter', () => {
  assert.equal(PingDropClient.buildUrl('/s'), '/s');
  assert.equal(PingDropClient.buildUrl('/s', ['a', 'b'], { uId: '4 2', skip: null }), '/s?uId=4%202&eventType=a,b');
  assert.equal(PingDropClient.buildUrl('/s?x=1', ['a']), '/s?x=1&eventType=a');
  const { client } = make({ url: '/pingdrop/stream', eventTypes: ['notification', 'alert'], params: { uId: '42' }, withCredentials: true });
  assert.equal(client.url, '/pingdrop/stream?uId=42&eventType=notification,alert');
  assert.equal(last().url, client.url);
  assert.equal(last().init.withCredentials, true);
});

test('typed handlers parse JSON and fall back to raw text', () => {
  const { client } = make({ eventTypes: ['notification'] });
  const got = [];
  client.on('notification', (d, e) => got.push([d, e.type]));
  client.on('late', d => got.push(['late', d]));
  last().message('notification', '{"Text":"Hi"}');
  last().message('notification', 'plain text');
  last().message('late', '5');
  assert.deepEqual(got, [[{ Text: 'Hi' }, 'notification'], ['plain text', 'notification'], ['late', 5]]);
});

test('open/error/close lifecycle and state', () => {
  const { client } = make();
  const seen = [];
  ['open', 'error', 'close'].forEach(n => client.on(n, () => seen.push(n)));
  assert.equal(client.state, 'connecting');
  last().open();
  assert.equal(client.state, 'open');
  last().fail(false);
  assert.equal(client.state, 'connecting');
  client.close();
  assert.equal(client.state, 'closed');
  assert.deepEqual(seen, ['open', 'error', 'close']);
});

test('backoff schedule when EventSource is CLOSED, reset on open', () => {
  const { client, timers } = make({ reconnect: { initialDelayMs: 100, maxDelayMs: 500, factor: 2, jitter: 0 } });
  for (let i = 0; i < 5; i++) {
    last().fail(true);
    timers[timers.length - 1].fn();
  }
  assert.deepEqual(timers.map(t => t.ms), [100, 200, 400, 500, 500]);
  assert.equal(FakeES.instances.length, 6);
  last().open();
  last().fail(true);
  assert.equal(timers[timers.length - 1].ms, 100);
  assert.equal(client.state, 'connecting');
});

test('jitter shaves up to the configured fraction', () => {
  const { timers } = make({ random: () => 1, reconnect: { initialDelayMs: 1000, jitter: 0.5 } });
  last().fail(true);
  assert.equal(timers[0].ms, 500);
});

test('native CONNECTING errors do not schedule a manual reconnect', () => {
  const { timers } = make();
  last().fail(false);
  assert.equal(timers.length, 0);
  assert.equal(FakeES.instances.length, 1);
});

test('close() stops reconnecting', () => {
  const { client, timers } = make();
  last().fail(true);
  assert.equal(timers.length, 1);
  client.close();
  assert.equal(timers[0].cleared, true);
  assert.equal(last().closedByClient, true);
  const n = FakeES.instances.length;
  last().fail(true);
  assert.equal(timers.length, 1);
  assert.equal(FakeES.instances.length, n);
});

test('reconnect:false emits close instead of retrying', () => {
  const { client, timers } = make({ reconnect: false });
  let closed = 0;
  client.on('close', () => closed++);
  last().fail(true);
  assert.equal(timers.length, 0);
  assert.equal(client.state, 'closed');
  assert.equal(closed, 1);
});

test('handlers are re-attached to the new EventSource after reconnect', () => {
  const { client, timers } = make();
  const got = [];
  client.on('notification', d => got.push(d));
  last().fail(true);
  timers[0].fn();
  last().message('notification', '1');
  assert.deepEqual(got, [1]);
});

test('off() removes one handler or all handlers', () => {
  const { client } = make();
  const a = [], b = [];
  const fa = d => a.push(d), fb = d => b.push(d);
  client.on('x', fa).on('x', fb);
  last().message('x', '1');
  client.off('x', fa);
  last().message('x', '2');
  client.off('x');
  last().message('x', '3');
  assert.deepEqual(a, [1]);
  assert.deepEqual(b, [1, 2]);
});

test('a throwing handler does not break others', () => {
  const { client } = make();
  const orig = console.error; console.error = () => {};
  const got = [];
  client.on('x', () => { throw new Error('boom'); }).on('x', d => got.push(d));
  last().message('x', '7');
  console.error = orig;
  assert.deepEqual(got, [7]);
});

test('manual reconnect sends the last seen event id as lastEventId', () => {
  const { client, timers } = make({ eventTypes: ['n'], params: { uId: '7' } });
  assert.equal(client.lastEventId, null);
  last().message('n', '1', 4);
  last().message('n', '2', 5);
  assert.equal(client.lastEventId, '5');
  assert.equal(client.url, '/pingdrop/stream?uId=7&eventType=n'); // url stays the base url
  last().fail(true);
  timers[0].fn();
  assert.equal(last().url, '/pingdrop/stream?uId=7&eventType=n&lastEventId=5');
  last().message('n', '3', 6);
  last().fail(true);
  timers[1].fn();
  assert.match(last().url, /lastEventId=6/);
  assert.equal((last().url.match(/lastEventId/g) || []).length, 1);
});

test('no lastEventId before any event, and replay:false or an explicit param disables tracking', () => {
  let { client, timers } = make({ eventTypes: ['n'] });
  last().fail(true); timers[0].fn();
  assert.equal(last().url, '/pingdrop/stream?eventType=n');

  ({ client, timers } = make({ eventTypes: ['n'], replay: false }));
  last().message('n', '1', 9);
  last().fail(true); timers[0].fn();
  assert.doesNotMatch(last().url, /lastEventId/);

  ({ client, timers } = make({ eventTypes: ['n'], params: { lastEventId: '100' } }));
  last().message('n', '1', 9);
  last().fail(true); timers[0].fn();
  assert.equal((last().url.match(/lastEventId/g) || []).length, 1);
  assert.match(last().url, /lastEventId=100/);
});
