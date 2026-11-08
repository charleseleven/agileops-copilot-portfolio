<script setup lang="ts">
import type { SprintRiskReport } from '~/composables/useSprintRisk'

const props = defineProps<{
  report: SprintRiskReport
  aiGeneratedAt: Date | null
  loading: boolean
}>()

defineEmits<{ regenerate: [] }>()

const a = computed(() => props.report.analysis)

const utilizationCardTone = computed(() => {
  const u = a.value.capacityUtilization
  if (u == null) return a.value.remainingWorkHours > 0 && a.value.teamDailyCapacityHours > 0 ? 'rose' : 'slate'
  return u >= 1 ? 'rose' : u >= 0.85 ? 'amber' : 'emerald'
})

const paceTone = computed(() => {
  const { observedDailyDeliveryHours: observed, requiredDailyBurnHours: required } = a.value
  if (observed == null || required == null || required === 0) return 'slate'
  return observed >= required ? 'emerald' : observed >= required * 0.9 ? 'amber' : 'rose'
})

const slipHint = computed(() => {
  const slip = a.value.projectedSlipWorkingDays
  if (slip == null) return 'sem ritmo observado ainda'
  if (slip > 0) return `atraso projetado de ${formatNumber(slip)} dia(s) útil(eis)`
  return 'fecha no prazo no ritmo atual'
})
</script>

<template>
  <div class="space-y-6">
    <SprintRiskHeader :analysis="a" />

    <!-- KPIs: 1 coluna no celular, 2 no tablet, 4 no desktop -->
    <div class="grid grid-cols-1 gap-4 min-[420px]:grid-cols-2 lg:grid-cols-4">
      <SprintRiskKpiCard
        label="Trabalho restante"
        icon="mdi-timer-sand"
        :value="formatHoursValue(a.remainingWorkHours)"
        :hint="`${a.pendingItems} item(ns) pendente(s)`"
        :tone="a.remainingWorkHours > a.remainingCapacityHours && a.teamDailyCapacityHours > 0 ? 'rose' : 'slate'"
      />
      <SprintRiskKpiCard
        label="Capacidade restante"
        icon="mdi-account-clock-outline"
        :value="formatHoursValue(a.remainingCapacityHours)"
        :hint="`${formatHoursValue(a.teamDailyCapacityHours)}/dia · ${a.remainingWorkingDays} dia(s)`"
        tone="brand"
      />
      <SprintRiskKpiCard
        label="Utilização"
        icon="mdi-gauge"
        :value="formatPercent(a.capacityUtilization)"
        :hint="a.capacityUtilization == null ? 'sem capacidade restante' : 'trabalho ÷ capacidade'"
        :tone="utilizationCardTone"
      />
      <SprintRiskKpiCard
        label="Ritmo de entrega"
        icon="mdi-speedometer"
        :value="a.observedDailyDeliveryHours == null ? '—' : `${formatNumber(a.observedDailyDeliveryHours)} h/dia`"
        :hint="a.requiredDailyBurnHours == null ? slipHint : `precisa de ${formatNumber(a.requiredDailyBurnHours)} h/dia · ${slipHint}`"
        :tone="paceTone"
      />
    </div>

    <SprintRiskAiInsight
      :summary="report.aiSummary"
      :recommendations="report.aiRecommendations"
      :unavailable="report.aiInsightUnavailable"
      :generated-at="aiGeneratedAt"
      :loading="loading"
      @regenerate="$emit('regenerate')"
    />

    <div class="grid grid-cols-1 gap-6 lg:grid-cols-3">
      <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition-colors lg:col-span-2 dark:border-slate-800 dark:bg-slate-900/60">
        <h3 class="mb-4 flex items-center gap-2 text-sm font-semibold uppercase tracking-wider text-slate-900 dark:text-white">
          <i class="mdi mdi-chart-line-variant text-lg text-brand-500 dark:text-brand-400" /> Projeção do trabalho restante
        </h3>
        <SprintRiskForecastChart :points="a.forecast" />
      </section>

      <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-slate-800 dark:bg-slate-900/60">
        <h3 class="mb-4 flex items-center gap-2 text-sm font-semibold uppercase tracking-wider text-slate-900 dark:text-white">
          <i class="mdi mdi-chart-donut text-lg text-emerald-500" /> Horas Estimadas
        </h3>
        <SprintRiskDeliveryDonut :delivered="a.deliveredEstimatedHours" :remaining="a.remainingWorkHours" />
        <p class="mt-4 text-xs text-slate-500 dark:text-slate-400">
          {{ a.doneItems }} de {{ a.hourTrackedItems }} itens entregues
          <template v-if="a.unestimatedItems"> · {{ a.unestimatedItems }} sem estimativa</template>
        </p>
      </section>
    </div>

    <div class="grid grid-cols-1 gap-6 lg:grid-cols-2">
      <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-slate-800 dark:bg-slate-900/60">
        <h3 class="mb-4 flex items-center gap-2 text-sm font-semibold uppercase tracking-wider text-slate-900 dark:text-white">
          <i class="mdi mdi-alert-octagon-outline text-lg text-rose-500 dark:text-rose-400" /> Alertas de risco
          <span class="ml-auto rounded-full bg-slate-100 px-2 py-0.5 font-mono text-xs text-slate-500 dark:bg-slate-800 dark:text-slate-400">{{ a.alerts.length }}</span>
        </h3>
        <SprintRiskAlertList :alerts="a.alerts" :organization="a.organization" :project="a.project" />
      </section>

      <section class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-slate-800 dark:bg-slate-900/60">
        <h3 class="mb-4 flex items-center gap-2 text-sm font-semibold uppercase tracking-wider text-slate-900 dark:text-white">
          <i class="mdi mdi-account-group-outline text-lg text-brand-500 dark:text-brand-400" /> Carga por membro
        </h3>
        <SprintRiskMemberLoad :members="a.members" />
      </section>
    </div>
  </div>
</template>

