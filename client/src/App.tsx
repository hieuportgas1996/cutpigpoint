import { NavLink, Route, Routes } from 'react-router-dom';
import PlayersPage from './pages/PlayersPage';
import GamesPage from './pages/GamesPage';
import NewGamePage from './pages/NewGamePage';
import GamePlayPage from './pages/GamePlayPage';
import LoginPage from './pages/LoginPage';
import { ToastProvider } from './ui/Toast';
import { Icon } from './ui/Icon';
import { AuthProvider, useAuth } from './auth/AuthContext';

export default function App() {
  return (
    <ToastProvider>
      <AuthProvider>
        <AppShell />
      </AuthProvider>
    </ToastProvider>
  );
}

function AppShell() {
  const { state, logout } = useAuth();

  if (state.status === 'loading') {
    return (
      <div className="container" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: '50vh' }}>
        <div className="muted"><Icon name="clock" size={14} /> Đang tải…</div>
      </div>
    );
  }

  if (state.status === 'unauthenticated') {
    return <LoginPage />;
  }

  return (
    <>
      <nav className="nav">
        <div className="nav-inner">
          <NavLink to="/" className="brand">
            <span className="brand-icon"><Icon name="cards" size={16} /></span>
            <span>CutPigPoint</span>
          </NavLink>
          <div className="nav-links">
            <NavLink to="/" end>
              <span className="hide-mobile">Ván chơi</span>
              <span className="show-mobile"><Icon name="cards" size={16} /></span>
            </NavLink>
            <NavLink to="/players">
              <span className="hide-mobile">Người chơi</span>
              <span className="show-mobile"><Icon name="users" size={16} /></span>
            </NavLink>
            <NavLink to="/new">
              <span className="hide-mobile">Ván mới</span>
              <span className="show-mobile"><Icon name="plus" size={16} /></span>
            </NavLink>
            <span className="muted small hide-mobile" style={{ padding: '0 0.5rem' }}>
              {state.username}
            </span>
            <button
              type="button"
              className="ghost sm"
              onClick={() => logout()}
              title="Đăng xuất"
              aria-label="Đăng xuất"
            >
              <Icon name="flag" size={14} />
              <span className="hide-mobile">Đăng xuất</span>
            </button>
          </div>
        </div>
      </nav>
      <main className="container">
        <Routes>
          <Route path="/" element={<GamesPage />} />
          <Route path="/players" element={<PlayersPage />} />
          <Route path="/new" element={<NewGamePage />} />
          <Route path="/games/:id" element={<GamePlayPage />} />
        </Routes>
      </main>
    </>
  );
}
