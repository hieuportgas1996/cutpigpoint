import { NavLink, Route, Routes } from 'react-router-dom';
import PlayersPage from './pages/PlayersPage';
import GamesPage from './pages/GamesPage';
import NewGamePage from './pages/NewGamePage';
import GamePlayPage from './pages/GamePlayPage';

export default function App() {
  return (
    <>
      <nav className="nav">
        <div className="brand">CutPigPoint</div>
        <NavLink to="/" end>Ván chơi</NavLink>
        <NavLink to="/players">Người chơi</NavLink>
        <NavLink to="/new">Ván mới</NavLink>
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
