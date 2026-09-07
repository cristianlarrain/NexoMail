import { nexiOfficialImage } from '../../assets/nexi/nexiOfficialImage'

export function NexiVisual({ size = 'medium', className = '' }: { size?: 'small' | 'medium' | 'large'; className?: string }) {
  return <span className={`nexi-visual ${size} ${className}`.trim()} aria-hidden="true">
    <img src={nexiOfficialImage} alt="" />
  </span>
}
