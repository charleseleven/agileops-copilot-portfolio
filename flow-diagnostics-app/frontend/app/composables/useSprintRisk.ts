import axios from 'axios'

export type SprintRiskLevel = 'OnTrack' | 'AtRisk' | 'Critical'
export type SprintRiskSeverity = 'Info' | 'Warning' | 'Critical'
export type SprintTimelineStatus = 'NotStarted' | 'InProgress' | 'Ended'

export interface MemberLoad {
  member: string
  capacityPerDay: number
  availableWorkingDays: number
  remainingCapacityHours: number
  remainingWorkHours: number
  utilization: number | null
  severity: SprintRiskSeverity | null
}

export interface SprintRiskAlert {
  code: string
  severity: SprintRiskSeverity
  title: string
  message: string
  member: string | null
  workItemIds: number[]
}

export interface BurndownForecastPoint {
  date: string
  atObservedPaceHours: number
  atFullCapacityHours: number
}

export interface SprintRiskAnalysis {
  organization: string
  project: string
  team: string
  sprintName: string
  iterationPath: string
  startDate: string
  finishDate: string
  analysisDate: string
  timelineStatus: SprintTimelineStatus
  totalWorkingDays: number
  elapsedWorkingDays: number
  remainingWorkingDays: number
  totalEstimatedHours: number
  deliveredEstimatedHours: number
  completedWorkHours: number
  remainingWorkHours: number
  deliveryPercent: number
  teamDailyCapacityHours: number
  remainingCapacityHours: number
  capacityUtilization: number | null
  requiredDailyBurnHours: number | null
  observedDailyDeliveryHours: number | null
  projectedSlipWorkingDays: number | null
  hourTrackedItems: number
  doneItems: number
  pendingItems: number
  unestimatedItems: number
  unassignedRemainingHours: number
  riskLevel: SprintRiskLevel
  members: MemberLoad[]
  alerts: SprintRiskAlert[]
  forecast: BurndownForecastPoint[]
}

export interface SprintRiskReport {
  generatedAtUtc: string
  analysis: SprintRiskAnalysis
  aiSummary: string
  aiRecommendations: string[]
  aiInsightUnavailable: boolean
}

export interface SprintRiskQuery {
  /** Qualquer URL do Azure Boards da organização/projeto. */
  boardsUrl: string
  /** Time do Azure DevOps; vazio = time padrão do projeto. */
  team?: string
  /** Sprint; vazio = sprint atual do time. */
  iterationPath?: string
  /** PAT com acesso à organização; vazio = PAT configurado no servidor. Fica só em memória. */
  personalAccessToken?: string
}

/** Intervalos de atualização automática oferecidos no dashboard (segundos; 0 = desligado). */
export const REFRESH_OPTIONS = [0, 30, 60, 300] as const

/**
 * Integração do dashboard com o endpoint do Sprint Risk Bot (PBI 8.2, Task 2).
 *
 * "Tempo real": depois da primeira consulta, o dashboard pode reconsultar a API num intervalo.
 * Os refreshes automáticos pedem só a análise (sem chamar a LLM a cada ciclo) e preservam o último
 * parecer da IA; o botão "Atualizar com IA" gera um parecer novo. O PAT e a última consulta ficam
 * apenas em memória (nada vai para localStorage).
 */
export function useSprintRisk() {
  const config = useRuntimeConfig()
  const report = useState<SprintRiskReport | null>('sprint-risk-report', () => null)
  const isLoading = useState<boolean>('sprint-risk-loading', () => false)
  const isRefreshing = useState<boolean>('sprint-risk-refreshing', () => false)
  const errorMessage = useState<string | null>('sprint-risk-error', () => null)
  const lastUpdatedAt = useState<Date | null>('sprint-risk-updated', () => null)
  const aiGeneratedAt = useState<Date | null>('sprint-risk-ai-at', () => null)
  const refreshSeconds = useState<number>('sprint-risk-refresh-seconds', () => 60)

  let lastQuery: SprintRiskQuery | null = null
  let timer: ReturnType<typeof setInterval> | null = null

  async function request(query: SprintRiskQuery, includeAiInsight: boolean) {
    const { data } = await axios.post<SprintRiskReport>(`${config.public.apiBaseUrl}/api/sprint-risk`, {
      boardsUrl: query.boardsUrl.trim(),
      team: query.team?.trim() || undefined,
      iterationPath: query.iterationPath?.trim() || undefined,
      personalAccessToken: query.personalAccessToken?.trim() || undefined,
      includeAiInsight
    })
    return data
  }

  function describeError(error: unknown) {
    if (axios.isAxiosError(error)) {
      if (!error.response) return 'Não foi possível falar com o backend. Verifique se a API está no ar.'
      return error.response.data?.message ?? `Falha ao gerar o relatório (HTTP ${error.response.status}).`
    }
    return 'Falha inesperada ao gerar o relatório.'
  }

  /** Consulta completa (com parecer da IA). */
  async function load(query: SprintRiskQuery) {
    if (!query.boardsUrl?.trim()) {
      errorMessage.value = 'Cole a URL do Azure Boards da organização/projeto.'
      return
    }

    isLoading.value = true
    errorMessage.value = null
    try {
      report.value = await request(query, true)
      lastQuery = { ...query }
      lastUpdatedAt.value = new Date()
      aiGeneratedAt.value = new Date()
      restartTimer()
    } catch (error) {
      errorMessage.value = describeError(error)
      report.value = null
      stopTimer()
    } finally {
      isLoading.value = false
    }
  }

  /** Reconsulta silenciosa (sem IA), mantendo o último parecer. */
  async function refresh() {
    if (!lastQuery || isLoading.value || isRefreshing.value) return
    isRefreshing.value = true
    try {
      const fresh = await request(lastQuery, false)
      const previous = report.value
      report.value = {
        ...fresh,
        aiSummary: previous?.aiSummary ?? '',
        aiRecommendations: previous?.aiRecommendations ?? [],
        aiInsightUnavailable: previous?.aiInsightUnavailable ?? false
      }
      lastUpdatedAt.value = new Date()
      errorMessage.value = null
    } catch (error) {
      // Mantém o último relatório na tela e avisa; o próximo ciclo tenta de novo.
      errorMessage.value = `Atualização automática falhou: ${describeError(error)}`
    } finally {
      isRefreshing.value = false
    }
  }

  function setRefreshSeconds(seconds: number) {
    refreshSeconds.value = seconds
    restartTimer()
  }

  function restartTimer() {
    stopTimer()
    if (!import.meta.client || !lastQuery || refreshSeconds.value <= 0) return
    timer = setInterval(() => {
      // Não consulta com a aba em segundo plano; volta a atualizar quando o usuário retorna.
      if (document.visibilityState === 'visible') refresh()
    }, refreshSeconds.value * 1000)
  }

  function stopTimer() {
    if (timer) clearInterval(timer)
    timer = null
  }

  function reloadWithAi() {
    if (lastQuery) return load(lastQuery)
  }

  return {
    report,
    isLoading,
    isRefreshing,
    errorMessage,
    lastUpdatedAt,
    aiGeneratedAt,
    refreshSeconds,
    load,
    refresh,
    reloadWithAi,
    setRefreshSeconds,
    stopTimer
  }
}
