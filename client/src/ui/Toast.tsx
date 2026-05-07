import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { Icon } from './Icon';

type ToastKind = 'info' | 'success' | 'error';
interface ToastItem {
  id: number;
  kind: ToastKind;
  message: string;
}

interface ToastCtx {
  push: (kind: ToastKind, message: string) => void;
}

const Ctx = createContext<ToastCtx | null>(null);

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const idRef = useRef(0);

  const push = useCallback((kind: ToastKind, message: string) => {
    const id = ++idRef.current;
    setItems((prev) => [...prev, { id, kind, message }]);
    setTimeout(() => {
      setItems((prev) => prev.filter((t) => t.id !== id));
    }, 4500);
  }, []);

  return (
    <Ctx.Provider value={{ push }}>
      {children}
      <div className="toast-stack" aria-live="polite">
        {items.map((t) => (
          <ToastView key={t.id} item={t} onClose={() => setItems((prev) => prev.filter((x) => x.id !== t.id))} />
        ))}
      </div>
    </Ctx.Provider>
  );
}

function ToastView({ item, onClose }: { item: ToastItem; onClose: () => void }) {
  useEffect(() => {}, []);
  const iconName: 'check' | 'alert' | 'info' =
    item.kind === 'success' ? 'check' : item.kind === 'error' ? 'alert' : 'info';
  return (
    <div className={`toast ${item.kind}`} role="status">
      <div className="toast-icon">
        <Icon name={iconName} />
      </div>
      <div className="toast-body">{item.message}</div>
      <button type="button" className="toast-close" onClick={onClose} aria-label="Đóng">×</button>
    </div>
  );
}

export function useToast() {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error('useToast must be used inside ToastProvider');
  return ctx;
}
