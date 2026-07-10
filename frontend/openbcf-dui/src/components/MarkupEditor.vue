<script setup lang="ts">
import { onMounted, ref } from 'vue'

const props = defineProps<{ imageDataUrl: string }>()
const emit = defineEmits<{ save: [dataUrl: string]; cancel: [] }>()

type Tool = 'cloud' | 'arrow' | 'line' | 'freehand' | 'text'
type Point = { x: number; y: number }
type Shape = { tool: Tool; color: string; width: number; points: Point[]; text?: string }

const canvas = ref<HTMLCanvasElement | null>(null)
const activeTool = ref<Tool | null>(null)
const color = ref('#e91e63')
// Snapshots are captured at ~1024px (see BcfViewpointCapture.RenderSnapshot in both host
// clients) but displayed at ~400px in the panel - a plain "3px" stroke becomes sub-pixel and
// unreadable once downscaled. These defaults are calibrated against that ~1024px canvas, not
// against on-screen CSS pixels.
const lineWidth = ref(10)

let baseImage: HTMLImageElement | null = null
let ctx: CanvasRenderingContext2D | null = null
const shapes: Shape[] = []
let inProgress: Shape | null = null

onMounted(() => {
  const image = new Image()
  image.onload = () => {
    baseImage = image
    const el = canvas.value!
    el.width = image.naturalWidth
    el.height = image.naturalHeight
    ctx = el.getContext('2d')
    redraw()
  }
  image.src = props.imageDataUrl
})

function redraw() {
  if (!ctx || !baseImage || !canvas.value) return
  ctx.clearRect(0, 0, canvas.value.width, canvas.value.height)
  ctx.drawImage(baseImage, 0, 0)
  for (const shape of shapes) drawShape(ctx, shape)
  if (inProgress) drawShape(ctx, inProgress)
}

function drawShape(c: CanvasRenderingContext2D, shape: Shape) {
  c.strokeStyle = shape.color
  c.fillStyle = shape.color
  c.lineWidth = shape.width
  c.lineCap = 'round'
  c.lineJoin = 'round'

  if (shape.tool === 'line' && shape.points.length >= 2) {
    c.beginPath()
    c.moveTo(shape.points[0].x, shape.points[0].y)
    c.lineTo(shape.points[1].x, shape.points[1].y)
    c.stroke()
  } else if (shape.tool === 'arrow' && shape.points.length >= 2) {
    drawArrow(c, shape.points[0], shape.points[1], shape.width)
  } else if (shape.tool === 'freehand' && shape.points.length >= 2) {
    c.beginPath()
    c.moveTo(shape.points[0].x, shape.points[0].y)
    for (const p of shape.points.slice(1)) c.lineTo(p.x, p.y)
    c.stroke()
  } else if (shape.tool === 'cloud' && shape.points.length >= 2) {
    drawCloud(c, shape.points[0], shape.points[1])
  } else if (shape.tool === 'text' && shape.points.length >= 1 && shape.text) {
    drawText(c, shape.points[0], shape.text, shape.color)
  }
}

function drawArrow(c: CanvasRenderingContext2D, p0: Point, p1: Point, width: number) {
  c.beginPath()
  c.moveTo(p0.x, p0.y)
  c.lineTo(p1.x, p1.y)
  c.stroke()

  const angle = Math.atan2(p1.y - p0.y, p1.x - p0.x)
  const headLen = Math.max(10, width * 4)
  c.beginPath()
  c.moveTo(p1.x, p1.y)
  c.lineTo(p1.x - headLen * Math.cos(angle - Math.PI / 7), p1.y - headLen * Math.sin(angle - Math.PI / 7))
  c.lineTo(p1.x - headLen * Math.cos(angle + Math.PI / 7), p1.y - headLen * Math.sin(angle + Math.PI / 7))
  c.closePath()
  c.fill()
}

// Approximates the classic BIM "revision cloud" as a ring of scalloped bumps around the
// ellipse inscribed in the drag rectangle - not geometrically exact, but reads clearly at
// snapshot resolution without needing a proper spline library.
//
// Bump count is derived from the ellipse's actual perimeter divided by a fixed target bump
// size, then clamped - NOT from (rx+ry) directly, which grows the bump count unboundedly as the
// drawn cloud gets bigger and produces a dense "spring" zigzag instead of a handful of readable
// scallops (the original bug: a large drag on a ~1024px snapshot canvas could produce 30+ bumps).
function drawCloud(c: CanvasRenderingContext2D, p0: Point, p1: Point) {
  const cx = (p0.x + p1.x) / 2
  const cy = (p0.y + p1.y) / 2
  const rx = Math.abs(p1.x - p0.x) / 2
  const ry = Math.abs(p1.y - p0.y) / 2
  if (rx < 4 || ry < 4) return

  // Ramanujan's ellipse perimeter approximation.
  const h = ((rx - ry) / (rx + ry)) ** 2
  const perimeter = Math.PI * (rx + ry) * (1 + (3 * h) / (10 + Math.sqrt(4 - 3 * h)))

  const targetBumpSize = 55
  const bumpCount = Math.min(20, Math.max(6, Math.round(perimeter / targetBumpSize)))
  const bumpRadius = (perimeter / bumpCount) * 0.4

  c.beginPath()
  for (let i = 0; i <= bumpCount; i++) {
    const a0 = (i / bumpCount) * Math.PI * 2
    const a1 = ((i + 1) / bumpCount) * Math.PI * 2
    const p0x = cx + rx * Math.cos(a0)
    const p0y = cy + ry * Math.sin(a0)
    const p1x = cx + rx * Math.cos(a1)
    const p1y = cy + ry * Math.sin(a1)
    const midX = (p0x + p1x) / 2 + Math.cos((a0 + a1) / 2) * bumpRadius
    const midY = (p0y + p1y) / 2 + Math.sin((a0 + a1) / 2) * bumpRadius
    if (i === 0) c.moveTo(p0x, p0y)
    c.quadraticCurveTo(midX, midY, p1x, p1y)
  }
  c.stroke()
}

