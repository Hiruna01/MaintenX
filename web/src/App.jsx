import NavBar from './components/NavBar';
import AppRoutes from './routes/AppRoutes';

/** App shell: the header on every page, and the router's output below it. */
export function App() {
  return (
    <div className="app">
      <NavBar />
      <main className="app__main">
        <AppRoutes />
      </main>
    </div>
  );
}

export default App;
