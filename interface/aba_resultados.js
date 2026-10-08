// @ts-check
// Aba Resultados: as tabelas de saida, uma por aba interna. A paginacao e do
// servidor, entao abrir a base detalhada nao carrega a base inteira.

import { abas, aviso, el, secao } from './componentes.js';
import { estado } from './state.js';
import { tabelaRemota } from './tabela.js';

const TABELAS = [
  { id: 'medianas', rotulo: 'Medianas por cliente' },
  { id: 'base_detalhada', rotulo: 'Base detalhada' },
  { id: 'janelas_entrega', rotulo: 'Janelas de entrega', exigeJanelas: true },
  { id: 'processados', rotulo: 'Processados' },
  { id: 'inconsistencias', rotulo: 'Inconsistências' },
  { id: 'expurgados', rotulo: 'Expurgados' },
  { id: 'anomalias', rotulo: 'Anomalias' },
];

let ativa = 'medianas';

export function abaResultados() {
  const resultado = estado.resultado;
  if (!resultado) {
    return el('div', { classe: 'pagina' }, [
      el('h1', { texto: 'Resultados' }),
      secao('', [aviso('neutro', 'Processe uma base para ver os resultados.')]),
    ]);
  }

  const disponiveis = TABELAS.filter(
    (t) => !t.exigeJanelas || resultado.parametros.exibir_janelas_entrega);

  if (!disponiveis.some((t) => t.id === ativa)) ativa = disponiveis[0].id;

  const corpo = el('div');

  function desenhar() {
    const atual = disponiveis.find((t) => t.id === ativa);
    corpo.replaceChildren(
      abas(disponiveis.map((t) => ({
        id: t.id,
        rotulo: `${t.rotulo} (${(resultado.contagens[t.id] ?? 0).toLocaleString('pt-BR')})`,
      })), ativa, (id) => { ativa = id; desenhar(); }),
      el('div', { classe: 'secao' }, [tabelaRemota(ativa)]),
      atual ? el('p', { classe: 'dica', texto: nota(atual.id) }) : null,
    );
  }

  desenhar();

  return el('div', { classe: 'pagina' }, [
    el('h1', { texto: 'Resultados' }),
    el('p', { classe: 'linha-apoio', texto: resultado.nome_base }),
    corpo,
  ]);
}

/** @param {string} id */
function nota(id) {
  const notas = {
    medianas: 'Uma linha por cliente, ordenada pela mediana do tempo.',
    base_detalhada: 'Cada entrega com a mediana e o tempo ideal Q1 do cliente ao lado.',
    janelas_entrega: 'A janela é a menor faixa circular que cobre a cobertura alvo.',
    processados: 'As entregas que passaram pelas regras operacionais.',
    inconsistencias: 'Linhas barradas antes das regras: data inválida, cliente vazio ou tempo negativo.',
    expurgados: 'Entregas abaixo do tempo mínimo de expurgo.',
    anomalias: 'Entregas acima do tempo máximo de anomalia.',
  };
  return notas[id] || '';
}
