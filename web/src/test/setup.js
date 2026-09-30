// Shared by every test file (vite.config.js → test.setupFiles).
import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';

import { clearToken } from '../services/tokenStore';

afterEach(() => {
  // Unmount whatever the test rendered, so no component outlives its test.
  cleanup();
  // The token store is a module variable mirrored into localStorage; reset both so one test's
  // session never leaks into the next.
  clearToken();
  window.localStorage.clear();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});
