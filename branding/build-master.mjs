import fs from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const require = createRequire(path.join(root, 'webapp/package.json'))
const paper = require(process.env.AXIS_PAPER_PATH || 'paper/dist/paper-core.js')
paper.setup(new paper.Size(512, 512))

const scale = 7.6
const gap = 2.6
const rx = 24
const ry = 8
const depth = 9
const px = (x) => 256 + (x - 50) * scale
const py = (y) => 256 + (y - 56.5) * scale
const ellipse = (x, y, a, b) => new paper.Path.Ellipse({ center: [px(x), py(y)], radius: [a * scale, b * scale] })
const rect = (x, y, half) => new paper.Path.Rectangle({ from: [px(x - half), py(y)], to: [px(x + half), py(y + depth)] })
const silhouette = (x, y, grow = 0) => ellipse(x, y, rx + grow, ry + grow).unite(rect(x, y, rx + grow)).unite(ellipse(x, y + depth, rx + grow, ry + grow))
const ring = (x, y) => ellipse(x, y, rx + gap / 2, ry + gap / 2).subtract(ellipse(x, y, rx - gap / 2, ry - gap / 2))

const coins = [
  { id: 'blue', fill: '#5B72D6', x: 48, y: 66 },
  { id: 'amber', fill: '#CBA257', x: 53, y: 52 },
  { id: 'mint', fill: '#38D998', x: 47, y: 38 }
]
const shape = (coin, index) => {
  let result = silhouette(coin.x, coin.y).subtract(ring(coin.x, coin.y))
  for (const upper of coins.slice(index + 1)) result = result.subtract(silhouette(upper.x, upper.y, gap))
  return result
}
const shapes = Object.fromEntries(coins.map((coin, index) => [coin.id, shape(coin, index)]))
const data = (item) => item.pathData.replace(/-?\d+\.?\d*(?:e-?\d+)?/gi, (n) => String(Number(Number(n).toFixed(2))))
const order = ['blue', 'mint', 'amber']
const lines = order.map((id) => `  <path id="${id}" fill="${coins.find((coin) => coin.id === id).fill}" d="${data(shapes[id])}" />`)
const bounds = order.map((id) => shapes[id].bounds)
const all = bounds.reduce((a, b) => a.unite(b))
console.log(`bounds x ${all.left.toFixed(2)}..${all.right.toFixed(2)} y ${all.top.toFixed(2)}..${all.bottom.toFixed(2)}`)
await fs.writeFile(path.join(root, 'branding/axis-mark.svg'), `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">\n${lines.join('\n')}\n</svg>\n`)
