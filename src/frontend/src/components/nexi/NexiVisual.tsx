import { useId } from 'react'

export function NexiVisual({ size = 'medium', className = '' }: { size?: 'small' | 'medium' | 'large'; className?: string }) {
  const rawId = useId().replace(/:/g, '')
  const ids = {
    shell: `nexi-shell-${rawId}`,
    visor: `nexi-visor-${rawId}`,
    cyan: `nexi-cyan-${rawId}`,
    glow: `nexi-glow-${rawId}`,
  }

  return <span className={`nexi-visual ${size} ${className}`.trim()} aria-hidden="true">
    <svg
      viewBox="0 0 160 160"
      role="presentation"
      focusable="false"
      style={{ width: '100%', height: '100%', display: 'block', overflow: 'visible' }}
    >
      <defs>
        <linearGradient id={ids.shell} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#ffffff" />
          <stop offset="0.48" stopColor="#dff8ff" />
          <stop offset="1" stopColor="#9fdff4" />
        </linearGradient>
        <linearGradient id={ids.visor} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#17385f" />
          <stop offset="0.55" stopColor="#071b38" />
          <stop offset="1" stopColor="#0b2348" />
        </linearGradient>
        <linearGradient id={ids.cyan} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#61f7ff" />
          <stop offset="0.5" stopColor="#13cfe8" />
          <stop offset="1" stopColor="#078ad8" />
        </linearGradient>
        <filter id={ids.glow} x="-50%" y="-50%" width="200%" height="200%">
          <feGaussianBlur stdDeviation="2.5" result="blur" />
          <feMerge>
            <feMergeNode in="blur" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
      </defs>

      <g filter={`url(#${ids.glow})`} opacity="0.28">
        <ellipse cx="80" cy="145" rx="31" ry="6" fill="#16d9ef" />
      </g>

      <g strokeLinecap="round" strokeLinejoin="round">
        <path d="M80 17V8" stroke="#83f8ff" strokeWidth="3" />
        <circle cx="80" cy="6" r="3.5" fill="#dffcff" stroke="#53ecf8" strokeWidth="2" />

        <path d="M49 104c-9 1-17 6-21 13 5 6 12 8 20 6" fill={`url(#${ids.cyan})`} stroke="#77f7ff" strokeWidth="3" />
        <path d="M111 104c9 1 17 6 21 13-5 6-12 8-20 6" fill={`url(#${ids.cyan})`} stroke="#77f7ff" strokeWidth="3" />
        <circle cx="24" cy="119" r="8" fill="#19d8ea" stroke="#a5fbff" strokeWidth="3" />
        <circle cx="136" cy="119" r="8" fill="#19d8ea" stroke="#a5fbff" strokeWidth="3" />

        <path d="M55 96c8-7 42-7 50 0l8 28c1 7-4 14-11 16l-11 3H69l-11-3c-7-2-12-9-11-16z" fill={`url(#${ids.shell})`} stroke="#91effa" strokeWidth="3" />
        <path d="M59 102l21 18 21-18" fill="none" stroke="#16cce7" strokeWidth="5" opacity="0.9" />
        <path d="M72 121h16" stroke="#b9eef8" strokeWidth="4" opacity="0.8" />

        <path d="M64 140l-3 10c4 4 11 5 16 1l1-10" fill={`url(#${ids.shell})`} stroke="#91effa" strokeWidth="3" />
        <path d="M96 140l3 10c-4 4-11 5-16 1l-1-10" fill={`url(#${ids.shell})`} stroke="#91effa" strokeWidth="3" />

        <rect x="24" y="52" width="18" height="34" rx="9" fill="#c9f7ff" stroke="#3fe5f5" strokeWidth="3" />
        <rect x="118" y="52" width="18" height="34" rx="9" fill="#c9f7ff" stroke="#3fe5f5" strokeWidth="3" />
        <rect x="29" y="59" width="8" height="20" rx="4" fill="#159dc7" opacity="0.9" />
        <rect x="123" y="59" width="8" height="20" rx="4" fill="#159dc7" opacity="0.9" />

        <path d="M35 65c0-26 18-43 45-43s45 17 45 43v8c0 25-20 38-45 38S35 98 35 73z" fill={`url(#${ids.shell})`} stroke="#8ff5ff" strokeWidth="4" />
        <path d="M42 66c0-21 15-32 38-32s38 11 38 32v7c0 18-15 27-38 27S42 91 42 73z" fill={`url(#${ids.visor})`} stroke="#68eefa" strokeWidth="3" />

        <path d="M55 62c6-11 13-18 25-22" stroke="#8feeff" strokeWidth="4" opacity="0.18" />
        <path d="M101 44c8 5 12 12 14 21" stroke="#ffffff" strokeWidth="3" opacity="0.08" />

        <path d="M56 69c5-7 11-8 16 0" fill="none" stroke="#61f7ff" strokeWidth="6" />
        <path d="M88 69c5-7 11-8 16 0" fill="none" stroke="#61f7ff" strokeWidth="6" />
        <circle cx="64" cy="68" r="2.2" fill="#eaffff" />
        <circle cx="96" cy="68" r="2.2" fill="#eaffff" />
        <path d="M71 82c6 6 12 6 18 0" fill="none" stroke="#3fe8f4" strokeWidth="4" />

        <path d="M50 92c8 7 18 10 30 10s22-3 30-10" fill="none" stroke="#ffffff" strokeWidth="3" opacity="0.72" />
      </g>
    </svg>
  </span>
}
