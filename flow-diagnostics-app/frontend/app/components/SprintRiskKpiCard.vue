<script setup lang="ts">
type Tone = 'rose' | 'amber' | 'emerald' | 'brand' | 'slate'

const props = withDefaults(defineProps<{
  label: string
  value: string
  icon: string
  hint?: string
  tone?: Tone
}>(), { tone: 'slate', hint: undefined })

const toneClasses: Record<Tone, { box: string; icon: string }> = {
  rose: { box: 'border-rose-200 bg-rose-50/60 dark:border-rose-500/20 dark:bg-rose-500/5', icon: 'text-rose-600 dark:text-rose-400' },
  amber: { box: 'border-amber-200 bg-amber-50/60 dark:border-amber-500/20 dark:bg-amber-500/5', icon: 'text-amber-600 dark:text-amber-400' },
  emerald: { box: 'border-emerald-200 bg-emerald-50/60 dark:border-emerald-500/20 dark:bg-emerald-500/5', icon: 'text-emerald-600 dark:text-emerald-400' },
  brand: { box: 'border-brand-200 bg-brand-50/60 dark:border-brand-500/20 dark:bg-brand-500/5', icon: 'text-brand-600 dark:text-brand-400' },
  slate: { box: 'border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900/60', icon: 'text-slate-500 dark:text-slate-400' }
}

const classes = computed(() => toneClasses[props.tone])
</script>

<template>
  <div class="min-w-0 rounded-2xl border p-4 transition-colors sm:p-5" :class="classes.box">
    <div class="flex items-center gap-2" :class="classes.icon">
      <i :class="`mdi ${icon}`" class="text-lg" />
      <span class="truncate text-[0.7rem] font-semibold uppercase tracking-wider sm:text-xs">{{ label }}</span>
    </div>
    <p class="mt-2 text-2xl font-bold text-slate-900 sm:text-3xl dark:text-white">{{ value }}</p>
    <p v-if="hint" class="mt-1 text-xs text-slate-500 dark:text-slate-400">{{ hint }}</p>
  </div>
</template>
