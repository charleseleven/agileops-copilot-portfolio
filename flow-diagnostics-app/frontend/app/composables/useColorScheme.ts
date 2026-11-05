export function useColorScheme(storageKey: string) {
  const isDark = useState<boolean>(`color-scheme:${storageKey}`, () => true)

  function apply(value: boolean) {
    isDark.value = value
    if (import.meta.client) {
      try {
        localStorage.setItem(storageKey, value ? 'dark' : 'light')
      } catch {
        // Ambiente sem acesso a localStorage (ex.: navegação privada) — preferência só não persiste.
      }
    }
  }

  function toggle() {
    apply(!isDark.value)
  }

  function init() {
    if (!import.meta.client) return
    try {
      const stored = localStorage.getItem(storageKey)
      if (stored === 'light' || stored === 'dark') {
        isDark.value = stored === 'dark'
      }
    } catch {
      // Mantém o valor padrão (dark) se não for possível ler a preferência salva.
    }
  }

  return { isDark, toggle, init }
}
