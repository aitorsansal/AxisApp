import './Avatar.css'

// Light tints so the dark initials stay readable on every one; fixed, never accent-derived.
const AVATAR_TINTS = ['#E3A77F', '#7FC0DC', '#B9A2E6', '#9ED39B', '#E6C27A', '#F0A0A0', '#A6D6C8', '#D9A3C7']

// Stable per member: the same id always gets the same tint, so two people with identical
// initials still look different.
function tintFor(seed: string): string {
  let hash = 0
  for (let i = 0; i < seed.length; i++) hash = (hash * 31 + seed.charCodeAt(i)) | 0
  return AVATAR_TINTS[Math.abs(hash) % AVATAR_TINTS.length]
}

// Web equivalent of AxisApp/Controls/ProfileCircle — image-if-set-else-
// initials, same fallback order MemberDisplay.AvatarUrl already resolves.
export function Avatar({
  url,
  initials,
  seed,
  size = 36,
}: {
  url: string | null
  initials: string
  seed?: string | null
  size?: number
}) {
  return (
    <div
      className={`avatar${seed ? ' avatar-tinted' : ''}`}
      aria-hidden="true"
      style={{ width: size, height: size, fontSize: size * 0.4, background: seed ? tintFor(seed) : undefined }}
    >
      {url ? <img src={url} alt="" /> : <span>{initials}</span>}
    </div>
  )
}
