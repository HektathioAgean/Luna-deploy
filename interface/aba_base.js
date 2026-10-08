// @ts-check
// Aba Base: entrada (caminho + parametros), progresso da execucao e a
// pre-visualizacao da base padronizada.

import { aviso, bloco, el, metricas, secao } from './componentes.js';
import { formularioEntrada } from './formulario.js';
import { estado } from './state.js';
import { tabelaRemota } from './tabela.js';

// Os estagios que o pipeline emite, na ordem, com nome legivel.
const ESTAGIOS = [
  ['leitura', 'Leitura do arquivo'],
  ['padronizacao', 'Padronização de colunas'],
  ['validacao', 'Validação do schema'],
  ['transformacao', 'Transformação'],
  ['regras_operacionais', 'Regras operacionais'],
  ['medianas', 'Medianas por cliente'],
  ['kpis', 'KPIs'],
  ['base_detalhada', 'Base detalhada'],
  ['janelas', 'Janelas de entrega'],
];

export function painelProgresso() {
  if (!estado.rodando && estado.estagios.length === 0) return null;

  const vistos = new Set(estado.estagios);
  const falhou = vistos.has('erro');

  const etiquetas = ESTAGIOS.map(([id, rotulo]) => el('span', {
    classe: `estagio${vistos.has(id) ? ' feito' : ''}`,
    texto: rotulo,
  }));

  if (vistos.has('cache')) {
    etiquetas.unshift(el('span', {
      classe: 'estagio feito', texto: 'Resultado em cache',
    }));
  }
  if (falhou) {
    etiquetas.push(el('span', { classe: 'estagio falhou', texto: 'Erro' }));
  }

  return secao(
    estado.rodando ? 'Processando' : 'Última execução',
    [
      bloco([
        el('div', { classe: 'progresso' }, etiquetas),
        el('p', { classe: 'dica', texto: `${estado.segundos}s` }),
      ]),
    ],
    'ic-relogio',
  );
}

/** @param {() => void} aoProcessar */
export function abaBase(aoProcessar) {
  const partes = [
    el('h1', { texto: 'Luna' }),
    el('p', { classe: 'linha-apoio', texto: 'Análise de tempos operacionais de entrega' }),
    formularioEntrada(aoProcessar),
    painelProgresso(),
  ];

  if (estado.erro) {
    partes.push(secao('', [aviso('erro', estado.erro)]));
  }

  const resultado = estado.resultado;
  if (resultado) {
    partes.push(secao('Visão da base', [
      metricas([
        { rotulo: 'Linhas brutas', valor: resultado.linhas_brutas.toLocaleString('pt-BR') },
        { rotulo: 'Colunas encontradas', valor: resultado.validacao.total_columns },
        { rotulo: 'Estrutura válida', valor: resultado.validacao.is_valid ? 'Sim' : 'Não' },
        { rotulo: 'Arquivo', valor: resultado.nome_base },
      ]),
    ], 'ic-painel'));

    partes.push(secao('Pré-visualização da base padronizada', [
      tabelaRemota('base_padronizada', { tamanho: 100 }),
    ], 'ic-tabela'));
  }

  return el('div', { classe: 'pagina' }, partes);
}
