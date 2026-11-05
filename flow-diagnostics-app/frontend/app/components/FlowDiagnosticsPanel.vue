<script setup lang="ts">
import type { FlowDiagnosticReport } from '~/composables/useFlowDiagnostics'

const props = defineProps<{ report: FlowDiagnosticReport }>()

const cardsAboveBaseline = computed(() =>
  props.report.metrics.cards.filter((c) => c.isCycleTimeAboveBaseline).length
)

const activeBottlenecks = computed(() => props.report.bottleneckAlerts.length)

const latestThroughput = computed(() => {
  const points = props.report.metrics.weeklyThroughput
  return points.length > 0 ? points[points.length - 1]!.completedCount : 0
})

function severityBadgeClass(severity: string) {
  return severity === 'Critical'
    ? 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'
    : 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300'
}

function statusBadgeClass(isBottleneck: boolean, isAboveBaseline: boolean) {
  if (isBottleneck) return 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'
  if (isAboveBaseline) return 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300'
  return 'bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300'
}

function formatHours(hours: number) {
  if (hours < 24) return `${hours.toFixed(1)}h`
  return `${(hours / 24).toFixed(1)}d`
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })
}

function formatDateTime(iso: string) {
  return new Date(iso).toLocaleString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  })
}
</script>

<template>
  <div class="mt-8 space-y-6">
    <p class="font-mono text-xs text-slate-500 dark:text-slate-500">
      <i class="mdi mdi-clock-outline align-middle" /> Gerado em {{ formatDateTime(report.generatedAtUtc) }}
    </p>

    <!-- KPIs -->
    <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
      <div class="rounded-2xl border border-rose-200 bg-rose-50/60 p-5 transition-colors dark:border-rose-500/20 dark:bg-rose-500/5">
        <div class="flex items-center gap-2 text-rose-600 dark:text-rose-400">
          <i class="mdi mdi-alert-octagon-outline text-lg" />
          <span class="text-xs font-semibold uppercase tracking-wider">Gargalos ativos</span>
        </div>
        <p class="mt-2 text-3xl font-bold text-slate-900 dark:text-white">{{ activeBottlenecks }}</p>
      </div>
      <div class="rounded-2xl border border-amber-200 bg-amber-50/60 p-5 transition-colors dark:border-amber-500/20 dark:bg-amber-500/5">
        <div class="flex items-center gap-2 text-amber-600 dark:text-amber-400">
          <i class="mdi mdi-trending-up text-lg" />
          <span class="text-xs font-semibold uppercase tracking-wider">Acima da baseline</span>
        </div>
        <p class="mt-2 text-3xl font-bold text-slate-900 dark:text-white">{{ cardsAboveBaseline }}</p>
      </div>
      <div class="rounded-2xl border border-brand-200 bg-brand-50/60 p-5 transition-colors dark:border-brand-500/20 dark:bg-brand-500/5">
        <div class="flex items-center gap-2 text-brand-600 dark:text-brand-400">
          <i class="mdi mdi-chart-line text-lg" />
          <span class="text-xs font-semibold uppercase tracking-wider">Vazão da última semana</span>
        </div>
        <p class="mt-2 text-3xl font-bold text-slate-900 dark:text-white">{{ latestThroughput }}</p>
      </div>
    </div>

    <!-- Diagnóstico de IA -->
    <div class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm transition-colors dark:border-slate-800 dark:bg-slate-900/60">
      <div class="flex items-center gap-2 border-b border-slate-200 bg-slate-50 px-4 py-2.5 dark:border-slate-800 dark:bg-slate-900">
        <span class="h-2.5 w-2.5 rounded-full bg-rose-400/80" />
        <span class="h-2.5 w-2.5 rounded-full bg-amber-400/80" />
        <span class="h-2.5 w-2.5 rounded-full bg-emerald-400/80" />
        <span class="ml-2 font-mono text-xs text-slate-500 dark:text-slate-400">ai-insight.json</span>
      </div>
      <div class="p-5">
        <div class="mb-3 flex items-center gap-2 text-slate-900 dark:text-white">
          <i class="mdi mdi-robot-outline text-lg text-brand-500 dark:text-brand-400" />
          <h3 class="text-sm font-semibold uppercase tracking-wider">Diagnóstico gerado por IA</h3>
        </div>

        <div
          v-if="report.aiInsightUnavailable"
          class="flex items-start gap-3 rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-300"
        >
          <i class="mdi mdi-alert-outline mt-0.5 text-base" />
          <span>Não foi possível gerar o diagnóstico de IA neste momento. As métricas estatísticas abaixo continuam válidas.</span>
        </div>
        <template v-else>
          <p class="text-sm leading-relaxed text-slate-700 dark:text-slate-300">{{ report.aiDiagnosticSummary }}</p>
          <ul v-if="report.aiRecommendations.length" class="mt-4 space-y-2">
            <li
              v-for="(recommendation, index) in report.aiRecommendations"
              :key="index"
              class="flex items-start gap-2.5 text-sm text-slate-700 dark:text-slate-300"
            >
              <i class="mdi mdi-lightbulb-on-outline mt-0.5 text-base text-amber-500 dark:text-amber-400" />
              <span>{{ recommendation }}</span>
            </li>
          </ul>
        </template>
      </div>
    </div>

    <!-- Alertas de gargalo -->
    <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-slate-800 dark:bg-slate-900/60">
      <div class="mb-4 flex items-center gap-2 text-slate-900 dark:text-white">
        <i class="mdi mdi-alert-octagon-outline text-lg text-rose-500 dark:text-rose-400" />
        <h3 class="text-sm font-semibold uppercase tracking-wider">Alertas de gargalo por status e responsável</h3>
      </div>

      <div
        v-if="report.bottleneckAlerts.length === 0"
        class="flex items-center gap-3 rounded-xl border border-emerald-300 bg-emerald-50 px-4 py-3 text-sm text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-300"
      >
        <i class="mdi mdi-check-circle-outline text-base" />
        <span>Nenhum card ultrapassou o limite estatístico de permanência em status crítico.</span>
      </div>

      <template v-else>
        <!-- Desktop: tabela -->
        <div class="hidden overflow-hidden rounded-xl border border-slate-200 dark:border-slate-800 md:block">
          <table class="w-full text-left text-sm">
            <thead class="bg-slate-50 text-xs uppercase tracking-wider text-slate-500 dark:bg-slate-900 dark:text-slate-400">
              <tr>
                <th class="px-4 py-3 font-semibold">Card</th>
                <th class="px-4 py-3 font-semibold">Responsável</th>
                <th class="px-4 py-3 font-semibold">Status</th>
                <th class="px-4 py-3 font-semibold">Tempo no status</th>
                <th class="px-4 py-3 font-semibold">Limite</th>
                <th class="px-4 py-3 font-semibold">Severidade</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-200 dark:divide-slate-800">
              <tr v-for="alert in report.bottleneckAlerts" :key="alert.externalId" class="bg-white dark:bg-slate-900/40">
                <td class="px-4 py-3 font-medium text-slate-900 dark:text-slate-100">#{{ alert.externalId }} {{ alert.title }}</td>
                <td class="px-4 py-3 text-slate-600 dark:text-slate-400">{{ alert.assignedTo ?? 'Sem responsável' }}</td>
                <td class="px-4 py-3 font-mono text-xs text-slate-600 dark:text-slate-400">{{ alert.status }}</td>
                <td class="px-4 py-3 text-slate-600 dark:text-slate-400">{{ formatHours(alert.hoursInStatus) }}</td>
                <td class="px-4 py-3 text-slate-600 dark:text-slate-400">{{ formatHours(alert.thresholdHours) }}</td>
                <td class="px-4 py-3">
                  <span class="inline-flex items-center rounded-full px-2.5 py-1 text-xs font-semibold" :class="severityBadgeClass(alert.severity)">
                    {{ alert.severity === 'Critical' ? 'Crítico' : 'Atenção' }}
                  </span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Mobile: cards empilhados -->
        <div class="space-y-3 md:hidden">
          <div
            v-for="alert in report.bottleneckAlerts"
            :key="alert.externalId"
            class="rounded-xl border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900/40"
          >
            <div class="flex items-start justify-between gap-3">
              <p class="text-sm font-medium text-slate-900 dark:text-slate-100">#{{ alert.externalId }} {{ alert.title }}</p>
              <span class="shrink-0 inline-flex items-center rounded-full px-2.5 py-1 text-xs font-semibold" :class="severityBadgeClass(alert.severity)">
                {{ alert.severity === 'Critical' ? 'Crítico' : 'Atenção' }}
              </span>
            </div>
            <dl class="mt-3 grid grid-cols-2 gap-y-2 text-xs text-slate-500 dark:text-slate-400">
              <div><dt class="text-slate-400 dark:text-slate-500">Responsável</dt><dd class="text-slate-700 dark:text-slate-300">{{ alert.assignedTo ?? 'Sem responsável' }}</dd></div>
              <div><dt class="text-slate-400 dark:text-slate-500">Status</dt><dd class="font-mono text-slate-700 dark:text-slate-300">{{ alert.status }}</dd></div>
              <div><dt class="text-slate-400 dark:text-slate-500">Tempo no status</dt><dd class="text-slate-700 dark:text-slate-300">{{ formatHours(alert.hoursInStatus) }}</dd></div>
              <div><dt class="text-slate-400 dark:text-slate-500">Limite</dt><dd class="text-slate-700 dark:text-slate-300">{{ formatHours(alert.thresholdHours) }}</dd></div>
            </dl>
          </div>
        </div>
      </template>
    </div>

    <!-- Cards ativos -->
    <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-slate-800 dark:bg-slate-900/60">
      <div class="mb-4 flex items-center gap-2 text-slate-900 dark:text-white">
        <i class="mdi mdi-view-list-outline text-lg text-brand-500 dark:text-brand-400" />
        <h3 class="text-sm font-semibold uppercase tracking-wider">Cards ativos da sprint</h3>
      </div>

      <div v-if="report.metrics.cards.length === 0" class="rounded-xl border border-slate-200 bg-slate-50 px-4 py-6 text-center text-sm text-slate-500 dark:border-slate-800 dark:bg-slate-900 dark:text-slate-400">
        Nenhum card ativo encontrado para esta iteração.
      </div>

      <template v-else>
        <!-- Desktop: tabela -->
        <div class="hidden overflow-hidden rounded-xl border border-slate-200 dark:border-slate-800 md:block">
          <table class="w-full text-left text-sm">
            <thead class="bg-slate-50 text-xs uppercase tracking-wider text-slate-500 dark:bg-slate-900 dark:text-slate-400">
              <tr>
                <th class="px-4 py-3 font-semibold">Card</th>
                <th class="px-4 py-3 font-semibold">Responsável</th>
                <th class="px-4 py-3 font-semibold">Status atual</th>
                <th class="px-4 py-3 font-semibold">Cycle Time</th>
                <th class="px-4 py-3 font-semibold">Lead Time</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-slate-200 dark:divide-slate-800">
              <tr v-for="card in report.metrics.cards" :key="card.externalId" class="bg-white dark:bg-slate-900/40">
                <td class="px-4 py-3 font-medium text-slate-900 dark:text-slate-100">#{{ card.externalId }} {{ card.title }}</td>
                <td class="px-4 py-3 text-slate-600 dark:text-slate-400">{{ card.assignedTo ?? 'Sem responsável' }}</td>
                <td class="px-4 py-3">
                  <span class="inline-flex items-center rounded-full px-2.5 py-1 font-mono text-xs font-medium" :class="statusBadgeClass(card.isCurrentStatusBottleneck, card.isCycleTimeAboveBaseline)">
                    {{ card.currentStatus }}
                  </span>
                </td>
                <td class="px-4 py-3 text-slate-600 dark:text-slate-400">
                  {{ formatHours(card.cycleTimeHours) }}
                  <i v-if="card.isCycleTimeAboveBaseline" class="mdi mdi-trending-up ml-1 align-middle text-amber-500 dark:text-amber-400" title="Acima da baseline histórica" />
                </td>
                <td class="px-4 py-3 text-slate-600 dark:text-slate-400">{{ formatHours(card.leadTimeHours) }}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Mobile: cards empilhados -->
        <div class="space-y-3 md:hidden">
          <div
            v-for="card in report.metrics.cards"
            :key="card.externalId"
            class="rounded-xl border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900/40"
          >
            <div class="flex items-start justify-between gap-3">
              <p class="text-sm font-medium text-slate-900 dark:text-slate-100">#{{ card.externalId }} {{ card.title }}</p>
              <span class="shrink-0 inline-flex items-center rounded-full px-2.5 py-1 font-mono text-xs font-medium" :class="statusBadgeClass(card.isCurrentStatusBottleneck, card.isCycleTimeAboveBaseline)">
                {{ card.currentStatus }}
              </span>
            </div>
            <dl class="mt-3 grid grid-cols-2 gap-y-2 text-xs text-slate-500 dark:text-slate-400">
              <div><dt class="text-slate-400 dark:text-slate-500">Responsável</dt><dd class="text-slate-700 dark:text-slate-300">{{ card.assignedTo ?? 'Sem responsável' }}</dd></div>
              <div>
                <dt class="text-slate-400 dark:text-slate-500">Cycle Time</dt>
                <dd class="text-slate-700 dark:text-slate-300">
                  {{ formatHours(card.cycleTimeHours) }}
                  <i v-if="card.isCycleTimeAboveBaseline" class="mdi mdi-trending-up ml-1 align-middle text-amber-500 dark:text-amber-400" />
                </dd>
              </div>
              <div><dt class="text-slate-400 dark:text-slate-500">Lead Time</dt><dd class="text-slate-700 dark:text-slate-300">{{ formatHours(card.leadTimeHours) }}</dd></div>
            </dl>
          </div>
        </div>
      </template>
    </div>

    <!-- Vazão semanal -->
    <div class="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-slate-800 dark:bg-slate-900/60">
      <div class="mb-4 flex items-center gap-2 text-slate-900 dark:text-white">
        <i class="mdi mdi-calendar-week text-lg text-brand-500 dark:text-brand-400" />
        <h3 class="text-sm font-semibold uppercase tracking-wider">
          Vazão semanal <span class="font-normal normal-case text-slate-400 dark:text-slate-500">(baseline de {{ report.metrics.baselineWindowDays }} dias)</span>
        </h3>
      </div>

      <div v-if="report.metrics.weeklyThroughput.length === 0" class="rounded-xl border border-slate-200 bg-slate-50 px-4 py-6 text-center text-sm text-slate-500 dark:border-slate-800 dark:bg-slate-900 dark:text-slate-400">
        Sem itens concluídos na janela de baseline para calcular vazão.
      </div>
      <div v-else class="flex flex-wrap gap-3">
        <div
          v-for="point in report.metrics.weeklyThroughput"
          :key="point.weekStartUtc"
          class="min-w-[4.5rem] rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 text-center dark:border-slate-800 dark:bg-slate-900"
        >
          <p class="font-mono text-[0.65rem] uppercase tracking-wide text-slate-400 dark:text-slate-500">{{ formatDate(point.weekStartUtc) }}</p>
          <p class="text-lg font-bold text-slate-900 dark:text-white">{{ point.completedCount }}</p>
        </div>
      </div>
    </div>
  </div>
</template>
