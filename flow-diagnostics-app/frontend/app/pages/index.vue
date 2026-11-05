<script setup lang="ts">
const capabilities = [
  {
    key: 'mcp',
    badge: 'Epic #178',
    icon: 'mdi-connection',
    title: 'MCP Tooling',
    subtitle: 'Integração local com Azure DevOps Boards',
    description:
      'Servidor MCP (Model Context Protocol) que conecta um assistente de IA diretamente ao Azure DevOps: ler Work Items e atualizar prioridade, iteração, area path e estado por linguagem natural no terminal — sem abrir o navegador.',
    tech: 'Node.js · @modelcontextprotocol/sdk',
    linkLabel: 'Ver README (CLI/MCP, sem demo web)',
    linkHref: '../../mcp-tooling/README.md',
    cta: null
  },
  {
    key: 'gmud',
    badge: 'Epic #185',
    icon: 'mdi-source-branch-sync',
    title: 'GMUD Automation',
    subtitle: 'Orquestração de release ponta a ponta',
    description:
      'Automação do processo de GMUD (Gestão de Mudança): varre a User Story principal, identifica impacto técnico, cria a branch de release com merge das features e orquestra as Pull Requests — API/WEB automáticas, Database em aprovação do DBA.',
    tech: '.NET CLI · Azure Repos/Boards REST API',
    linkLabel: 'Ver README (CLI, sem demo web)',
    linkHref: '../../gmud-automation/README.md',
    cta: null
  },
  {
    key: 'flow',
    badge: 'Epic #209',
    icon: 'mdi-chart-bell-curve-cumulative',
    title: 'Flow Diagnostics',
    subtitle: 'Diagnóstico preditivo de gargalos de fluxo',
    description:
      'Cycle Time de cada card comparado à baseline histórica de 90 dias, alertas de gargalo por status/responsável e sugestões de mitigação geradas por IA — para qualquer organização do Azure DevOps.',
    tech: '.NET Web API · Nuxt/Vue + Tailwind',
    linkLabel: 'Abrir demo funcional',
    linkHref: '/flow-diagnostics',
    cta: 'Experimentar agora'
  }
]
</script>

<template>
  <div>
    <span class="inline-flex items-center gap-1.5 rounded-full border border-brand-400/30 bg-brand-50 px-3 py-1 font-mono text-xs font-medium text-brand-700 dark:bg-brand-500/10 dark:text-brand-300">
      <i class="mdi mdi-shield-check-outline text-sm" /> Portfólio de Scrum Master · Product Owner · Systems Analyst
    </span>

    <h1 class="mt-5 max-w-2xl text-4xl font-extrabold tracking-tight text-slate-900 dark:text-white sm:text-5xl">
      <span class="bg-gradient-to-r from-brand-500 to-sky-400 bg-clip-text text-transparent dark:from-brand-300 dark:to-sky-400">AgileOps Copilot</span>
    </h1>
    <p class="mt-4 max-w-2xl text-base leading-relaxed text-slate-600 dark:text-slate-300 sm:text-lg">
      Um copiloto de IA para as operações ágeis do dia a dia: automação da gestão de Work Items no
      Azure DevOps, orquestração de releases (GMUD) e diagnóstico preditivo de gargalos de fluxo —
      três capacidades já desenvolvidas e reunidas aqui sob uma identidade só.
    </p>

    <div class="mt-10 grid grid-cols-1 gap-5 md:grid-cols-3">
      <div
        v-for="capability in capabilities"
        :key="capability.key"
        class="flex flex-col rounded-2xl border border-slate-200 bg-white p-6 shadow-sm transition hover:border-brand-400/40 dark:border-slate-800 dark:bg-slate-900/60 dark:shadow-none"
      >
        <div class="flex items-center justify-between">
          <span class="inline-flex h-10 w-10 items-center justify-center rounded-xl bg-brand-50 text-brand-600 dark:bg-brand-500/10 dark:text-brand-300">
            <i :class="`mdi ${capability.icon}`" class="text-xl" />
          </span>
          <span class="font-mono text-xs text-slate-400 dark:text-slate-500">{{ capability.badge }}</span>
        </div>

        <h2 class="mt-4 text-lg font-bold text-slate-900 dark:text-white">{{ capability.title }}</h2>
        <p class="text-sm font-medium text-brand-600 dark:text-brand-300">{{ capability.subtitle }}</p>
        <p class="mt-3 flex-1 text-sm leading-relaxed text-slate-600 dark:text-slate-400">{{ capability.description }}</p>
        <p class="mt-4 font-mono text-xs text-slate-400 dark:text-slate-500">{{ capability.tech }}</p>

        <NuxtLink
          v-if="capability.cta"
          :to="capability.linkHref"
          class="mt-5 inline-flex items-center justify-center gap-2 rounded-lg bg-brand-500 px-4 py-2.5 text-sm font-semibold text-slate-950 transition hover:bg-brand-400"
        >
          <i class="mdi mdi-arrow-right-circle-outline text-base" />
          {{ capability.cta }}
        </NuxtLink>
        <a
          v-else
          :href="capability.linkHref"
          class="mt-5 inline-flex items-center gap-2 text-sm font-medium text-slate-500 transition hover:text-brand-600 dark:text-slate-400 dark:hover:text-brand-300"
        >
          <i class="mdi mdi-file-document-outline text-base" />
          {{ capability.linkLabel }}
        </a>
      </div>
    </div>
  </div>
</template>
