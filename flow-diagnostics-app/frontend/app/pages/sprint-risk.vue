<script setup lang="ts">
import { REFRESH_OPTIONS } from '~/composables/useSprintRisk'

useHead({ title: 'Sprint Risk · AgileOps Copilot' })

const {
  report, isLoading, isRefreshing, errorMessage, lastUpdatedAt, aiGeneratedAt, refreshSeconds,
  load, refresh, reloadWithAi, setRefreshSeconds, stopTimer
} = useSprintRisk()

const form = reactive({ boardsUrl: '', team: '', iterationPath: '', personalAccessToken: '' })
const showToken = ref(false)
const showAdvanced = ref(false)

onBeforeUnmount(stopTimer)

function submit() {
  load({ ...form })
}

function refreshLabel(seconds: number) {
  if (seconds === 0) return 'Manual'
  return seconds < 60 ? `${seconds}s` : `${seconds / 60} min`
}

const inputClass =
  'w-full rounded-lg border border-slate-300 bg-white px-3.5 py-2.5 text-sm text-slate-900 placeholder:text-slate-400 shadow-sm transition focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/30 dark:border-slate-700 dark:bg-slate-950 dark:text-slate-100 dark:placeholder:text-slate-600 dark:focus:border-brand-400 dark:focus:ring-brand-400/20'
</script>

