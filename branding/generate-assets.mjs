import fs from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(path.join(root, 'webapp/package.json'))
const sharp = require(process.env.AXIS_SHARP_PATH || 'sharp')
const source = await fs.readFile(path.join(root, 'branding/axis-mark.svg'), 'utf8')
const paths = [...source.matchAll(/<path\s+id="([^"]+)"\s+fill="([^"]+)"\s+d="([^"]+)"\s*\/>/g)]
if (paths.length !== 3) throw new Error('The master mark must contain exactly three paths')
const shapes = paths.map(([, id, fill, d]) => ({ id, fill, d }))
const navy = '#0B1220'
const svg = (body) => `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">\n${body}\n</svg>\n`
const mark = (mono = false) => shapes.map(({ id, fill, d }) => `  <path id="${id}" fill="${mono ? '#FFFFFF' : fill}" d="${d}" />`).join('\n')
const tile = (scale, radius = 112) => svg(`  <rect width="512" height="512" rx="${radius}" fill="${navy}" />\n  <g transform="translate(${256 * (1 - scale)} ${256 * (1 - scale)}) scale(${scale})">\n${mark()}\n  </g>`)
const write = async (relative, data) => {
  const target = path.join(root, relative)
  await fs.mkdir(path.dirname(target), { recursive: true })
  await fs.writeFile(target, data)
}
const png = (text, size) => sharp(Buffer.from(text)).resize(size, size).png().toBuffer()
const colour = svg(mark())
const mono = svg(mark(true))
const regular = tile(0.84)
const maskable = tile(0.78, 0)
const favicon = tile(0.9)

await write('branding/axis-mark-monochrome.svg', mono)
await write('branding/axis-icon.svg', regular)
await write('branding/axis-icon-maskable.svg', maskable)
await write('AxisApp/Resources/AppIcon/appicon.svg', svg(`  <rect width="512" height="512" fill="${navy}" />`))
await write('AxisApp/Resources/AppIcon/appiconfg.svg', colour)
await write('AxisApp/Resources/Splash/splash.svg', colour)
await write('AxisApp/Resources/Images/axis_mark.svg', colour)
await write('webapp/public/axis-mark.svg', colour)
await write('webapp/public/favicon.svg', favicon)
await write('webapp/public/email-assets/icon.svg', regular)
await write('webapp/public/email-assets/favicon.svg', favicon)

const whitePaths = shapes.map(({ d }) => `    <path android:fillColor="#FFFFFFFF" android:pathData="${d}" />`).join('\n')
await write('AxisApp/Platforms/Android/Resources/drawable/ic_stat_axis.xml', `<?xml version="1.0" encoding="utf-8"?>\n<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="24dp" android:height="24dp" android:viewportWidth="512" android:viewportHeight="512">\n${whitePaths}\n</vector>\n`)
for (const size of [192, 512]) await write(`webapp/public/icon-${size}.png`, await png(regular, size))
await write('webapp/public/icon-512-maskable.png', await png(maskable, 512))
await write('webapp/public/apple-touch-icon.png', await png(tile(0.84, 0), 180))
await write('webapp/public/favicon-32.png', await png(favicon, 32))
await write('webapp/public/notification-badge.png', await png(mono, 96))
await write('webapp/public/email-assets/icon.png', await png(regular, 256))
await write('webapp/public/email-assets/apple-touch-icon.png', await png(tile(0.84, 0), 180))
await write('branding/exports/google-play-512.png', await png(tile(0.84, 0), 512))
await write('branding/exports/axis-mark-512.png', await png(colour, 512))
await write('branding/exports/axis-notification-96.png', await png(mono, 96))

const sizes = [16, 32, 48]
const buffers = await Promise.all(sizes.map(size => png(favicon, size)))
const header = Buffer.alloc(6 + 16 * sizes.length)
header.writeUInt16LE(1, 2)
header.writeUInt16LE(sizes.length, 4)
let offset = header.length
buffers.forEach((buffer, index) => {
  const at = 6 + 16 * index
  header[at] = sizes[index]
  header[at + 1] = sizes[index]
  header.writeUInt16LE(1, at + 4)
  header.writeUInt16LE(32, at + 6)
  header.writeUInt32LE(buffer.length, at + 8)
  header.writeUInt32LE(offset, at + 12)
  offset += buffer.length
})
await write('webapp/public/favicon.ico', Buffer.concat([header, ...buffers]))
await write('webapp/public/email-assets/favicon.ico', Buffer.concat([header, ...buffers]))
console.log('Generated Axis branding for MAUI, web, notifications, email, and store exports.')
