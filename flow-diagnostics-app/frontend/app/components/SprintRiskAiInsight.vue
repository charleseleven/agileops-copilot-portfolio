<script setup lang="ts">
defineProps<{
  summary: string
  recommendations: string[]
  unavailable: boolean
  generatedAt: Date | null
  loading: boolean
}>()

defineEmits<{ regenerate: [] }>()
</script>

<template>
  <div class="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm transition-colors dark:border-slate-800 dark:bg-slate-900/60">
    <div class="flex flex-wrap items-center gap-2 border-b border-slate-200 bg-slate-50 px-4 py-2.5 dark:border-slate-800 dark:bg-slate-900">
      <i class="mdi mdi-robot-outline text-lg text-brand-500 dark:text-brand-400" />
      <h3 class="text-sm font-semibold uppercase tracking-wider text-slate-900 dark:text-white">Parecer do Sprint Risk Bot</h3>
      <span class="ml-auto font-mono text-xs text-slate-500 dark:text-slate-400">IA · {{ formatClock(generatedAt) }}</span>
      <button
        type="button"
        :disabled="loading"
        class="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 px-2.5 py-1 text-xs font-medium text-slate-600 transition hover:border-brand-400 hover:text-brand-600 disabled:opacity-50 dark:border-slate-700 dark:text-slate-300 dark:hover:text-brand-300"
        @click="$emit('regenerate')"
      >
        <i class="mdi mdi-refresh text-sm" :class="{ 'mdi-spin': loading }" /> Atualizar com IA
      </button>
    </div>

    <div class="p-5">
      <div
        v-if="unavailable"
        class="flex items-start gap-3 rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-700 dark:border-amber-500/30 dark:bg-amber-500/10 dark:text-amber-300"
      >
        <i class="mdi mdi-alert-outline mt-0.5 text-base" />
        <span>O parecer da IA não está disponível agora. Os indicadores e alertas continuam válidos: são calculados pelo backend, sem IA.</span>
      </div>
      <p v-else-if="!summary" class="text-sm text-slate-500 dark:text-slate-400">
        Ainda sem parecer da IA para esta análise. Clique em "Atualizar com IA" para gerar.
      </p>
      <template v-else>
        <p class="text-sm leading-relaxed text-slate-700 dark:text-slate-300">{{ summary }}</p>
        <ul v-if="recommendations.length" class="mt-4 space-y-2">
          <li v-for="(item, index) in recommendations" :key="index" class="flex items-start gap-2.5 text-sm text-slate-700 dark:text-slate-300">
            <i class="mdi mdi-lightbulb-on-outline mt-0.5 text-base text-amber-500 dark:text-amber-400" />
            <span>{{ item }}</span>
          </li>
        </ul>
      </template>
    </div>
  </div>
</template>
