import axios from 'axios'

export interface CardFlowMetrics {
  externalId: number
  title: string
  assignedTo: string | null
  currentStatus: string
  cycleTimeHours: number
  leadTimeHours: number
  timeInCurrentStatusHours: number
  isCycleTimeAboveBaseline: boolean
  isCurrentStatusBottleneck: boolean
}

export interface StatusBaselineStats {
  status: string
  meanHours: number
  stdDevHours: number
  sampleSize: number
  thresholdHours: number
  isStatisticallyDerived: boolean
}

export interface WeeklyThroughputPoint {
  weekStartUtc: string
  completedCount: number
}

export interface BottleneckAlert {
  externalId: number
  title: string
  assignedTo: string | null
  status: string
  hoursInStatus: number
  thresholdHours: number
  severity: 'Warning' | 'Critical'
}

export interface FlowMetricsPayload {
  iterationPath: string
  generatedAtUtc: string
  baselineWindowDays: number
  cards: CardFlowMetrics[]
  weeklyThroughput: WeeklyThroughputPoint[]
  statusBaselines: Record<string, StatusBaselineStats>
  bottleneckAlerts: BottleneckAlert[]
}

export interface FlowDiagnosticReport {
  generatedAtUtc: string
  metrics: FlowMetricsPayload
  bottleneckAlerts: BottleneckAlert[]
  aiDiagnosticSummary: string
  aiRecommendations: string[]
  aiInsightUnavailable: boolean
}

export interface FlowDiagnosticsQuery {
  /** Qualquer URL do Azure Boards da organização/projeto a analisar (work item, board, sprint, etc.). */
  boardsUrl: string
  /** Caminho da iteração/sprint (ex.: "Applied AI Engineering\Sprint 4"). */
  iterationPath: string
  /** Personal Access Token com acesso a essa organização. Nunca é persistido pelo composable. */
  personalAccessToken: string
}

export function useFlowDiagnostics() {
  const config = useRuntimeConfig()
  const report = useState<FlowDiagnosticReport | null>('flow-diagnostics-report', () => null)
  const isLoading = useState<boolean>('flow-diagnostics-loading', () => false)
  const errorMessage = useState<string | null>('flow-diagnostics-error', () => null)

  async function loadDiagnostics(query: FlowDiagnosticsQuery) {
    if (!query.boardsUrl?.trim()) {
      errorMessage.value = 'Cole a URL do Azure Boards da organização/projeto a analisar.'
      return
    }
    if (!query.iterationPath?.trim()) {
      errorMessage.value = 'Informe o caminho da iteração (sprint) para gerar o diagnóstico.'
      return
    }

    isLoading.value = true
    errorMessage.value = null
    try {
      const { data } = await axios.post<FlowDiagnosticReport>(
        `${config.public.apiBaseUrl}/api/flow-diagnostics`,
        {
          boardsUrl: query.boardsUrl,
          iterationPath: query.iterationPath,
          personalAccessToken: query.personalAccessToken || undefined
        }
      )
      report.value = data
    } catch (error: unknown) {
      const message = axios.isAxiosError(error)
        ? error.response?.data?.message ?? 'Não foi possível gerar o diagnóstico. Verifique se o backend está no ar.'
        : 'Não foi possível gerar o diagnóstico. Verifique se o backend está no ar.'
      errorMessage.value = message
      report.value = null
    } finally {
      isLoading.value = false
    }
  }

  return { report, isLoading, errorMessage, loadDiagnostics }
}
