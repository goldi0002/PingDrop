export interface ReconnectOptions {
  /** Delay before the first manual reconnect. Default 1000. */
  initialDelayMs?: number;
  /** Upper bound for the delay. Default 30000. */
  maxDelayMs?: number;
  /** Multiplier per failed attempt. Default 2. */
  factor?: number;
  /** 0..1: fraction of the delay randomly shaved off (0 = none). Default 0.5. */
  jitter?: number;
}

export interface PingDropClientOptions {
  /** Stream URL. Default "/pingdrop/stream". */
  url?: string;
  /** Sent to the server as `eventType=a,b`; only these event types are delivered. */
  eventTypes?: string[];
  /** Extra query string parameters (e.g. the user key). Array values repeat the key. */
  params?: Record<string, string | number | boolean | null | undefined | Array<string | number | boolean>>;
  withCredentials?: boolean;
  /** Backoff settings, or false to never reconnect after the browser gives up. */
  reconnect?: ReconnectOptions | false;
  /**
   * On a manual reconnect (after the browser gave up), send the last seen event id as `?lastEventId=N` so the server
   * can replay events missed in the gap. Default true. Ignored when `params.lastEventId` is set.
   */
  replay?: boolean;
  /** Connect in the constructor. Default true. */
  autoConnect?: boolean;
  /** Custom EventSource constructor (tests, polyfills). */
  EventSourceImpl?: new (url: string, init?: { withCredentials?: boolean }) => any;
  random?: () => number;
  setTimeoutImpl?: (fn: () => void, ms: number) => any;
  clearTimeoutImpl?: (handle: any) => void;
}

export type PingDropState = 'connecting' | 'open' | 'closed';

export class PingDropClient {
  constructor(options?: PingDropClientOptions);
  /** The final URL including query parameters. */
  readonly url: string;
  readonly state: PingDropState;
  /** Id of the newest event delivered to a handler, or null. Only events with a registered handler are tracked. */
  readonly lastEventId: string | null;
  /** Native EventSource readyState (0, 1, 2); 2 when there is no connection. */
  readonly readyState: number;
  on(name: 'open' | 'close', handler: () => void): this;
  on(name: 'error', handler: (evt: Event) => void): this;
  on<T = unknown>(name: string, handler: (data: T, evt: MessageEvent) => void): this;
  off(name: string, handler?: (...args: any[]) => void): this;
  connect(): this;
  close(): void;
  static buildUrl(url: string, eventTypes?: string[], params?: PingDropClientOptions['params']): string;
}

export default PingDropClient;
