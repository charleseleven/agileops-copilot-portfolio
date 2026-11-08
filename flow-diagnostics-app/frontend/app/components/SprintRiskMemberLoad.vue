<script setup lang="ts">
import type { MemberLoad } from '~/composables/useSprintRisk'

/** Trabalho restante x capacidade restante por membro (barras horizontais, responsivas por natureza). */
const props = defineProps<{ members: MemberLoad[] }>()

const scale = computed(() => Math.max(1, ...props.members.map((m) => Math.max(m.remainingWorkHours, m.remainingCapacityHours))))

function widthOf(hours: number) {
  return `${Math.min(100, (hours / scale.value) * 100)}%`
}
</script>

<template>
  <div>
    <div v-if="members.length === 0" class="rounded-xl border border-slate-200 bg-slate-50 px-4 py-6 text-center text-sm text-slate-500 dark:border-slate-800 dark:bg-slate-900 dark:text-slate-400">
      Nenhum membro com capacidade cadastrada ou trabalho atribuído nesta sprint.
    </div>

    <ul v-else class="space-y-4">
      <li v-for="member in members" :key="member.member">
        <div class="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
          <span class="min-w-0 truncate text-sm font-medium text-slate-900 dark:text-slate-100">{{ member.member }}</span>
          <span class="text-xs text-slate-500 dark:text-slate-400">
            {{ formatHoursValue(member.remainingWorkHours) }} / {{ formatHoursValue(member.remainingCapacityHours) }}
            <span
              class="ml-1 rounded-full px-2 py-0.5 font-semibold"
              :class="member.severity ? severityMeta[member.severity].badge : 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300'"
            >
              {{ member.utilization != null ? formatPercent(member.utilization) : 'sem capacidade' }}
            </span>
          </span>
        </div>
        <!-- Trilho = capacidade restante; barra = trabalho restante. Passar do trilho é sobrecarga. -->
        <div class="relative mt-1.5 h-3 rounded-full bg-slate-100 dark:bg-slate-800" :title="`${member.availableWorkingDays} dia(s) disponível(is) × ${formatHoursValue(member.capacityPerDay)}/dia`">
          <div class="absolute inset-y-0 left-0 rounded-full border border-slate-300 bg-slate-200/70 dark:border-slate-600 dark:bg-slate-700/60" :style="{ width: widthOf(member.remainingCapacityHours) }" />
          <div
            class="absolute inset-y-0.5 left-0 rounded-full transition-all"
            :class="utilizationTone(member.utilization, member.remainingWorkHours > 0)"
            :style="{ width: widthOf(member.remainingWorkHours) }"
          />
        </div>
      </li>
    </ul>

    <p v-if="members.length" class="mt-4 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-500 dark:text-slate-400">
      <span class="inline-flex items-center gap-1.5"><span class="h-2.5 w-4 rounded border border-slate-300 bg-slate-200 dark:border-slate-600 dark:bg-slate-700" /> Capacidade restante</span>
      <span class="inline-flex items-center gap-1.5"><span class="h-2.5 w-4 rounded bg-emerald-500" /> Trabalho restante</span>
    </p>
  </div>
</template>
