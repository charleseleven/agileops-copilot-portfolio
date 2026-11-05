// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: '2025-07-15',
  devtools: { enabled: true },
  modules: ['@nuxtjs/tailwindcss'],
  // Só a fonte de ícones do Material Design (usada pelos componentes via <i class="mdi mdi-...">),
  // sem o runtime do Vuetify — este projeto é 100% Tailwind.
  css: ['@mdi/font/css/materialdesignicons.css'],
  app: {
    head: {
      link: [
        { rel: 'preconnect', href: 'https://fonts.googleapis.com' },
        { rel: 'preconnect', href: 'https://fonts.gstatic.com', crossorigin: '' },
        {
          rel: 'stylesheet',
          href: 'https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&family=JetBrains+Mono:wght@400;500;600&display=swap'
        }
      ]
    }
  },
  runtimeConfig: {
    public: {
      apiBaseUrl: 'http://localhost:5250'
    }
  },
  vite: {
    optimizeDeps: {
      include: ['axios']
    }
  }
})
