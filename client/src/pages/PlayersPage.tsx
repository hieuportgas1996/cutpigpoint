import { useEffect, useRef, useState } from 'react';
import { api, Player } from '../api';
import { Icon } from '../ui/Icon';
import { useToast } from '../ui/Toast';
import { Avatar } from '../ui/Avatar';
import { fileToAvatarDataUrl } from '../ui/image';

export default function PlayersPage() {
  const [players, setPlayers] = useState<Player[]>([]);
  const [name, setName] = useState('');
  const [nickname, setNickname] = useState('');
  const [editId, setEditId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [uploadingId, setUploadingId] = useState<string | null>(null);
  const [avatarVersion, setAvatarVersion] = useState<Record<string, number>>({});
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const targetPlayerRef = useRef<string | null>(null);
  const toast = useToast();

  async function refresh() {
    try {
      setPlayers(await api.listPlayers());
    } catch (e) {
      toast.push('error', (e as Error).message);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    refresh();
  }, []);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) return;
    try {
      if (editId) {
        await api.updatePlayer(editId, name, nickname || undefined);
        toast.push('success', `Đã cập nhật ${name}`);
      } else {
        await api.createPlayer(name, nickname || undefined);
        toast.push('success', `Đã thêm ${name}`);
      }
      setName('');
      setNickname('');
      setEditId(null);
      await refresh();
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  function edit(p: Player) {
    setEditId(p.id);
    setName(p.name);
    setNickname(p.nickname ?? '');
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  function cancelEdit() {
    setEditId(null);
    setName('');
    setNickname('');
  }

  async function remove(p: Player) {
    if (!confirm(`Xoá người chơi "${p.name}"?`)) return;
    try {
      await api.deletePlayer(p.id);
      toast.push('info', `Đã xoá ${p.name}`);
      await refresh();
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  function openFilePicker(playerId: string) {
    targetPlayerRef.current = playerId;
    fileInputRef.current?.click();
  }

  async function onFileChosen(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = '';
    const playerId = targetPlayerRef.current;
    targetPlayerRef.current = null;
    if (!file || !playerId) return;
    setUploadingId(playerId);
    try {
      const dataUrl = await fileToAvatarDataUrl(file);
      await api.setAvatar(playerId, dataUrl);
      setAvatarVersion((v) => ({ ...v, [playerId]: Date.now() }));
      setPlayers((prev) => prev.map((p) => (p.id === playerId ? { ...p, hasAvatar: true } : p)));
      toast.push('success', 'Đã cập nhật ảnh đại diện');
    } catch (err) {
      toast.push('error', (err as Error).message);
    } finally {
      setUploadingId(null);
    }
  }

  async function removeAvatar(p: Player) {
    if (!confirm(`Xoá ảnh đại diện của "${p.name}"?`)) return;
    try {
      await api.deleteAvatar(p.id);
      setPlayers((prev) => prev.map((x) => (x.id === p.id ? { ...x, hasAvatar: false } : x)));
      toast.push('info', 'Đã xoá ảnh đại diện');
    } catch (e) {
      toast.push('error', (e as Error).message);
    }
  }

  return (
    <div>
      <input
        ref={fileInputRef}
        type="file"
        accept="image/*"
        style={{ display: 'none' }}
        onChange={onFileChosen}
      />

      <div className="page-header">
        <div>
          <h1>Người chơi</h1>
          <div className="muted small">Quản lý danh sách người chơi để thêm vào ván</div>
        </div>
        <span className="status done"><Icon name="users" size={14} />{players.length} người</span>
      </div>

      <div className="card">
        <h3 style={{ marginBottom: '0.85rem' }}>
          {editId ? 'Sửa người chơi' : 'Thêm người chơi'}
        </h3>
        <form onSubmit={submit}>
          <div className="form-row">
            <div>
              <label htmlFor="p-name">Tên</label>
              <input
                id="p-name"
                placeholder="Ví dụ: Nguyễn Văn A"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                autoComplete="off"
              />
            </div>
            <div>
              <label htmlFor="p-nick">Biệt danh <span className="dim">(tuỳ chọn)</span></label>
              <input
                id="p-nick"
                placeholder="Ví dụ: A Cá"
                value={nickname}
                onChange={(e) => setNickname(e.target.value)}
                autoComplete="off"
              />
            </div>
          </div>
          <div className="row mt-2">
            <button type="submit" className="block-mobile">
              <Icon name={editId ? 'check' : 'plus'} size={16} />
              {editId ? 'Cập nhật' : 'Thêm người chơi'}
            </button>
            {editId && (
              <button type="button" className="ghost block-mobile" onClick={cancelEdit}>
                Huỷ
              </button>
            )}
          </div>
        </form>
        {!editId && (
          <div className="muted tiny mt-1">
            <Icon name="info" size={11} /> Sau khi thêm, chạm vào ảnh đại diện trong danh sách để tải ảnh lên
          </div>
        )}
      </div>

      <div className="card card-flush">
        {loading ? (
          <div className="empty">
            <div className="empty-icon"><Icon name="clock" /></div>
            <div>Đang tải…</div>
          </div>
        ) : players.length === 0 ? (
          <div className="empty">
            <div className="empty-icon"><Icon name="users" /></div>
            <div>Chưa có người chơi nào</div>
            <div className="small dim mt-1">Thêm người chơi đầu tiên ở form phía trên</div>
          </div>
        ) : (
          <div style={{ padding: '0.5rem 0' }}>
            {players.map((p) => {
              const isUploading = uploadingId === p.id;
              return (
                <div
                  key={p.id}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '0.85rem',
                    padding: '0.7rem 1rem',
                    borderBottom: '1px solid var(--border)'
                  }}
                >
                  <button
                    type="button"
                    className="ghost"
                    onClick={() => openFilePicker(p.id)}
                    disabled={isUploading}
                    aria-label="Đổi ảnh đại diện"
                    style={{
                      padding: 0,
                      width: 44,
                      height: 44,
                      borderRadius: '50%',
                      position: 'relative',
                      overflow: 'visible'
                    }}
                  >
                    <Avatar
                      playerId={p.id}
                      name={p.name}
                      hasAvatar={p.hasAvatar}
                      size="lg"
                      cacheBuster={avatarVersion[p.id]}
                    />
                    <span
                      aria-hidden
                      style={{
                        position: 'absolute',
                        right: -2,
                        bottom: -2,
                        width: 18,
                        height: 18,
                        borderRadius: '50%',
                        background: 'var(--bg-elev)',
                        border: '1px solid var(--border-strong)',
                        display: 'inline-flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        color: 'var(--text-muted)'
                      }}
                    >
                      <Icon name="edit" size={10} />
                    </span>
                  </button>

                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div className="bold" style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {p.name}
                    </div>
                    {p.nickname && <div className="small muted">{p.nickname}</div>}
                    {isUploading && <div className="tiny dim">Đang tải ảnh…</div>}
                  </div>

                  {p.hasAvatar && (
                    <button
                      className="ghost icon-only"
                      onClick={() => removeAvatar(p)}
                      aria-label="Xoá ảnh đại diện"
                      title="Xoá ảnh đại diện"
                    >
                      <Icon name="trash" size={14} />
                    </button>
                  )}
                  <button className="secondary icon-only" onClick={() => edit(p)} aria-label="Sửa tên">
                    <Icon name="edit" size={16} />
                  </button>
                  <button className="danger icon-only" onClick={() => remove(p)} aria-label="Xoá người chơi">
                    <Icon name="trash" size={16} />
                  </button>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