<template>
  <div>
    <div class="max-w-2xl">
      <span class="inline-flex items-center gap-1.5 rounded-full border border-brand-300/60 bg-brand-50 px-3 py-1 font-mono text-xs font-medium text-brand-700 dark:border-brand-400/30 dark:bg-brand-500/10 dark:text-brand-300">
        <i class="mdi mdi-robot-outline text-sm" /> Feature 8 · Gestão de Projetos com IA
      </span>
      <h1 class="mt-4 text-3xl font-extrabold tracking-tight text-slate-900 sm:text-4xl dark:text-white">
        Sprint
        <span class="bg-gradient-to-r from-brand-500 to-sky-400 bg-clip-text text-transparent dark:from-brand-300 dark:to-sky-400">Risk Bot</span>
      </h1>
      <p class="mt-3 text-sm leading-relaxed text-slate-600 sm:text-base dark:text-slate-400">
        Dashboard executivo de risco da sprint ativa: cruza as Horas Estimadas entregues com a capacidade
        diária restante do time, aponta gargalos por membro e traz o parecer da IA para você agir antes do atraso.
      </p>
    </div>

    <!-- Consulta -->
    <form
      class="mt-8 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm transition-colors sm:p-6 dark:border-slate-800 dark:bg-slate-900/60"
      @submit.prevent="submit"
    >
      <div class="grid grid-cols-1 gap-5 md:grid-cols-2">
        <div class="md:col-span-2">
          <label for="sr-url" class="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">URL do Azure Boards</label>
          <input id="sr-url" v-model="form.boardsUrl" type="url" required placeholder="https://dev.azure.com/sua-organizacao/seu-projeto/_boards/board" :class="inputClass">
          <p class="mt-1.5 text-xs text-slate-500">Qualquer link do projeto (board, sprint, work item); organização e projeto são identificados automaticamente.</p>
        </div>

        <div>
          <label for="sr-pat" class="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">Personal Access Token</label>
          <div class="relative">
            <input
              id="sr-pat" v-model="form.personalAccessToken" :type="showToken ? 'text' : 'password'" autocomplete="off"
              placeholder="Opcional se o servidor tiver o PAT da organização" :class="[inputClass, 'pr-10']"
            >
            <button
              type="button" :aria-label="showToken ? 'Ocultar token' : 'Mostrar token'"
              class="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-slate-400 hover:text-brand-600 dark:hover:text-brand-300"
              @click="showToken = !showToken"
            >
              <i :class="showToken ? 'mdi mdi-eye-off' : 'mdi mdi-eye'" />
            </button>
          </div>
          <p class="mt-1.5 text-xs text-slate-500">Escopos de leitura: Work Items e Project and Team. Fica só na memória da página.</p>
        </div>

        <div class="flex items-end">
          <button
            type="button"
            class="inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 transition hover:text-brand-600 dark:text-slate-400 dark:hover:text-brand-300"
            :aria-expanded="showAdvanced"
            @click="showAdvanced = !showAdvanced"
          >
            <i :class="showAdvanced ? 'mdi mdi-chevron-up' : 'mdi mdi-chevron-down'" /> Time e sprint específicos
          </button>
        </div>

        <template v-if="showAdvanced">
          <div>
            <label for="sr-team" class="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">Time</label>
            <input id="sr-team" v-model="form.team" type="text" placeholder="Padrão: <Projeto> Team" :class="inputClass">
          </div>
          <div>
            <label for="sr-iteration" class="mb-1.5 block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">Sprint</label>
            <input id="sr-iteration" v-model="form.iterationPath" type="text" placeholder="Padrão: sprint atual do time" :class="[inputClass, 'font-mono']">
          </div>
        </template>
      </div>

      <button
        type="submit" :disabled="isLoading"
        class="mt-6 inline-flex w-full items-center justify-center gap-2 rounded-lg bg-brand-500 px-5 py-2.5 text-sm font-semibold text-slate-950 shadow-glow transition hover:bg-brand-400 focus:outline-none focus:ring-2 focus:ring-brand-400/50 disabled:cursor-not-allowed disabled:opacity-60 sm:w-auto"
      >
        <i :class="isLoading ? 'mdi mdi-loading mdi-spin' : 'mdi mdi-radar'" class="text-base" />
        Analisar risco da sprint
      </button>
    </form>

    <!-- Barra de atualização em tempo real -->
    <div
      v-if="report"
      class="mt-6 flex flex-col gap-3 rounded-xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm sm:flex-row sm:items-center dark:border-slate-800 dark:bg-slate-900"
    >
      <span class="flex items-center gap-2 text-slate-600 dark:text-slate-300">
        <span class="relative flex h-2.5 w-2.5">
          <span v-if="refreshSeconds > 0" class="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
          <span class="relative inline-flex h-2.5 w-2.5 rounded-full" :class="refreshSeconds > 0 ? 'bg-emerald-500' : 'bg-slate-400'" />
        </span>
        Atualizado às <strong class="font-mono">{{ formatClock(lastUpdatedAt) }}</strong>
        <i v-if="isRefreshing" class="mdi mdi-loading mdi-spin text-brand-500" aria-label="Atualizando" />
      </span>

      <div class="flex flex-wrap items-center gap-2 sm:ml-auto">
        <span class="text-xs text-slate-500 dark:text-slate-400">Atualização automática:</span>
        <div class="inline-flex overflow-hidden rounded-lg border border-slate-300 dark:border-slate-700" role="group" aria-label="Intervalo de atualização">
          <button
            v-for="seconds in REFRESH_OPTIONS" :key="seconds" type="button"
            class="px-2.5 py-1 text-xs font-medium transition"
            :class="refreshSeconds === seconds ? 'bg-brand-500 text-slate-950' : 'bg-white text-slate-600 hover:bg-slate-100 dark:bg-slate-950 dark:text-slate-300 dark:hover:bg-slate-800'"
            :aria-pressed="refreshSeconds === seconds"
            @click="setRefreshSeconds(seconds)"
          >
            {{ refreshLabel(seconds) }}
          </button>
        </div>
        <button
          type="button" :disabled="isRefreshing || isLoading"
          class="inline-flex items-center gap-1 rounded-lg border border-slate-300 bg-white px-2.5 py-1 text-xs font-medium text-slate-600 transition hover:border-brand-400 hover:text-brand-600 disabled:opacity-50 dark:border-slate-700 dark:bg-slate-950 dark:text-slate-300"
          @click="refresh"
        >
          <i class="mdi mdi-refresh" /> Agora
        </button>
      </div>
    </div>

    <div
      v-if="errorMessage"
      class="mt-6 flex items-start gap-3 rounded-xl border border-rose-300 bg-rose-50 px-4 py-3 text-sm text-rose-700 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-300"
      role="alert"
    >
      <i class="mdi mdi-alert-circle-outline mt-0.5 text-base" />
      <span>{{ errorMessage }}</span>
    </div>

    <div v-if="report" class="mt-6">
      <SprintRiskDashboard :report="report" :ai-generated-at="aiGeneratedAt" :loading="isLoading" @regenerate="reloadWithAi" />
    </div>

    <div
      v-else-if="!isLoading && !errorMessage"
      class="mt-6 flex items-start gap-3 rounded-xl border border-brand-300/60 bg-brand-50 px-4 py-3 text-sm text-brand-700 dark:border-brand-400/30 dark:bg-brand-500/10 dark:text-brand-300"
    >
      <i class="mdi mdi-information-outline mt-0.5 text-base" />
      <span>Informe a URL do Azure Boards e clique em "Analisar risco da sprint". Sem time e sprint, o bot analisa a sprint atual do time padrão.</span>
    </div>
  </div>
</template>
