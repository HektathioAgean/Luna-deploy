// @ts-check
// Aba Processamento: os 8 KPIs, o resumo da execucao e os parametros usados.

import { aviso, bloco, el, ficha, metricas, secao } from './componentes.js';
import { painelProgresso } from './aba_base.js';
import { estado } from './state.js';

/** @param {number} valor */
function numero(valor) {
  return Number(valor || 0).toLocaleString('pt-BR');
}

export function abaProcessamento() {
  const resultado = estado.resultado;
  if (!resultado) {
    return el('div', { classe: 'pagina' }, [
      el('h1', { texto: 'Processamento' }),
      secao('', [aviso('neutro', 'Processe uma base para ver os KPIs.')]),
    ]);
  }

  const k = resultado.kpis;
  const p = resultado.parametros;

  return el('div', { classe: 'pagina' }, [
    el('h1', { texto: 'Processamento' }),
    el('p', { classe: 'linha-apoio', texto: resultado.caminho }),

    secao('KPIs', [
      metricas([
        { rotulo: 'Linhas válidas', valor: numero(k.linhas_validas) },
        { rotulo: 'Inconsistências', valor: numero(k.inconsistencias) },
        { rotulo: 'Expurgados', valor: numero(k.expurgados) },
        { rotulo: 'Anomalias', valor: numero(k.anomalias) },
        { rotulo: 'Clientes únicos', valor: numero(k.clientes_unicos) },
        { rotulo: 'Mediana global', valor: k.mediana_global_fmt },
        { rotulo: 'Tempo ideal global Q1', valor: k.tempo_ideal_q1_global_fmt },
        { rotulo: 'Outliers Boxplot', valor: numero(k.outliers_boxplot) },
      ]),
    ], 'ic-painel'),

    secao('Resumo', [
      bloco([ficha({
        'Arquivo': resultado.nome_base,
        'Linhas brutas': numero(resultado.linhas_brutas),
        'Linhas válidas': numero(k.linhas_validas),
        'Gap global mediana × Q1': k.gap_mediana_q1_global_fmt,
        'Inconsistências': numero(k.inconsistencias),
        'Expurgados': numero(k.expurgados),
        'Anomalias': numero(k.anomalias),
        'Base detalhada': numero(resultado.contagens.base_detalhada),
      })]),
    ]),

    secao('Parâmetros usados', [
      bloco([ficha({
        'Tempo mínimo de expurgo': `${numero(p.tempo_min_expurgo)} s`,
        'Tempo máximo para anomalia': `${numero(p.tempo_max_anomalia)} s`,
        'Eventos prévios': p.eventos_previos,
        'Mínimo de apontamentos': p.minimo_apontamentos,
        'Tempo padrão (poucos apontamentos)': `${numero(p.tempo_padrao_poucos_apontamentos)} s`,
        'Ajuste percentual': `${p.ajuste_percentual}%`,
      })]),
    ], 'ic-relogio'),

    secao('Janelas de entrega', [
      bloco([
        p.exibir_janelas_entrega
          ? ficha({
              'Exibir janelas': 'Sim',
              'Cobertura da janela': `${p.cobertura_janela}%`,
              'Base da janela': p.base_janela,
              'Janelas geradas': numero(resultado.contagens.janelas_entrega),
            })
          : el('p', { classe: 'tabela-vazia',
              texto: 'Cálculo de janelas desligado neste processamento.' }),
      ]),
    ], 'ic-relogio'),

    painelProgresso(),
  ]);
}
