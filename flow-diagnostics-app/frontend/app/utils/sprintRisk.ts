import type { SprintRiskLevel, SprintRiskSeverity, SprintTimelineStatus } from '~/composables/useSprintRisk'

const number = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1 })

export function formatHoursValue(hours: number | null | undefined) {
  return hours == null ? '—' : `${number.format(hours)} h`
}

export function formatPercent(ratio: number | null | undefined) {
  return ratio == null ? '—' : `${number.format(ratio * 100)}%`
}

export function formatNumber(value: number | null | undefined) {
  return value == null ? '—' : number.format(value)
}

/** Datas do backend chegam como "yyyy-MM-dd" (DateOnly); evita o deslocamento de fuso do Date. */
export function formatDayMonth(isoDate: string) {
  const [, month, day] = isoDate.split('-')
  return `${day}/${month}`
}

export function formatFullDate(isoDate: string) {
  const [year, month, day] = isoDate.split('-')
  return `${day}/${month}/${year}`
}

export function formatClock(date: Date | null) {
  return date ? date.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' }) : '—'
}

export const riskLevelMeta: Record<SprintRiskLevel, { label: string; icon: string; badge: string; ring: string; text: string }> = {
  Critical: {
    label: 'Risco crítico',
    icon: 'mdi-alert-octagon',
    badge: 'bg-rose-500 text-white',
    ring: 'border-rose-300 bg-rose-50/70 dark:border-rose-500/30 dark:bg-rose-500/10',
    text: 'text-rose-700 dark:text-rose-300'
  },
  AtRisk: {
    label: 'Em risco',
    icon: 'mdi-alert',
    badge: 'bg-amber-400 text-slate-950',
    ring: 'border-amber-300 bg-amber-50/70 dark:border-amber-500/30 dark:bg-amber-500/10',
    text: 'text-amber-700 dark:text-amber-300'
  },
  OnTrack: {
    label: 'No prazo',
    icon: 'mdi-check-decagram',
    badge: 'bg-emerald-500 text-white',
    ring: 'border-emerald-300 bg-emerald-50/70 dark:border-emerald-500/30 dark:bg-emerald-500/10',
    text: 'text-emerald-700 dark:text-emerald-300'
  }
}

export const severityMeta: Record<SprintRiskSeverity, { label: string; icon: string; badge: string; border: string }> = {
  Critical: {
    label: 'Crítico',
    icon: 'mdi-alert-octagon-outline',
    badge: 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300',
    border: 'border-l-rose-500'
  },
  Warning: {
    label: 'Atenção',
    icon: 'mdi-alert-outline',
    badge: 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300',
    border: 'border-l-amber-400'
  },
  Info: {
    label: 'Info',
    icon: 'mdi-information-outline',
    badge: 'bg-sky-100 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300',
    border: 'border-l-sky-400'
  }
}

export const timelineLabel: Record<SprintTimelineStatus, string> = {
  NotStarted: 'Não iniciada',
  InProgress: 'Em andamento',
  Ended: 'Encerrada'
}

/** Cor da barra de utilização: verde até 85%, âmbar até 100%, vermelho acima. */
export function utilizationTone(utilization: number | null, hasWork: boolean) {
  if (utilization == null) return hasWork ? 'bg-rose-500' : 'bg-slate-300 dark:bg-slate-700'
  if (utilization >= 1) return 'bg-rose-500'
  if (utilization >= 0.85) return 'bg-amber-400'
  return 'bg-emerald-500'
}

export function workItemUrl(organization: string, project: string, id: number) {
  return `https://dev.azure.com/${encodeURIComponent(organization)}/${encodeURIComponent(project)}/_workitems/edit/${id}`
}
