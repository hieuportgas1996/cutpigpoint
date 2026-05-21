import { useNavigate, useParams } from 'react-router-dom';

export default function RoomPlayPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  return (
    <div className="card" style={{ textAlign: 'center' }}>
      <h2 style={{ marginTop: 0 }}>🎴 Gameplay sắp có</h2>
      <p className="muted">
        Phòng <code>{id}</code> đã bắt đầu, nhưng phần chơi bài online (chia bài, đánh, validate luật) đang được phát triển ở Phase 3.
      </p>
      <button className="ghost sm" onClick={() => navigate('/rooms')}>← Về danh sách phòng</button>
    </div>
  );
}
