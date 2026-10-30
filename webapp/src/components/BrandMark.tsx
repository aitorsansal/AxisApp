export function BrandMark({ size = 32 }: { size?: number }) {
  return (
    <span
      className="brand-mark"
      aria-hidden="true"
      style={{ width: size, height: size }}
    />
  )
}
