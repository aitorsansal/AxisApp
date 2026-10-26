export function BrandMark({ size = 32 }: { size?: number }) {
  return (
    <img
      className="brand-mark"
      src="/axis-mark.svg"
      alt=""
      aria-hidden="true"
      width={size}
      height={size}
    />
  )
}
