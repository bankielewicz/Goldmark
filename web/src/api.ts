/**
 * The Goldmark API from the browser (`/api/v1`, DEC-040): JSON in and out, same-origin cookies, and
 * the anti-forgery token on every state-changing request. A token is fetched fresh for each such
 * request, because tokens are bound to the session and signing in or out changes it; a request
 * refused for its token is retried once (signing in ends the session the token was issued for).
 * Errors are Problem Details with a stable `code`.
 */

export const antiforgeryHeader = 'X-Goldmark-Antiforgery';

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>;
  readonly body: Readonly<Record<string, unknown>>;
  /** Shown as the "Reference ID" on server and network errors; matches the server log's trace id. */
  readonly reference: string | null;

  constructor(status: number, code: string, title: string, body: Record<string, unknown> = {}) {
    super(title);
    this.status = status;
    this.code = code;
    this.body = body;
    this.fieldErrors = (body.errors as Record<string, string[]> | undefined) ?? {};
    const trace = typeof body.traceId === 'string' ? body.traceId : null;
    // W3C trace parent: 00-<trace id>-<span id>-<flags>; the first 8 hex digits are enough to find it.
    this.reference = trace?.split('-')[1]?.slice(0, 8) ?? null;
  }

  /** The server could not be reached, or answered with an unexpected error. */
  get isUnavailable(): boolean {
    return this.status === 0 || this.status >= 500;
  }

  hasField(field: string, error: string): boolean {
    return this.fieldErrors[field]?.includes(error) ?? false;
  }
}

export interface ApiResponse<T> {
  data: T;
  etag: string | null;
  headers: Headers;
}

interface RequestOptions {
  ifMatch?: string;
  /** The answer is a file (a CSV export): `data` is its Blob. Errors are still Problem Details. */
  file?: boolean;
}

type UnauthorizedListener = () => void;
const unauthorizedListeners = new Set<UnauthorizedListener>();

/** Called when a signed-in request answers 401: the session has ended (idle, 12 h, or revoked). */
export function onSessionEnded(listener: UnauthorizedListener): () => void {
  unauthorizedListeners.add(listener);
  return () => unauthorizedListeners.delete(listener);
}

async function antiforgeryToken(): Promise<string> {
  const { data } = await send<{ token: string }>('GET', '/api/v1/auth/antiforgery');
  return data.token;
}

async function send<T>(
  method: string,
  path: string,
  body?: unknown,
  options: RequestOptions = {},
): Promise<ApiResponse<T>> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }
  if (method !== 'GET' && method !== 'HEAD') {
    headers[antiforgeryHeader] = await antiforgeryToken();
  }
  if (options.ifMatch) {
    headers['If-Match'] = options.ifMatch;
  }

  let response: Response;
  try {
    response = await fetch(path, {
      method,
      headers,
      credentials: 'same-origin',
      body: body === undefined ? null : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, 'network', 'Couldn’t reach Goldmark.');
  }

  if (response.ok && options.file) {
    return { data: (await response.blob()) as T, etag: response.headers.get('ETag'), headers: response.headers };
  }

  const text = await response.text();
  let json: unknown = null;
  if (text.length > 0 && (response.headers.get('Content-Type') ?? '').includes('json')) {
    try {
      json = JSON.parse(text);
    } catch {
      json = null;
    }
  }

  if (!response.ok) {
    const problem = (json ?? {}) as Record<string, unknown>;
    const code = typeof problem.code === 'string' ? problem.code : `http-${response.status}`;
    const title = typeof problem.title === 'string' ? problem.title : response.statusText;
    throw new ApiError(response.status, code, title, problem);
  }

  return { data: json as T, etag: response.headers.get('ETag'), headers: response.headers };
}

function isAntiforgeryRefusal(error: unknown): boolean {
  return error instanceof ApiError && error.status === 400 && error.message.includes('anti-forgery');
}

/**
 * One API call. `signedIn` requests report a 401 to {@link onSessionEnded} listeners, so the app
 * can show "Your session expired"; sign-in steps pass false and handle their own 401s.
 */
export async function api<T>(
  method: 'GET' | 'POST' | 'PUT' | 'DELETE',
  path: string,
  body?: unknown,
  options: RequestOptions & { signedIn?: boolean } = {},
): Promise<ApiResponse<T>> {
  try {
    try {
      return await send<T>(method, path, body, options);
    } catch (error) {
      if (method !== 'GET' && isAntiforgeryRefusal(error)) {
        return await send<T>(method, path, body, options);
      }
      throw error;
    }
  } catch (error) {
    if (options.signedIn !== false && error instanceof ApiError && error.status === 401) {
      unauthorizedListeners.forEach((listener) => listener());
    }
    throw error;
  }
}

/** The person signed in, as `/api/v1/auth/session` and the sign-in steps return them. */
export interface SessionUser {
  id: string;
  userName: string;
  displayName: string;
  roles: string[];
}
