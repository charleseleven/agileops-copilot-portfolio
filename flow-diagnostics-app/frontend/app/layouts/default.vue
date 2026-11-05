<script setup lang="ts">
const { isDark, toggle, init } = useColorScheme('agileops-theme')
onMounted(init)

const mobileOpen = ref(false)

const navLinks = [
  { to: '/', label: 'Home' },
  { to: '/flow-diagnostics', label: 'Flow Diagnostics' }
]
</script>

<template>
  <div :class="isDark ? 'dark' : ''">
    <div class="relative min-h-screen bg-white font-sans transition-colors dark:bg-slate-950">
      <!-- fundo decorativo em grade de pontos, fixo e cobrindo a viewport inteira -->
      <div class="pointer-events-none fixed inset-0 bg-grid-light opacity-60 [background-size:18px_18px] dark:bg-grid-dark" />

      <div class="relative">
        <header class="sticky top-0 z-20 border-b border-slate-200 bg-white/80 backdrop-blur transition-colors dark:border-slate-800 dark:bg-slate-950/80">
          <nav class="mx-auto flex max-w-6xl items-center gap-4 px-4 py-3 sm:px-6">
            <NuxtLink to="/" class="flex items-center gap-2 text-sm font-bold tracking-tight text-slate-900 dark:text-white">
              <i class="mdi mdi-robot-excited-outline text-lg text-brand-500 dark:text-brand-400" />
              AgileOps Copilot
            </NuxtLink>

            <div class="ml-auto flex items-center gap-2 sm:hidden">
              <button
                type="button"
                :aria-label="isDark ? 'Mudar para modo claro' : 'Mudar para modo escuro'"
                class="inline-flex h-8 w-8 items-center justify-center rounded-md text-slate-500 dark:text-slate-400"
                @click="toggle"
              >
                <i :class="isDark ? 'mdi mdi-weather-sunny' : 'mdi mdi-weather-night'" class="text-lg" />
              </button>
              <button
                type="button"
                class="inline-flex h-8 w-8 items-center justify-center rounded-md text-slate-500 dark:text-slate-400"
                aria-label="Abrir menu"
                @click="mobileOpen = !mobileOpen"
              >
                <i :class="mobileOpen ? 'mdi mdi-close' : 'mdi mdi-menu'" class="text-xl" />
              </button>
            </div>

            <div class="ml-auto hidden items-center gap-5 text-sm font-medium text-slate-600 dark:text-slate-300 sm:flex">
              <NuxtLink
                v-for="link in navLinks"
                :key="link.to"
                :to="link.to"
                class="transition hover:text-slate-900 dark:hover:text-white"
                active-class="!text-slate-900 dark:!text-white"
              >
                {{ link.label }}
              </NuxtLink>

              <button
                type="button"
                :aria-label="isDark ? 'Mudar para modo claro' : 'Mudar para modo escuro'"
                :title="isDark ? 'Mudar para modo claro' : 'Mudar para modo escuro'"
                class="inline-flex h-8 w-8 items-center justify-center rounded-full border border-slate-300 text-slate-600 transition hover:border-brand-400 hover:text-brand-600 dark:border-slate-700 dark:text-slate-300 dark:hover:border-brand-400 dark:hover:text-brand-300"
                @click="toggle"
              >
                <i :class="isDark ? 'mdi mdi-weather-sunny' : 'mdi mdi-weather-night'" class="text-base" />
              </button>
            </div>
          </nav>

          <div v-if="mobileOpen" class="border-t border-slate-200 bg-white px-4 py-3 dark:border-slate-800 dark:bg-slate-950 sm:hidden">
            <div class="flex flex-col gap-3 text-sm font-medium text-slate-600 dark:text-slate-300">
              <NuxtLink
                v-for="link in navLinks"
                :key="link.to"
                :to="link.to"
                class="transition hover:text-slate-900 dark:hover:text-white"
                active-class="!text-slate-900 dark:!text-white"
                @click="mobileOpen = false"
              >
                {{ link.label }}
              </NuxtLink>
            </div>
          </div>
        </header>

        <main class="mx-auto max-w-6xl px-4 py-8 sm:px-6 sm:py-10">
          <slot />
        </main>
      </div>
    </div>
  </div>
</template>
