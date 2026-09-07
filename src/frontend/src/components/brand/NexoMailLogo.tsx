export function NexoMailLogo({ compact = false }: { compact?: boolean }) {
  return <span className={`nexomail-logo ${compact ? 'compact' : ''}`} aria-hidden="true">
    <span className="nexomail-logo-mark">
      <svg viewBox="0 0 40 40" role="presentation">
        <rect className="nexomail-logo-tile" x="2" y="2" width="36" height="36" rx="11" />
        <path className="nexomail-logo-n" d="M10.5 27V13.5L29.5 27V13.5" />
        <path className="nexomail-logo-flap" d="M10.5 14.5 20 21.2l9.5-6.7" />
      </svg>
    </span>
    {!compact && <span className="nexomail-wordmark"><strong>Nexo</strong><span>Mail</span></span>}
  </span>
}
