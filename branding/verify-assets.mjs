import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(path.join(root, 'webapp/package.json'))
const sharp = require(process.env.AXIS_SHARP_PATH || 'sharp')
const read = relative => fs.readFile(path.join(root, relative), 'utf8')
const master = await read('branding/axis-mark.svg')
const paths = [...master.matchAll(/<path\s+id="([^"]+)"\s+fill="([^"]+)"\s+d="([^"]+)"/g)]
assert.equal(paths.length, 3)
assert.deepEqual(paths.map(match => match[2]), ['#5B72D6', '#38D998', '#CBA257'])
const pathData = paths.map(match => match[3])
for (const relative of ['AxisApp/Resources/AppIcon/appiconfg.svg', 'AxisApp/Resources/Splash/splash.svg', 'AxisApp/Resources/Images/axis_mark.svg', 'webapp/public/axis-mark.svg']) {
  const text = await read(relative)
  assert.deepEqual([...text.matchAll(/ d="([^"]+)"/g)].map(match => match[1]), pathData, relative)
  assert(!text.includes('<rect'), `${relative} must have a transparent background`)
}
const vector = await read('AxisApp/Platforms/Android/Resources/drawable/ic_stat_axis.xml')
assert.deepEqual([...vector.matchAll(/android:pathData="([^"]+)"/g)].map(match => match[1]), pathData)
assert.equal((vector.match(/android:fillColor="#FFFFFFFF"/g) || []).length, 3)
assert((await read('AxisApp/Platforms/Android/AxisFirebaseMessagingService.cs')).includes('.SetSmallIcon(Resource.Drawable.ic_stat_axis)'))
assert((await read('AxisApp/Platforms/Android/NotificationActionReceiver.cs')).includes('.SetSmallIcon(Resource.Drawable.ic_stat_axis)'))
assert((await read('AxisApp/Platforms/Android/AndroidManifest.xml')).includes('android:resource="@drawable/ic_stat_axis"'))

const raster = async relative => sharp(path.join(root, relative)).ensureAlpha().raw().toBuffer({ resolveWithObject: true })
for (const [relative, size] of [['webapp/public/icon-192.png', 192], ['webapp/public/icon-512.png', 512], ['webapp/public/icon-512-maskable.png', 512], ['webapp/public/apple-touch-icon.png', 180], ['webapp/public/notification-badge.png', 96]]) {
  const { info } = await raster(relative)
  assert.equal(info.width, size, relative)
  assert.equal(info.height, size, relative)
}
const badge = await raster('webapp/public/notification-badge.png')
let opaque = 0, transparent = 0
for (let at = 0; at < badge.data.length; at += 4) {
  const alpha = badge.data[at + 3]
  if (alpha === 0) transparent++
  if (alpha > 0) {
    assert.deepEqual([...badge.data.subarray(at, at + 3)], [255, 255, 255], 'Notification badge must contain only white and transparency')
    opaque++
  }
}
assert(opaque > 0 && transparent > opaque, 'Notification badge must be a cutout, not a filled tile')
const maskable = await raster('webapp/public/icon-512-maskable.png')
for (let at = 0; at < maskable.data.length; at += 4) {
  assert.equal(maskable.data[at + 3], 255)
  const background = maskable.data[at] === 11 && maskable.data[at + 1] === 18 && maskable.data[at + 2] === 32
  if (!background) {
    const pixel = at / 4
    const dx = pixel % 512 - 255.5, dy = Math.floor(pixel / 512) - 255.5
    assert(dx * dx + dy * dy <= (512 * 0.4) ** 2, 'Maskable art must fit the central safe circle')
  }
}
const ico = await fs.readFile(path.join(root, 'webapp/public/favicon.ico'))
assert.equal(ico.readUInt16LE(2), 1)
assert.equal(ico.readUInt16LE(4), 3)
for (let i = 0; i < 3; i++) {
  const at = 6 + i * 16
  const size = ico.readUInt32LE(at + 8), offset = ico.readUInt32LE(at + 12)
  const meta = await sharp(ico.subarray(offset, offset + size)).metadata()
  assert.equal(meta.width, [16, 32, 48][i])
}
const manifest = JSON.parse(await read('webapp/public/manifest.json'))
for (const icon of manifest.icons) await fs.access(path.join(root, 'webapp/public', icon.src.split('?')[0]))
assert((await read('webapp/public/sw.js')).includes("badge: '/notification-badge.png?v=axis-brand-2'"))
assert(!(await read('webapp/index.html')).includes('vite.svg'))
if (process.argv.includes('--android-build')) {
  const foreground = await raster('AxisApp/obj/Debug/net10.0-android/resizetizer/r/mipmap-xxxhdpi/appicon_foreground.png')
  const size = foreground.info.width
  for (let at = 0; at < foreground.data.length; at += 4) {
    if (!foreground.data[at + 3]) continue
    const pixel = at / 4
    const dx = pixel % size - size / 2, dy = Math.floor(pixel / size) - size / 2
    assert(dx * dx + dy * dy <= (size * 33 / 108) ** 2, 'Built Android foreground must fit its adaptive safe circle')
  }
  console.log('Built Android adaptive foreground fits the 66dp safe circle.')
}
console.log('Brand checks passed: vector parity, white notification alpha, PNG/ICO sizes, maskable safe area, manifest and push wiring.')
