namespace AgilePredict.Models.DTOs.Flow
{
    /// <summary>
    /// Estatística histórica (linha de base) de permanência em um status, calculada a partir
    /// dos itens concluídos dentro da janela de baseline (padrão: últimos 90 dias).
    /// </summary>
    public class StatusBaselineStats
    {
        public string Status { get; set; } = string.Empty;

        public double MeanHours { get; set; }

        public double StdDevHours { get; set; }

        public int SampleSize { get; set; }

        /// <summary>
        /// Limite (em horas) acima do qual a permanência no status é considerada estatisticamente
        /// anômala: MeanHours + (StdDevMultiplier * StdDevHours), ou o limite padrão configurado
        /// quando a amostra histórica é insuficiente.
        /// </summary>
        public double ThresholdHours { get; set; }

        public bool IsStatisticallyDerived { get; set; }
    }
}
