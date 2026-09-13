type ProviderLogoName = 'gmail' | 'microsoft' | 'imap' | 'outlook' | 'yahoo' | 'exchange'

export function MailProviderLogo({ provider, size = 38 }: { provider: ProviderLogoName; size?: number }) {
  if (provider === 'gmail') {
    return <svg className="mail-provider-logo" width={size} height={size} viewBox="0 0 48 48" aria-hidden="true">
      <path fill="#4285F4" d="M6 10.5 24 24 42 10.5V38a4 4 0 0 1-4 4h-5V20L24 27 15 20v22h-5a4 4 0 0 1-4-4Z" />
      <path fill="#34A853" d="M6 10.5 15 17v25H10a4 4 0 0 1-4-4Z" />
      <path fill="#FBBC04" d="M42 10.5 33 17v25h5a4 4 0 0 0 4-4Z" />
      <path fill="#EA4335" d="M6 10.5V9a4 4 0 0 1 6.4-3.2L24 14.5 35.6 5.8A4 4 0 0 1 42 9v1.5L24 24Z" />
    </svg>
  }

  if (provider === 'microsoft' || provider === 'exchange') {
    return <svg className="mail-provider-logo" width={size} height={size} viewBox="0 0 48 48" aria-hidden="true">
      <rect x="4" y="4" width="18" height="18" fill="#F25022" /><rect x="26" y="4" width="18" height="18" fill="#7FBA00" />
      <rect x="4" y="26" width="18" height="18" fill="#00A4EF" /><rect x="26" y="26" width="18" height="18" fill="#FFB900" />
    </svg>
  }

  if (provider === 'outlook') {
    return <svg className="mail-provider-logo" width={size} height={size} viewBox="0 0 48 48" aria-hidden="true">
      <rect x="5" y="10" width="24" height="28" rx="2" fill="#1473E6" /><rect x="20" y="7" width="23" height="34" rx="2" fill="#2B88D8" />
      <path fill="#fff" d="m20 18 11 8 11-8v4l-11 8-11-8Z" /><circle cx="17" cy="24" r="8" fill="#0A5DC2" /><text x="17" y="28" textAnchor="middle" fontSize="11" fontWeight="700" fill="#fff">O</text>
    </svg>
  }

  if (provider === 'yahoo') {
    return <span className="mail-provider-logo yahoo-provider-logo" style={{ width: size, height: size }} aria-hidden="true">Y!</span>
  }

  return <svg className="mail-provider-logo" width={size} height={size} viewBox="0 0 48 48" aria-hidden="true">
    <rect x="7" y="7" width="34" height="34" rx="8" fill="currentColor" opacity=".12" />
    <path d="M14 18h20M14 24h20M14 30h12" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
    <circle cx="33" cy="30" r="3" fill="currentColor" />
  </svg>
}
