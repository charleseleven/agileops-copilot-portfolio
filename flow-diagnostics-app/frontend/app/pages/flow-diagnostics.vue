<script setup lang="ts">
const { report, isLoading, errorMessage, loadDiagnostics } = useFlowDiagnostics()

const boardsUrl = ref('')
const iterationPath = ref('')
const personalAccessToken = ref('')
const showToken = ref(false)

function handleSubmit() {
  loadDiagnostics({
    boardsUrl: boardsUrl.value,
    iterationPath: iterationPath.value,
    personalAccessToken: personalAccessToken.value
  })
}
</script>

<template>
  <div>
    <div class="max-w-2xl">
      <span class="inline-flex items-center gap-1.5 rounded-full border border-brand-300/60 bg-brand-50 px-3 py-1 font-mono text-xs font-medium text-brand-700 dark:border-brand-400/30 dark:bg-brand-500/10 dark:text-brand-300">
        <i class="mdi mdi-pulse text-sm" /> Epic #209 · Scrum Master Intelligence
      </span>
      <h1 class="mt-4 text-3xl font-extrabold tracking-tight text-slate-900 sm:text-4xl dark:text-white">
        Diagnóstico Preditivo de
        <span class="bg-gradient-to-r from-brand-500 to-sky-400 bg-clip-text text-transparent dark:from-brand-300 dark:to-sky-400">Fluxo</span>
      </h1>
      <p class="mt-3 text-sm leading-relaxed text-slate-600 sm:text-base dark:text-slate-400">
        Painel executivo do Scrum Master: Cycle Time por card comparado à baseline histórica,
        alertas de gargalo por status/responsável e sugestões de mitigação geradas por IA.
        Funciona com qualquer organização do Azure DevOps à qual você tenha acesso.
      </p>
    </div>

    <!-- Formulário -->
    <div class="mt-8 overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm transition-colors dark:border-slate-800 dark:bg-slate-900/60">
      <div class="flex items-center gap-2 border-b border-slate-200 bg-slate-50 px-4 py-2.5 dark:border-slate-800 dark:bg-slate-900">
        <span class="h-2.5 w-2.5 rounded-full bg-rose-400/80" />
        <span class="h-2.5 w-2.5 rounded-full bg-amber-400/80" />
        <span class="h-2.5 w-2.5 rounded-full bg-emerald-400/80" />
        <span class="ml-2 font-mono text-xs text-slate-500 dark:text-slate-400">flow-diagnostics.request.json</span>
      </div>

      <form class="p-5 sm:p-6" @submit.prevent="handleSubmit">
        <div class="grid grid-cols-1 gap-5 sm:grid-cols-2">
          <div class="sm:col-span-2">
            <label class="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
              URL do Azure Boards
            </label>
            <input
              v-model="boardsUrl"
              type="text"
              placeholder="https://dev.azure.com/sua-organizacao/seu-projeto/_boards/board"
              class="w-full rounded-lg border border-slate-300 bg-white px-3.5 py-2.5 text-sm text-slate-900 placeholder:text-slate-400 shadow-sm transition focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/30 dark:border-slate-700 dark:bg-slate-950 dark:text-slate-100 dark:placeholder:text-slate-600 dark:focus:border-brand-400 dark:focus:ring-brand-400/20"
            >
            <p class="mt-1.5 text-xs text-slate-500 dark:text-slate-500">
              Cole qualquer link do Azure Boards dessa organização (work item, board, sprint...) — organização e projeto são identificados automaticamente
            </p>
          </div>

          <div>
            <label class="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
              Caminho da iteração (sprint)
            </label>
            <input
              v-model="iterationPath"
              type="text"
              placeholder="Nome do Projeto\Sprint 4"
              class="w-full rounded-lg border border-slate-300 bg-white px-3.5 py-2.5 font-mono text-sm text-slate-900 placeholder:text-slate-400 shadow-sm transition focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/30 dark:border-slate-700 dark:bg-slate-950 dark:text-slate-100 dark:placeholder:text-slate-600 dark:focus:border-brand-400 dark:focus:ring-brand-400/20"
            >
            <p class="mt-1.5 text-xs text-slate-500 dark:text-slate-500">
              Iteration Path exato, ou cole a URL da sprint (taskboard/backlog) — o caminho é extraído automaticamente
            </p>
          </div>

          <div>
            <label class="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
              Personal Access Token
            </label>
            <div class="relative">
              <input
                v-model="personalAccessToken"
                :type="showToken ? 'text' : 'password'"
                placeholder="Necessário apenas se não houver um PAT padrão no servidor"
                class="w-full rounded-lg border border-slate-300 bg-white px-3.5 py-2.5 pr-10 text-sm text-slate-900 placeholder:text-slate-400 shadow-sm transition focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/30 dark:border-slate-700 dark:bg-slate-950 dark:text-slate-100 dark:placeholder:text-slate-600 dark:focus:border-brand-400 dark:focus:ring-brand-400/20"
              >
              <button
                type="button"
                :aria-label="showToken ? 'Ocultar token' : 'Mostrar token'"
                class="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-slate-400 transition hover:text-brand-600 dark:text-slate-500 dark:hover:text-brand-300"
                @click="showToken = !showToken"
              >
                <i :class="showToken ? 'mdi mdi-eye-off' : 'mdi mdi-eye'" />
              </button>
            </div>
            <p class="mt-1.5 text-xs text-slate-500 dark:text-slate-500">
              Enviado diretamente ao seu backend local; nunca é salvo pelo navegador
            </p>
          </div>
        </div>

        <button
          type="submit"
          :disabled="isLoading"
          class="mt-6 inline-flex items-center gap-2 rounded-lg bg-brand-500 px-5 py-2.5 text-sm font-semibold text-slate-950 shadow-glow transition hover:bg-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-400/50 disabled:cursor-not-allowed disabled:opacity-60"
        >
          <i v-if="isLoading" class="mdi mdi-loading mdi-spin text-base" />
          <i v-else class="mdi mdi-magnify text-base" />
          Gerar diagnóstico
        </button>
      </form>
    </div>

    <!-- Erro -->
    <div
      v-if="errorMessage"
      class="mt-6 flex items-start gap-3 rounded-xl border border-rose-300 bg-rose-50 px-4 py-3 text-sm text-rose-700 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-300"
    >
      <i class="mdi mdi-alert-circle-outline mt-0.5 text-base" />
      <span>{{ errorMessage }}</span>
    </div>

    <!-- Resultado -->
    <FlowDiagnosticsPanel v-if="report" :report="report" />

    <div
      v-else-if="!isLoading && !errorMessage"
      class="mt-6 flex items-start gap-3 rounded-xl border border-brand-300/60 bg-brand-50 px-4 py-3 text-sm text-brand-700 dark:border-brand-400/30 dark:bg-brand-500/10 dark:text-brand-300"
    >
      <i class="mdi mdi-information-outline mt-0.5 text-base" />
      <span>Informe a URL do Azure Boards e o caminho da iteração e clique em "Gerar diagnóstico" para analisar a sprint atual.</span>
    </div>
  </div>
</template>
