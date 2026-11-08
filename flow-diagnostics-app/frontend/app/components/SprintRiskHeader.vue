<script setup lang="ts">
import type { SprintRiskAnalysis } from '~/composables/useSprintRisk'

const props = defineProps<{ analysis: SprintRiskAnalysis }>()

const meta = computed(() => riskLevelMeta[props.analysis.riskLevel])

const elapsedPercent = computed(() => {
  const { totalWorkingDays, elapsedWorkingDays } = props.analysis
  return totalWorkingDays > 0 ? Math.min(100, Math.round((elapsedWorkingDays / totalWorkingDays) * 100)) : 0
})

const deliveredPercent = computed(() => Math.min(100, Math.round(props.analysis.deliveryPercent)))

const criticalCount = computed(() => props.analysis.alerts.filter((a) => a.severity === 'Critical').length)
const warningCount = computed(() => props.analysis.alerts.filter((a) => a.severity === 'Warning').length)
</script>

<template>
  <section class="rounded-2xl border p-5 transition-colors sm:p-6" :class="meta.ring" aria-live="polite">
    <div class="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div class="min-w-0">
        <p class="font-mono text-xs text-slate-500 dark:text-slate-400">{{ analysis.team }}</p>
        <h2 class="mt-1 truncate text-xl font-extrabold tracking-tight text-slate-900 sm:text-2xl dark:text-white">
          {{ analysis.sprintName }}
        </h2>
        <p class="mt-1 text-sm text-slate-600 dark:text-slate-300">
          {{ formatFullDate(analysis.startDate) }} a {{ formatFullDate(analysis.finishDate) }}
          · {{ timelineLabel[analysis.timelineStatus] }}
          · {{ analysis.remainingWorkingDays }} dia(s) útil(eis) restante(s)
        </p>
      </div>

      <div class="flex shrink-0 flex-col items-start gap-2 sm:items-end">
        <span class="inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-sm font-bold shadow-sm" :class="meta.badge">
          <i :class="`mdi ${meta.icon}`" class="text-base" /> {{ meta.label }}
        </span>
        <span class="text-xs" :class="meta.text">
          {{ criticalCount }} crítico(s) · {{ warningCount }} de atenção
        </span>
      </div>
    </div>

    <!-- Tempo decorrido x entrega: o descompasso entre as duas barras é o sinal de risco. -->
    <div class="mt-5 grid grid-cols-1 gap-3 sm:grid-cols-2">
      <div>
        <div class="flex justify-between text-xs font-medium text-slate-600 dark:text-slate-300">
          <span>Tempo decorrido</span><span>{{ elapsedPercent }}%</span>
        </div>
        <div class="mt-1.5 h-2.5 overflow-hidden rounded-full bg-slate-200 dark:bg-slate-800" role="progressbar" :aria-valuenow="elapsedPercent" aria-valuemin="0" aria-valuemax="100" aria-label="Tempo decorrido da sprint">
          <div class="h-full rounded-full bg-slate-500 transition-all dark:bg-slate-400" :style="{ width: `${elapsedPercent}%` }" />
        </div>
      </div>
      <div>
        <div class="flex justify-between text-xs font-medium text-slate-600 dark:text-slate-300">
          <span>Horas Estimadas entregues</span><span>{{ deliveredPercent }}%</span>
        </div>
        <div class="mt-1.5 h-2.5 overflow-hidden rounded-full bg-slate-200 dark:bg-slate-800" role="progressbar" :aria-valuenow="deliveredPercent" aria-valuemin="0" aria-valuemax="100" aria-label="Horas estimadas entregues">
          <div
            class="h-full rounded-full transition-all"
            :class="deliveredPercent + 10 < elapsedPercent ? 'bg-rose-500' : 'bg-emerald-500'"
            :style="{ width: `${deliveredPercent}%` }"
          />
        </div>
      </div>
    </div>
  </section>
</template>
