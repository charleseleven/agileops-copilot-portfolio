<script setup lang="ts">
/** Horas Estimadas entregues x restantes (rosca em SVG). */
const props = defineProps<{ delivered: number; remaining: number }>()

const radius = 52
const circumference = 2 * Math.PI * radius

const total = computed(() => props.delivered + props.remaining)
const deliveredRatio = computed(() => (total.value > 0 ? props.delivered / total.value : 0))
const dash = computed(() => `${deliveredRatio.value * circumference} ${circumference}`)
</script>

<template>
  <div class="flex flex-col items-center gap-4 sm:flex-row sm:justify-center lg:flex-col xl:flex-row">
    <svg viewBox="0 0 140 140" class="h-36 w-36 shrink-0 -rotate-90" role="img" :aria-label="`${formatPercent(deliveredRatio)} das horas entregues`">
      <circle cx="70" cy="70" :r="radius" fill="none" class="stroke-slate-200 dark:stroke-slate-800" stroke-width="16" />
      <circle
        cx="70" cy="70" :r="radius" fill="none" class="stroke-emerald-500 transition-all duration-700"
        stroke-width="16" stroke-linecap="round" :stroke-dasharray="dash"
      />
      <text x="70" y="70" class="rotate-90 fill-slate-900 dark:fill-white" style="transform-origin: 70px 70px" text-anchor="middle" dominant-baseline="central" font-size="22" font-weight="700">
        {{ total > 0 ? Math.round(deliveredRatio * 100) + '%' : '—' }}
      </text>
    </svg>

    <dl class="grid grid-cols-2 gap-x-6 gap-y-2 text-sm sm:grid-cols-1">
      <div>
        <dt class="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400"><span class="h-2.5 w-2.5 rounded-full bg-emerald-500" /> Entregues</dt>
        <dd class="font-semibold text-slate-900 dark:text-white">{{ formatHoursValue(delivered) }}</dd>
      </div>
      <div>
        <dt class="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400"><span class="h-2.5 w-2.5 rounded-full bg-slate-300 dark:bg-slate-700" /> Restantes</dt>
        <dd class="font-semibold text-slate-900 dark:text-white">{{ formatHoursValue(remaining) }}</dd>
      </div>
    </dl>
  </div>
</template>
