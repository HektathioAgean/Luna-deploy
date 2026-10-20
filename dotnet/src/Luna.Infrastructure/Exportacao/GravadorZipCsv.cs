using System.Globalization;
using System.IO.Compression;
using System.Text;
using Luna.Application;
using Luna.Domain;

namespace Luna.Infrastructure.Exportacao;

/// <summary>
/// <c>gravar_zip_csv</c> (src/engine.py): um CSV por saída, na ordem de SAIDAS,
/// cada um como o <c>df.to_csv(index=False, sep=";", encoding="utf-8-sig")</c> do
/// pandas 2.3.3 no Windows: BOM em cada arquivo, fim de linha CRLF, aspas só
/// quando o campo tem <c>;</c>, aspas ou quebra de linha, nulo vazio.
/// </summary>
internal static class GravadorZipCsv
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private const string FimDeLinha = "\r\n";

    public static void Gravar(IReadOnlyList<TabelaSaida> saidas, Stream destino, CancellationToken cancelamento)
    {
        using var zip = new ZipArchive(destino, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var tabela in saidas)
        {
            using var entrada = zip.CreateEntry(tabela.ArquivoCsv, CompressionLevel.Optimal).Open();
            entrada.Write(Bom);
            using var escritor = new StreamWriter(entrada, new UTF8Encoding(false), 1 << 16);
            Escrever(tabela, escritor, cancelamento);
        }
    }

    /// <summary>O texto do CSV de uma tabela (sem o BOM).</summary>
    internal static void Escrever(TabelaSaida tabela, TextWriter saida, CancellationToken cancelamento)
    {
        var formatos = Formatadores(tabela);
        EscreverRegistro(saida, tabela.Colunas.Select(c => c.Nome).ToArray());
        var campos = new string[tabela.Colunas.Count];
        int n = 0;
        foreach (var linha in tabela.Linhas())
        {
            if ((++n & 0x3FF) == 0)
                cancelamento.ThrowIfCancellationRequested();
            for (int j = 0; j < campos.Length; j++)
                campos[j] = linha[j] is { } valor ? formatos[j](valor) : "";
            EscreverRegistro(saida, campos);
        }
    }

    // writerow do módulo csv (QUOTE_MINIMAL): registro de um campo só e vazio
    // sai como "", para não virar linha em branco.
    private static void EscreverRegistro(TextWriter saida, string[] campos)
    {
        if (campos.Length == 1 && campos[0].Length == 0)
        {
            saida.Write("\"\"");
            saida.Write(FimDeLinha);
            return;
        }
        for (int j = 0; j < campos.Length; j++)
        {
            if (j > 0)
                saida.Write(';');
            string campo = campos[j];
            if (campo.AsSpan().IndexOfAny(";\"\r\n") < 0)
            {
                saida.Write(campo);
                continue;
            }
            saida.Write('"');
            saida.Write(campo.Replace("\"", "\"\""));
            saida.Write('"');
        }
        saida.Write(FimDeLinha);
    }

    /// <summary>
    /// Texto de cada célula não nula, pelo dtype da coluna (get_values_for_csv
    /// do pandas). datetime64 e timedelta64 dependem da coluna inteira, então
    /// essas colunas passam antes por uma leitura das linhas.
    /// </summary>
    private static Func<object, string>[] Formatadores(TabelaSaida tabela)
    {
        var colunas = tabela.Colunas;
        var datas = new EstadoDatas?[colunas.Count];
        var duracoes = new bool?[colunas.Count];
        for (int j = 0; j < colunas.Count; j++)
        {
            if (colunas[j] is { Tipo: TipoColuna.DataHora, Fuso: null })
                datas[j] = new EstadoDatas();
            else if (colunas[j].Tipo == TipoColuna.Duracao)
                duracoes[j] = false;
        }
        if (datas.Any(d => d is not null) || duracoes.Any(d => d is not null))
        {
            foreach (var linha in tabela.Linhas())
            {
                for (int j = 0; j < colunas.Count; j++)
                {
                    if (linha[j] is DateTime data)
                        datas[j]?.Observar(data);
                    else if (linha[j] is TimeSpan duracao && duracoes[j] is not null)
                        duracoes[j] |= duracao.Ticks % TimeSpan.TicksPerDay != 0;
                }
            }
        }

        var formatos = new Func<object, string>[colunas.Count];
        for (int j = 0; j < colunas.Count; j++)
        {
            var estado = datas[j];
            bool longo = duracoes[j] == true;
            formatos[j] = colunas[j].Tipo switch
            {
                TipoColuna.DataHora when colunas[j].Fuso is null => v => estado!.Formatar((DateTime)v),
                TipoColuna.DataHora => v => DataHoraComFuso((DateTimeOffset)v),
                TipoColuna.Duracao => v => Timedelta((TimeSpan)v, longo),
                _ => TextoPython.Str,
            };
        }
        return formatos;
    }

    /// <summary>
    /// datetime64[ns] sem fuso (format_array_from_datetime): só datas à
    /// meia-noite saem como <c>%Y-%m-%d</c>; senão data e hora, com a fração
    /// da menor resolução presente na coluna (ms, µs ou ns).
    /// </summary>
    private sealed class EstadoDatas
    {
        private bool _soMeiaNoite = true;
        private int _digitos; // 0, 3 (ms), 6 (µs) ou 9 (ns)

        public void Observar(DateTime data)
        {
            _soMeiaNoite &= data.TimeOfDay.Ticks == 0;
            long fracao = data.Ticks % TimeSpan.TicksPerSecond;
            int digitos = fracao % 10 != 0 ? 9 : fracao % 10_000 != 0 ? 6 : fracao != 0 ? 3 : 0;
            _digitos = Math.Max(_digitos, digitos);
        }

        public string Formatar(DateTime data)
        {
            var ci = CultureInfo.InvariantCulture;
            if (_soMeiaNoite)
                return data.ToString("yyyy-MM-dd", ci);
            string texto = data.ToString("yyyy-MM-dd HH:mm:ss", ci);
            long fracao = data.Ticks % TimeSpan.TicksPerSecond;
            return _digitos switch
            {
                9 => texto + "." + (fracao * 100).ToString("000000000", ci),
                6 => texto + "." + (fracao / 10).ToString("000000", ci),
                3 => texto + "." + (fracao / 10_000).ToString("000", ci),
                _ => texto,
            };
        }
    }

    /// <summary>datetime64 com fuso: <c>str(Timestamp)</c> de cada valor (fração só se houver; offset ±HH:MM).</summary>
    internal static string DataHoraComFuso(DateTimeOffset data)
    {
        var ci = CultureInfo.InvariantCulture;
        string texto = data.ToString("yyyy-MM-dd HH:mm:ss", ci);
        long fracao = data.Ticks % TimeSpan.TicksPerSecond;
        long micro = fracao / 10, nanos = fracao % 10 * 100;
        if (micro != 0)
            texto += "." + micro.ToString("000000", ci) + (nanos != 0 ? nanos.ToString("000", ci) : "");
        else if (nanos != 0)
            texto += "." + nanos.ToString("000000000", ci);
        var offset = data.Offset;
        return texto + (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", ci);
    }

    /// <summary>
    /// timedelta64[ns] (<c>Timedelta._repr_base</c>): dias com piso e resto
    /// positivo (<c>-1 days +23:00:00</c>). Coluna só de dias inteiros sai como
    /// <c>N days</c>; senão todas com hora (formato "long").
    /// </summary>
    internal static string Timedelta(TimeSpan duracao, bool longo)
    {
        var ci = CultureInfo.InvariantCulture;
        long ticks = duracao.Ticks;
        long dias = ticks >= 0 ? ticks / TimeSpan.TicksPerDay : -((-ticks + TimeSpan.TicksPerDay - 1) / TimeSpan.TicksPerDay);
        long resto = ticks - dias * TimeSpan.TicksPerDay;
        string texto = $"{dias.ToString(ci)} days";
        if (!longo)
            return texto;
        long segundos = resto / TimeSpan.TicksPerSecond, fracao = resto % TimeSpan.TicksPerSecond;
        texto += (ticks < 0 ? " +" : " ") +
            $"{(segundos / 3600).ToString("00", ci)}:{(segundos % 3600 / 60).ToString("00", ci)}:{(segundos % 60).ToString("00", ci)}";
        if (fracao != 0)
            texto += "." + (fracao / 10).ToString("000000", ci) + (fracao % 10 != 0 ? (fracao % 10 * 100).ToString("000", ci) : "");
        return texto;
    }
}
