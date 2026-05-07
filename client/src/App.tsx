import { NavLink, Route, Routes } from 'react-router-dom';
import PlayersPage from './pages/PlayersPage';
import GamesPage from './pages/GamesPage';
import NewGamePage from './pages/NewGamePage';
import GamePlayPage from './pages/GamePlayPage';
import { ToastProvider } from './ui/Toast';
import { Icon } from './ui/Icon';

export default function App() {
  return (
    <ToastProvider>
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
    </ToastProvider>
  );
}
