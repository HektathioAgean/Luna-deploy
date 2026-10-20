using CommunityToolkit.Mvvm.ComponentModel;
using Luna.Application;

namespace Luna.Apresentacao;

/// <summary>Coluna do arquivo e o nome que ela recebeu na padronização.</summary>
public sealed record ColunaMapeada(string Original, string Padronizada);

/// <summary>Um motivo de inconsistência e quantas linhas o têm.</summary>
public sealed record ContagemDeMotivo(string Motivo, int Linhas);

/// <summary>
/// Aba Validação (interface/aba_validacao.js): o laudo do esquema e as
/// mensagens de inconsistência do último processamento.
/// </summary>
public sealed partial class ValidacaoViewModel : ObservableObject
{
    // suggest_missing_columns (src/schema.py).
    private static readonly Dictionary<string, string> Sugestoes = new(StringComparer.Ordinal)
    {
        ["Cod_Cliente"] = "Adicionar a coluna Cod_Cliente com o identificador do cliente.",
        ["Chegou_em"] = "Adicionar a coluna Chegou_em com data e hora de chegada.",
        ["Finalizada_em"] = "Adicionar a coluna Finalizada_em com data e hora de finalização.",
    };

    [ObservableProperty] public partial bool TemResultado { get; private set; }
    [ObservableProperty] public partial bool Valido { get; private set; }
    [ObservableProperty] public partial string NomeBase { get; private set; } = "";
    [ObservableProperty] public partial IReadOnlyList<string> ObrigatoriasEncontradas { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> ObrigatoriasAusentes { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> SugestoesDeCorrecao { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> OpcionaisEncontradas { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> ColunasDesconhecidas { get; private set; } = [];
    [ObservableProperty] public partial string? AvisoDeDuplicadas { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<ColunaMapeada> Mapeamento { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<string> AvisosDeLeitura { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<ContagemDeMotivo> InconsistenciasPorMotivo { get; private set; } = [];

    public void Atualizar(ResultadoProcessamento? resultado)
    {
        TemResultado = resultado is not null;
        var esquema = resultado?.Esquema;
        Valido = esquema?.Valido ?? false;
        NomeBase = resultado is null ? "" : Path.GetFileName(resultado.Parametros.Caminho);
        ObrigatoriasEncontradas = esquema?.ObrigatoriasEncontradas ?? [];
        ObrigatoriasAusentes = esquema?.ObrigatoriasFaltando ?? [];
        SugestoesDeCorrecao = ObrigatoriasAusentes
            .Select(c => Sugestoes.TryGetValue(c, out var s) ? s : $"Adicionar a coluna obrigatória: {c}")
            .ToList();
        OpcionaisEncontradas = esquema?.OpcionaisEncontradas ?? [];
        ColunasDesconhecidas = esquema?.ColunasDesconhecidas ?? [];
        AvisoDeDuplicadas = esquema is { ColunasDuplicadas.Count: > 0 }
            ? $"Colunas duplicadas após a padronização: {string.Join(", ", esquema.ColunasDuplicadas)}. " +
              "As repetições receberam o sufixo __dupN."
            : null;
        Mapeamento = resultado is null
            ? []
            : resultado.ColunasOriginais.Zip(resultado.ColunasPadronizadas, (o, p) => new ColunaMapeada(o, p)).ToList();
        AvisosDeLeitura = resultado?.Avisos ?? [];
        InconsistenciasPorMotivo = resultado is null
            ? []
            : resultado.Inconsistencias
                .GroupBy(i => i.MotivoInconsistencia, StringComparer.Ordinal)
                .Select(g => new ContagemDeMotivo(g.Key, g.Count()))
                .OrderByDescending(c => c.Linhas)
                .ThenBy(c => c.Motivo, StringComparer.Ordinal)
                .ToList();
    }
}
