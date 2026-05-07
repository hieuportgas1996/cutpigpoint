import { useState } from 'react';
import { api } from '../api';
import { initials } from './helpers';

interface Props {
  playerId?: string;
  name: string;
  hasAvatar?: boolean;
  size?: 'sm' | 'md' | 'lg';
  cacheBuster?: number;
}

export function Avatar({ playerId, name, hasAvatar, size = 'md', cacheBuster }: Props) {
  const [errored, setErrored] = useState(false);
  const showImg = !!playerId && !!hasAvatar && !errored;
  const className = `avatar${size === 'sm' ? ' sm' : size === 'lg' ? ' lg' : ''}`;
  const src = showImg
    ? `${api.avatarUrl(playerId!)}${cacheBuster ? `?v=${cacheBuster}` : ''}`
    : null;

  return (
    <div className={className} aria-label={name}>
      {src ? (
        <img
          src={src}
          alt={name}
          onError={() => setErrored(true)}
          style={{ width: '100%', height: '100%', objectFit: 'cover', borderRadius: '50%' }}
        />
      ) : (
        initials(name)
      )}
    </div>
  );
}
