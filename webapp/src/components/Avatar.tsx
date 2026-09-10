import './Avatar.css'

// Web equivalent of AxisApp/Controls/ProfileCircle — image-if-set-else-
// initials, same fallback order MemberDisplay.AvatarUrl already resolves.
export function Avatar({
  url,
  initials,
  size = 36,
}: {
  url: string | null
  initials: string
  size?: number
}) {
  return (
    <div className="avatar" style={{ width: size, height: size, fontSize: size * 0.4 }}>
      {url ? <img src={url} alt="" /> : <span>{initials}</span>}
    </div>
  )
}