// Deliberately NOT derived from the line-width dropdown - unlike a stroke, text has no
// "thickness" the user is thinking about while typing a label, so tying font size to whatever
// width happened to be selected produced small, easy-to-miss text whenever the width wasn't
// bumped up first (calibrated to the ~1024px snapshot canvas, same as everything else here).
// 56px still read as too small in practice once downscaled to the panel - sized up further.
const TEXT_FONT_SIZE = 80

function drawText(c: CanvasRenderingContext2D, p: Point, text: string, textColor: string) {
  c.font = `bold ${TEXT_FONT_SIZE}px "Segoe UI", sans-serif`
  const metrics = c.measureText(text)
  const paddingX = TEXT_FONT_SIZE * 0.3
  const boxWidth = metrics.width + paddingX * 2
  const boxHeight = TEXT_FONT_SIZE * 1.4

  c.fillStyle = 'rgba(255, 255, 255, 0.9)'
  c.fillRect(p.x, p.y - boxHeight, boxWidth, boxHeight)
  c.strokeStyle = textColor
  c.lineWidth = 4
  c.strokeRect(p.x, p.y - boxHeight, boxWidth, boxHeight)
  c.fillStyle = textColor
  c.fillText(text, p.x + paddingX, p.y - boxHeight * 0.25)
}

function eventToCanvasPoint(ev: PointerEvent): Point {
  const el = canvas.value!
  const rect = el.getBoundingClientRect()
  const scaleX = el.width / rect.width
  const scaleY = el.height / rect.height
  return { x: (ev.clientX - rect.left) * scaleX, y: (ev.clientY - rect.top) * scaleY }
}

function onPointerDown(ev: PointerEvent) {
  if (!activeTool.value) return
  const point = eventToCanvasPoint(ev)

  if (activeTool.value === 'text') {
    const text = window.prompt('Markup text:')
    if (text && text.trim()) {
      shapes.push({ tool: 'text', color: color.value, width: lineWidth.value, points: [point], text: text.trim() })
      redraw()
    }
    return
  }

  inProgress = { tool: activeTool.value, color: color.value, width: lineWidth.value, points: [point, point] }
  canvas.value!.setPointerCapture(ev.pointerId)
}

function onPointerMove(ev: PointerEvent) {
  if (!inProgress) return
  const point = eventToCanvasPoint(ev)
  if (inProgress.tool === 'freehand') {
    inProgress.points.push(point)
  } else {
    inProgress.points[1] = point
  }
  redraw()
}

function onPointerUp() {
  if (!inProgress) return
  shapes.push(inProgress)
  inProgress = null
  redraw()
}

function selectTool(tool: Tool) {
  activeTool.value = activeTool.value === tool ? null : tool
}

function clearAll() {
  shapes.length = 0
  redraw()
}

function save() {
  emit('save', canvas.value!.toDataURL('image/png'))
}
</script>

<template>
  <div class="markup-editor">
    <div class="markup-editor__toolbar">
      <input v-model="color" type="color" class="markup-editor__color" title="Markup color" />
      <select v-model.number="lineWidth" class="markup-editor__width" title="Line width">
        <option :value="6">Thin</option>
        <option :value="10">Medium</option>
        <option :value="16">Thick</option>
        <option :value="24">Extra thick</option>
      </select>

      <button
        type="button"
        :class="{ 'markup-editor__tool--active': activeTool === 'cloud' }"
        class="markup-editor__tool"
        title="Cloud markup"
        @click="selectTool('cloud')"
      >
        ☁ Cloud
      </button>
      <button
        type="button"
        :class="{ 'markup-editor__tool--active': activeTool === 'arrow' }"
        class="markup-editor__tool"
        title="Arrow markup"
        @click="selectTool('arrow')"
      >
        ↗ Arrow
      </button>
      <button
        type="button"
        :class="{ 'markup-editor__tool--active': activeTool === 'line' }"
        class="markup-editor__tool"
        title="Line markup"
        @click="selectTool('line')"
      >
        ╱ Line
      </button>
      <button
        type="button"
        :class="{ 'markup-editor__tool--active': activeTool === 'text' }"
        class="markup-editor__tool"
        title="Text markup"
        @click="selectTool('text')"
      >
        T Text
      </button>
      <button
        type="button"
        :class="{ 'markup-editor__tool--active': activeTool === 'freehand' }"
        class="markup-editor__tool"
        title="Draw markup"
        @click="selectTool('freehand')"
      >
        ✎ Draw
      </button>
      <button type="button" class="markup-editor__clear" title="Clear all markups" @click="clearAll">
        🗑 Clear all
      </button>
    </div>

    <div class="markup-editor__canvas-wrap">
      <canvas
        ref="canvas"
        class="markup-editor__canvas"
        @pointerdown="onPointerDown"
        @pointermove="onPointerMove"
        @pointerup="onPointerUp"
      ></canvas>
    </div>

    <div class="markup-editor__actions">
      <button type="button" @click="save">Use this snapshot</button>
      <button type="button" @click="emit('cancel')">Cancel</button>
    </div>
  </div>
</template>
