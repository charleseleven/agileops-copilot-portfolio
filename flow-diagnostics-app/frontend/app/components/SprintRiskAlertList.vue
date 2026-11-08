<script setup lang="ts">
import type { SprintRiskAlert } from '~/composables/useSprintRisk'

const props = defineProps<{ alerts: SprintRiskAlert[]; organization: string; project: string }>()

/** Mostra até 6 links por alerta; o resto vira "+N" para não quebrar o layout no celular. */
const maxLinks = 6

const ordered = computed(() => {
  const weight = { Critical: 0, Warning: 1, Info: 2 } as const
  return [...props.alerts].sort((a, b) => weight[a.severity] - weight[b.severity])
})
</script>

<template>
  <div>
    <div
      v-if="alerts.length === 0"
      class="flex items-center gap-3 rounded-xl border border-emerald-300 bg-emerald-50 px-4 py-3 text-sm text-emerald-700 dark:border-emerald-500/30 dark:bg-emerald-500/10 dark:text-emerald-300"
    >
      <i class="mdi mdi-check-circle-outline text-base" />
      <span>Nenhum risco detectado: o trabalho restante cabe na capacidade e o ritmo de entrega está adequado.</span>
    </div>

    <ul v-else class="space-y-3">
      <li
        v-for="(alert, index) in ordered"
        :key="`${alert.code}-${alert.member ?? index}`"
        class="rounded-xl border border-l-4 border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900/40"
        :class="severityMeta[alert.severity].border"
      >
        <div class="flex flex-wrap items-start justify-between gap-2">
          <p class="flex min-w-0 items-center gap-2 text-sm font-semibold text-slate-900 dark:text-slate-100">
            <i :class="`mdi ${severityMeta[alert.severity].icon}`" class="text-base" />
            <span>{{ alert.title }}</span>
          </p>
          <span class="shrink-0 rounded-full px-2.5 py-0.5 text-xs font-semibold" :class="severityMeta[alert.severity].badge">
            {{ severityMeta[alert.severity].label }}
          </span>
        </div>
        <p class="mt-1.5 text-sm leading-relaxed text-slate-600 dark:text-slate-300">{{ alert.message }}</p>

        <div v-if="alert.workItemIds.length" class="mt-2.5 flex flex-wrap gap-1.5">
          <a
            v-for="id in alert.workItemIds.slice(0, maxLinks)"
            :key="id"
            :href="workItemUrl(organization, project, id)"
            target="_blank"
            rel="noopener noreferrer"
            class="rounded-md bg-slate-100 px-2 py-0.5 font-mono text-xs text-slate-600 transition hover:bg-brand-100 hover:text-brand-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-brand-500/20 dark:hover:text-brand-300"
          >#{{ id }}</a>
          <span v-if="alert.workItemIds.length > maxLinks" class="px-1 py-0.5 text-xs text-slate-500">
            +{{ alert.workItemIds.length - maxLinks }}
          </span>
        </div>
      </li>
    </ul>
  </div>
</template>
