/**
 * Where the access token lives.
 *
 * The API issues one access token with a 12-hour lifetime and no refresh token, so there
 * is exactly one value to keep. It is held in a module variable (fast, and what every
 * request reads) and mirrored into localStorage so a page reload does not sign the user
 * out. React Context owns the *user*; this module owns the *token*, because non-component
 * code — useFetch and the feature services — needs it too and cannot call a hook.
 */

const STORAGE_KEY = 'maintenx.token';

let token = null;
let loadedFromStorage = false;

// Listeners are notified whenever the token changes, which is how AuthContext finds out
// that a 401 expired the session from inside a fetch it never started.
const listeners = new Set();

function notify() {
  listeners.forEach((listener) => listener(token));
}

function readStorage() {
  try {
    return window.localStorage.getItem(STORAGE_KEY);
  } catch {
    // Private browsing or a blocked storage policy — fall back to memory only.
    return null;
  }
}

function writeStorage(value) {
  try {
    if (value === null) {
      window.localStorage.removeItem(STORAGE_KEY);
    } else {
      window.localStorage.setItem(STORAGE_KEY, value);
    }
  } catch {
    // Ignore: the in-memory copy still works for this tab.
  }
}

export function getToken() {
  if (!loadedFromStorage) {
    token = readStorage();
    loadedFromStorage = true;
  }
  return token;
}

export function setToken(value) {
  token = value;
  loadedFromStorage = true;
  writeStorage(value);
  notify();
}

export function clearToken() {
  setToken(null);
}

/**
 * Called when the API answers 401 to a request that carried a token: the token is no
 * longer good (expired, or the signing key changed), so drop it and tell the listeners.
 */
export function expireSession() {
  if (getToken() !== null) {
    clearToken();
  }
}

/** Returns an unsubscribe function, so a useEffect cleanup can call it directly. */
export function subscribe(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
