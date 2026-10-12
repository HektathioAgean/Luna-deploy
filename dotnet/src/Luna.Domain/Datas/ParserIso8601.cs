namespace Luna.Domain.Datas;

/// <summary>
/// Porta de <c>parse_iso_8601_datetime</c> do pandas 2.3.3
/// (pandas/_libs/src/vendored/numpy/datetime/np_datetime_strings.c), o
/// parser de <c>pd.to_datetime(..., format="ISO8601")</c>. A estrutura segue a
/// do C, passo a passo, inclusive o que ele aceita além do ISO estrito:
/// separadores <c>- . / \ espaço</c>, mês e dia com 1 dígito quando há
/// separador, <c>YYYYMMDD</c>, hora sozinha com 2 dígitos, fração de até 18
/// dígitos, <c>Z</c> maiúsculo e offset <c>±H</c>, <c>±HH</c>, <c>±HHMM</c>,
/// <c>±HH:MM</c>; espaço só no início, antes do fuso e depois do fuso.
/// Dígito e espaço são os do C (ASCII).
/// </summary>
internal static class ParserIso8601
{
    private static readonly char[] SeparadoresData = ['-', '.', '/', '\\', ' '];

    private static bool Digito(char c) => c is >= '0' and <= '9';

    private static bool Espaco(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    /// <summary>Lê o texto; falso quando o C devolveria erro de parse ou de faixa.</summary>
    public static bool TentarLer(string texto, out DataLida data)
    {
        data = default;
        int p = 0;
        int n = texto.Length;
        // Fim de string do C: além do texto, '\0'.
        char C(int i) => i < n ? texto[i] : '\0';

        int ano = 0, mes = 1, dia = 1, hora = 0, minuto = 0, segundo = 0;
        long us = 0, ps = 0, attos = 0;
        int? offset = null;

        while (p < n && Espaco(texto[p]))
            p++;

        if (C(p) == '-')
            p++;
        if (p >= n)
            return false;

        // Ano: 4 dígitos ou nenhum (fica 0, e a faixa do pandas rejeita depois).
        if (n - p >= 4 && Digito(texto[p]) && Digito(texto[p + 1]) && Digito(texto[p + 2]) && Digito(texto[p + 3]))
        {
            ano = 1000 * (texto[p] - '0') + 100 * (texto[p + 1] - '0') + 10 * (texto[p + 2] - '0') + (texto[p + 3] - '0');
            p += 4;
        }
        if (C(0) == '-')
            ano = -ano;

        if (p >= n)
            return Concluir(ano, mes, dia, hora, minuto, segundo, us, ps, offset, out data);

        bool temSeparadorData = false;
        char separadorData = '\0';
        if (!Digito(texto[p]))
        {
            if (Array.IndexOf(SeparadoresData, texto[p]) < 0)
                return false;
            temSeparadorData = true;
            separadorData = texto[p];
            p++;
            if (p >= n || !Digito(texto[p]))
                return false;
        }

        // Mês
        mes = texto[p] - '0';
        p++;
        if (Digito(C(p)))
        {
            mes = 10 * mes + (texto[p] - '0');
            p++;
        }
        else if (!temSeparadorData)
        {
            return false;
        }
        if (mes < 1 || mes > 12)
            return false;

        if (p >= n)
        {
            // YYYYMM é proibido; YYYY-MM vale.
            if (!temSeparadorData)
                return false;
            return Concluir(ano, mes, dia, hora, minuto, segundo, us, ps, offset, out data);
        }

        if (temSeparadorData)
        {
            if (texto[p] != separadorData || n - p == 1)
                return false;
            p++;
        }

        // Dia
        if (!Digito(C(p)))
            return false;
        dia = texto[p] - '0';
        p++;
        if (Digito(C(p)))
        {
            dia = 10 * dia + (texto[p] - '0');
            p++;
        }
        else if (!temSeparadorData)
        {
            return false;
        }
        if (dia < 1 || dia > DataLida.DiasNoMes(ano, mes))
            return false;

        if (p >= n)
            return Concluir(ano, mes, dia, hora, minuto, segundo, us, ps, offset, out data);

        if ((texto[p] != 'T' && texto[p] != ' ') || n - p == 1)
            return false;
        p++;

        // Hora
        if (!Digito(C(p)))
            return false;
        hora = texto[p] - '0';
        p++;
        bool horaCom2Digitos = false;
        if (Digito(C(p)))
        {
            horaCom2Digitos = true;
            hora = 10 * hora + (texto[p] - '0');
            p++;
            if (hora >= 24)
                return false;
        }

        if (p >= n)
        {
            if (!horaCom2Digitos)
                return false;
            return Concluir(ano, mes, dia, hora, minuto, segundo, us, ps, offset, out data);
        }

        bool temSeparadorHora = false;
        if (texto[p] == ':')
        {
            temSeparadorHora = true;
            p++;
            if (p >= n || !Digito(texto[p]))
                return false;
        }
        else if (!Digito(texto[p]))
        {
            if (!horaCom2Digitos)
                return false;
            return LerFuso(texto, ref p, ref offset) &&
                   Concluir(ano, mes, dia, hora, minuto, segundo, us, ps, offset, out data);
        }

        // Minutos
        minuto = texto[p] - '0';
        p++;
        if (Digito(C(p)))
        {
            minuto = 10 * minuto + (texto[p] - '0');
            p++;
            if (minuto >= 60)
                return false;
        }
        else if (!temSeparadorHora)
        {
            return false;
        }

        if (p >= n)
            return Concluir(ano, mes, dia, hora, minuto, segundo, us, ps, offset, out data);

        if (temSeparadorHora && texto[p] == ':')
        {
            p++;
            if (p >= n || !Digito(texto[p]))
                return false;
        }
        else if (!(!temSeparadorHora && Digito(texto[p])))
        {
            return LerFuso(texto, ref p, ref offset) &&
                   Concluir(ano, mes, dia, hora, minuto, segundo, us, ps, offset, out data);
        }

        // Segundos
        segundo = texto[p] - '0';
        p++;
        if (Digito(C(p)))
        {
            segundo = 10 * segundo + (texto[p] - '0');
            p++;
            if (segundo >= 60)
                return false;
        }
        else if (!temSeparadorHora)
        {
            return false;
        }

        if (p < n && texto[p] == '.')
        {
            p++;
            // Fração: até 6 dígitos de micro, 6 de pico e 6 de atto.
            us = LerFracao(texto, ref p);
            if (p < n && Digito(texto[p]))
            {
                ps = LerFracao(texto, ref p);
                if (p < n && Digito(texto[p]))
                    attos = LerFracao(texto, ref p);
            }
        }

        return LerFuso(texto, ref p, ref offset) &&
               Concluir(ano, mes, dia, hora, minuto, segundo, us, ps, offset, out data);
    }

    private static long LerFracao(string texto, ref int p)
    {
        long valor = 0;
        for (int i = 0; i < 6; i++)
        {
            valor *= 10;
            if (p < texto.Length && Digito(texto[p]))
            {
                valor += texto[p] - '0';
                p++;
            }
        }
        return valor;
    }

    /// <summary>Bloco parse_timezone do C, até o fim do texto.</summary>
    private static bool LerFuso(string texto, ref int p, ref int? offset)
    {
        int n = texto.Length;
        while (p < n && Espaco(texto[p]))
            p++;
        if (p >= n)
            return true;

        if (texto[p] == 'Z')
        {
            offset = 0;
            if (n - p == 1)
                return true;
            p++;
        }
        else if (texto[p] == '-' || texto[p] == '+')
        {
            bool negativo = texto[p] == '-';
            p++;
            int horas, minutos = 0;
            if (n - p >= 2 && Digito(texto[p]) && Digito(texto[p + 1]))
            {
                horas = 10 * (texto[p] - '0') + (texto[p + 1] - '0');
                p += 2;
                if (horas >= 24)
                    return false;
            }
            else if (n - p >= 1 && Digito(texto[p]))
            {
                horas = texto[p] - '0';
                p++;
            }
            else
            {
                return false;
            }

            if (p < n)
            {
                if (texto[p] == ':')
                    p++;
                if (n - p >= 2 && Digito(texto[p]) && Digito(texto[p + 1]))
                {
                    minutos = 10 * (texto[p] - '0') + (texto[p + 1] - '0');
                    p += 2;
                    if (minutos >= 60)
                        return false;
                }
                else if (n - p >= 1 && Digito(texto[p]))
                {
                    minutos = texto[p] - '0';
                    p++;
                }
                else
                {
                    return false;
                }
            }

            offset = negativo ? -(60 * horas + minutos) : 60 * horas + minutos;
        }

        while (p < n && Espaco(texto[p]))
            p++;
        return p == n;
    }

    private static bool Concluir(
        int ano, int mes, int dia, int hora, int minuto, int segundo, long us, long ps, int? offset, out DataLida data)
    {
        // Para nanossegundos: micro * 1000 + pico / 1000 (o resto é truncado).
        data = new DataLida(ano, mes, dia, hora, minuto, segundo, us * 1000 + ps / 1000, offset);
        return data.DentroDaFaixaDoPandas;
    }
}
