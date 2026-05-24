import { Navigate, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import PlayersPage from './pages/PlayersPage';
import GamesPage from './pages/GamesPage';
import NewGamePage from './pages/NewGamePage';
import GamePlayPage from './pages/GamePlayPage';
import LoginPage from './pages/LoginPage';
import DemoPage from './pages/DemoPage';
import AdminUsersPage from './pages/AdminUsersPage';
import ProfilePage from './pages/ProfilePage';
import RoomsPage from './pages/RoomsPage';
import RoomLobbyPage from './pages/RoomLobbyPage';
import RoomPlayPage from './pages/RoomPlayPage';
import RoomHistoryDetailPage from './pages/RoomHistoryDetailPage';
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
  const location = useLocation();

  if (location.pathname === '/demo') {
    return <DemoPage />;
  }

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
            <NavLink to="/rooms">
              <span className="hide-mobile">Phòng online</span>
              <span className="show-mobile"><Icon name="globe" size={16} /></span>
            </NavLink>
            {state.isAdmin && (
              <NavLink to="/admin/users">
                <span className="hide-mobile">Quản lý user</span>
                <span className="show-mobile"><Icon name="users" size={16} /></span>
              </NavLink>
            )}
            <NavLink to="/profile" className="muted small" style={{ padding: '0 0.5rem' }}>
              {state.displayName || state.username}
            </NavLink>
            <button
              type="button"
              className="ghost sm"
              onClick={() => logout()}
              title="Đăng xuất"
              aria-label="Đăng xuất"
            >
              <Icon name="logout" size={14} />
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
          <Route path="/rooms" element={<RoomsPage />} />
          <Route path="/rooms/:code/history" element={<RoomHistoryDetailPage />} />
          <Route path="/rooms/:code" element={<RoomLobbyPage />} />
          <Route path="/play/:id" element={<RoomPlayPage />} />
          <Route path="/profile" element={<ProfilePage />} />
          {state.isAdmin && <Route path="/admin/users" element={<AdminUsersPage />} />}
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </>
  );
}
