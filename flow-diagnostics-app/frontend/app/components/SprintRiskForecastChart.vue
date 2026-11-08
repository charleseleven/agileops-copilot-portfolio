<script setup lang="ts">
import type { BurndownForecastPoint } from '~/composables/useSprintRisk'

/**
 * Projeção do trabalho restante até o fim da sprint (SVG puro, escala com o container).
 * Se a linha "ritmo atual" não chega a zero no último dia, a sprint não fecha no prazo.
 */
const props = defineProps<{ points: BurndownForecastPoint[] }>()

const width = 640
const height = 240
const pad = { top: 16, right: 16, bottom: 32, left: 44 }
const plotW = width - pad.left - pad.right
const plotH = height - pad.top - pad.bottom

const maxHours = computed(() => {
  const max = Math.max(1, ...props.points.map((p) => Math.max(p.atObservedPaceHours, p.atFullCapacityHours)))
  // Arredonda o topo do eixo para um número "redondo".
  const step = max > 100 ? 50 : max > 40 ? 10 : 5
  return Math.ceil(max / step) * step
})

function x(index: number) {
  return pad.left + (props.points.length <= 1 ? plotW / 2 : (index / (props.points.length - 1)) * plotW)
}
function y(hours: number) {
  return pad.top + plotH - (hours / maxHours.value) * plotH
}

const pacePath = computed(() => props.points.map((p, i) => `${i ? 'L' : 'M'}${x(i)},${y(p.atObservedPaceHours)}`).join(' '))
const capacityPath = computed(() => props.points.map((p, i) => `${i ? 'L' : 'M'}${x(i)},${y(p.atFullCapacityHours)}`).join(' '))

const yTicks = computed(() => [0, 0.25, 0.5, 0.75, 1].map((f) => Math.round(maxHours.value * f)))

/** No celular há pouco espaço: mostra no máximo ~6 rótulos de data. */
const labelEvery = computed(() => Math.max(1, Math.ceil(props.points.length / 6)))

const leftoverAtEnd = computed(() => props.points.at(-1)?.atObservedPaceHours ?? 0)
</script>

<template>
  <div>
    <div class="mb-3 flex flex-wrap items-center gap-x-5 gap-y-2 text-xs text-slate-600 dark:text-slate-300">
      <span class="inline-flex items-center gap-2"><span class="h-0.5 w-5 rounded bg-rose-500" /> No ritmo atual</span>
      <span class="inline-flex items-center gap-2"><span class="h-0.5 w-5 rounded border-t-2 border-dashed border-brand-500" /> Usando toda a capacidade</span>
    </div>

    <div v-if="points.length < 2" class="rounded-xl border border-slate-200 bg-slate-50 px-4 py-6 text-center text-sm text-slate-500 dark:border-slate-800 dark:bg-slate-900 dark:text-slate-400">
      Sem dias úteis restantes para projetar.
    </div>

    <svg
      v-else
      :viewBox="`0 0 ${width} ${height}`"
      class="h-auto w-full"
      role="img"
      :aria-label="`Projeção do trabalho restante; no ritmo atual sobram ${formatHoursValue(leftoverAtEnd)} no último dia`"
    >
      <g class="text-slate-200 dark:text-slate-800">
        <line v-for="tick in yTicks" :key="tick" :x1="pad.left" :x2="width - pad.right" :y1="y(tick)" :y2="y(tick)" stroke="currentColor" stroke-width="1" />
      </g>
      <g class="fill-slate-400 dark:fill-slate-500" font-size="11">
        <text v-for="tick in yTicks" :key="`l${tick}`" :x="pad.left - 8" :y="y(tick) + 4" text-anchor="end">{{ tick }}h</text>
        <template v-for="(point, index) in points" :key="point.date">
          <text v-if="index % labelEvery === 0 || index === points.length - 1" :x="x(index)" :y="height - 10" text-anchor="middle">
            {{ formatDayMonth(point.date) }}
          </text>
        </template>
      </g>

      <path :d="capacityPath" fill="none" class="stroke-brand-500" stroke-width="2.5" stroke-dasharray="6 5" stroke-linecap="round" />
      <path :d="pacePath" fill="none" class="stroke-rose-500" stroke-width="3" stroke-linejoin="round" stroke-linecap="round" />

      <g v-for="(point, index) in points" :key="`p${point.date}`">
        <circle :cx="x(index)" :cy="y(point.atObservedPaceHours)" r="4" class="fill-rose-500">
          <title>{{ formatDayMonth(point.date) }}: {{ formatHoursValue(point.atObservedPaceHours) }} no ritmo atual · {{ formatHoursValue(point.atFullCapacityHours) }} usando toda a capacidade</title>
        </circle>
      </g>
    </svg>

    <p v-if="points.length >= 2" class="mt-2 text-xs text-slate-500 dark:text-slate-400">
      <template v-if="leftoverAtEnd > 0">
        No ritmo atual, sobram <strong class="text-rose-600 dark:text-rose-400">{{ formatHoursValue(leftoverAtEnd) }}</strong> no último dia da sprint.
      </template>
      <template v-else>No ritmo atual, o trabalho restante zera até o fim da sprint.</template>
    </p>
  </div>
</template>
