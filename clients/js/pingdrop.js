/*!
 * PingDrop JavaScript client - a tiny, dependency-free wrapper around EventSource.
 * Adds typed handlers (JSON parsed), server-side event type filtering, and
 * exponential backoff with jitter when the browser gives up on a stream.
 * MIT licensed. https://github.com/goldi0002/PingDrop
 */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) {
    module.exports = factory();
  } else if (typeof define === 'function' && define.amd) {
    define([], factory);
  } else {
    root.PingDropClient = factory();
  }
}(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  var LIFECYCLE = { open: 1, error: 1, close: 1 };
  var CLOSED = 2; // EventSource.CLOSED

  var DEFAULT_RECONNECT = { initialDelayMs: 1000, maxDelayMs: 30000, factor: 2, jitter: 0.5 };

  function buildUrl(url, eventTypes, params) {
    var hash = '';
    var h = url.indexOf('#');
    if (h >= 0) { hash = url.slice(h); url = url.slice(0, h); }

    var pairs = [];
    function add(k, v) { pairs.push(encodeURIComponent(k) + '=' + encodeURIComponent(v)); }

    if (params) {
      Object.keys(params).forEach(function (k) {
        var v = params[k];
        if (v === undefined || v === null) return;
        if (Array.isArray(v)) v.forEach(function (x) { add(k, x); });
        else add(k, v);
      });
    }
    if (eventTypes && eventTypes.length) {
      // The server reads a comma-separated list from the "eventType" query parameter.
      pairs.push('eventType=' + eventTypes.map(encodeURIComponent).join(','));
    }
    if (!pairs.length) return url + hash;
    return url + (url.indexOf('?') >= 0 ? (/[?&]$/.test(url) ? '' : '&') : '?') + pairs.join('&') + hash;
  }

  function parseData(text) {
    try { return JSON.parse(text); } catch (e) { return text; }
  }

  /**
   * @param {object} options
   * @param {string} [options.url='/pingdrop/stream']
   * @param {string[]} [options.eventTypes]  sent to the server as ?eventType=a,b
   * @param {object} [options.params]        extra query parameters
   * @param {boolean} [options.withCredentials]
   * @param {object|false} [options.reconnect]  { initialDelayMs, maxDelayMs, factor, jitter } or false to disable
   * @param {boolean} [options.autoConnect=true]
   * @param {Function} [options.EventSourceImpl]  EventSource constructor (for tests / polyfills)
   * @param {Function} [options.random]      Math.random replacement
   * @param {Function} [options.setTimeoutImpl]
   * @param {Function} [options.clearTimeoutImpl]
   */
  function PingDropClient(options) {
    if (!(this instanceof PingDropClient)) return new PingDropClient(options);
    options = options || {};

    var ES = options.EventSourceImpl || (typeof EventSource !== 'undefined' ? EventSource : null);
    if (!ES) throw new Error('PingDropClient: EventSource is not available; pass options.EventSourceImpl.');

    var rc = options.reconnect === false ? null : Object.assign({}, DEFAULT_RECONNECT, options.reconnect || {});
    var random = options.random || Math.random;
    var setT = options.setTimeoutImpl || function (fn, ms) { return setTimeout(fn, ms); };
    var clearT = options.clearTimeoutImpl || function (t) { clearTimeout(t); };

    var self = this;
    var handlers = {};
    var es = null;
    var attached = {};       // event names with a listener on the current EventSource
    var timer = null;
    var attempt = 0;
    var userClosed = false;
    var state = 'connecting';

    this.url = buildUrl(options.url || '/pingdrop/stream', options.eventTypes, options.params);

    Object.defineProperty(this, 'state', { get: function () { return state; }, enumerable: true });
    Object.defineProperty(this, 'readyState', { get: function () { return es ? es.readyState : CLOSED; } });

    function emit(name, a, b) {
      var list = handlers[name];
      if (!list) return;
      list.slice().forEach(function (fn) {
        try { fn(a, b); } catch (e) {
          if (typeof console !== 'undefined' && console.error) console.error('PingDropClient handler for "' + name + '" threw:', e);
        }
      });
    }

    function attach(name) {
      if (!es || attached[name]) return;
      attached[name] = true;
      es.addEventListener(name, function (evt) { emit(name, parseData(evt.data), evt); });
    }

    /** Delay before reconnect attempt number `n` (0-based), in ms. */
    function delayFor(n) {
      var base = Math.min(rc.maxDelayMs, rc.initialDelayMs * Math.pow(rc.factor, n));
      var j = Math.min(1, Math.max(0, rc.jitter));
      return Math.round(base * (1 - j * random()));
    }

    function connect() {
      timer = null;
      state = 'connecting';
      attached = {};
      es = new ES(self.url, { withCredentials: !!options.withCredentials });
      var current = es;

      es.onopen = function () {
        if (current !== es || userClosed) return;
        attempt = 0;
        state = 'open';
        emit('open');
      };
      es.onerror = function (evt) {
        if (current !== es || userClosed) return;
        if (current.readyState === CLOSED) {
          // The browser gave up (HTTP error, wrong content type, 204, ...). We retry ourselves.
          state = rc ? 'connecting' : 'closed';
          emit('error', evt);
          if (rc) {
            timer = setT(connect, delayFor(attempt++));
          } else {
            userClosed = true;
            emit('close');
          }
        } else {
          // CONNECTING: the browser is already retrying natively.
          state = 'connecting';
          emit('error', evt);
        }
      };

      Object.keys(handlers).forEach(function (name) {
        if (!LIFECYCLE[name]) attach(name);
      });
      if (options.eventTypes) options.eventTypes.forEach(attach);
    }

    /** Registers a handler. Names other than open/error/close are SSE event names; handlers get (parsedData, event). */
    this.on = function (name, fn) {
      (handlers[name] || (handlers[name] = [])).push(fn);
      if (!LIFECYCLE[name]) attach(name);
      return self;
    };

    /** Removes one handler, or all handlers for `name` when `fn` is omitted. */
    this.off = function (name, fn) {
      if (!handlers[name]) return self;
      if (!fn) delete handlers[name];
      else handlers[name] = handlers[name].filter(function (f) { return f !== fn; });
      return self;
    };

    /** Opens the connection (called automatically unless autoConnect is false). */
    this.connect = function () {
      if (es && !userClosed) return self;
      userClosed = false;
      attempt = 0;
      connect();
      return self;
    };

    /** Closes the stream and stops any further reconnect attempts. */
    this.close = function () {
      if (userClosed) return;
      userClosed = true;
      if (timer !== null) { clearT(timer); timer = null; }
      if (es) es.close();
      state = 'closed';
      emit('close');
    };

    if (options.autoConnect !== false) this.connect();
  }

  PingDropClient.buildUrl = buildUrl;
  PingDropClient.PingDropClient = PingDropClient;
  PingDropClient.default = PingDropClient;
  return PingDropClient;
}));
