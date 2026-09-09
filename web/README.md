# web — MaintenX React client

React 18 + Vite, **JavaScript (`.jsx`), not TypeScript**. Talks only to the ASP.NET Core
API in `api/` — never to the Python agent service.

## Running it

```bash
cd web
npm install
cp .env.example .env      # set VITE_API_BASE_URL if the API is not on http://localhost:5138
npm run dev               # http://localhost:5173
```

The API must allow the dev server's origin: set `Cors__AllowedOrigins__0=http://localhost:5173`
in the repo-root `.env`.

Other scripts: `npm run lint` (oxlint), `npm run build`, `npm run preview`.

## Structure (SE3090 Lab 02)

```
src/components/                shared reusable UI — Spinner, Button, ErrorMessage, …
src/hooks/                     shared hooks — useFetch, useDebounce
src/services/                  apiClient (base URL + JWT header), tokenStore
src/features/<name>/components|hooks|services|pages
src/routes/                    AppRoutes, ProtectedRoute, 404 / not-authorised pages
```

Components never call `fetch`. A page reads data through `useFetch` (or a feature hook that
wraps it) and every page renders **all three** of loading, error and success — a blank
screen while loading is a bug. Feature `services/` hold the API call functions.

## State

`useState` for local UI state, React **Context** for app-wide auth/session
(`AuthProvider` + `useAuth`). No Redux, no Zustand, no TanStack Query — locked ADR
decision.

The access token lives in `services/tokenStore.js` (a module variable mirrored into
`localStorage`), because `useFetch` and the feature services need it and cannot call a
hook. A 401 on a request that carried a token expires the session: the token is dropped,
`AuthProvider` clears the user, and the route guards send them to `/login`.

## Routing

`BrowserRouter` in `main.jsx`, routes in `src/routes/AppRoutes.jsx`, `NavLink` navigation,
and a catch-all 404. `ProtectedRoute` redirects an unauthenticated visitor to `/login`
(remembering where they were going) and renders a clear "not authorised" page — never a
blank one — when the role is wrong. Navigation is role-based: a Reporter never sees the
manager links.
