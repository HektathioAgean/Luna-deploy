// @ts-check
// Aba Validacao: o laudo estrutural da base contra o schema oficial.

import { aviso, bloco, el, listaColunas, secao } from './componentes.js';
import { estado } from './state.js';
import { tabelaRemota } from './tabela.js';

export function abaValidacao() {
  const resultado = estado.resultado;
  if (!resultado) {
    return el('div', { classe: 'pagina' }, [
      el('h1', { texto: 'Validação' }),
      secao('', [aviso('neutro', 'Processe uma base para ver a validação.')]),
    ]);
  }

  const v = resultado.validacao;
  const partes = [
    el('h1', { texto: 'Validação estrutural' }),
    el('p', { classe: 'linha-apoio', texto: resultado.nome_base }),
  ];

  if (!v.is_valid) {
    partes.push(secao('', [
      aviso('erro', 'Estrutura inválida: faltam colunas obrigatórias.'),
    ]));
    partes.push(secao('Colunas obrigatórias ausentes', [
      bloco([listaColunas(v.required_missing)]),
    ], 'ic-alerta'));

    if (resultado.sugestoes?.length) {
      partes.push(secao('Sugestões', [
        bloco([el('ul', {},
          resultado.sugestoes.map((/** @type {string} */ s) =>
            el('li', { texto: s })))]),
      ]));
    }

    partes.push(secao('Schema oficial', [tabelaRemota('schema')], 'ic-camadas'));
    return el('div', { classe: 'pagina' }, partes);
  }

  partes.push(secao('', [
    aviso('ok', 'Estrutura válida para processamento.'),
  ]));

  partes.push(secao('', [
    el('div', { classe: 'par-par' }, [
      el('div', {}, [
        el('h2', { texto: 'Colunas obrigatórias encontradas' }),
        bloco([listaColunas(v.required_found)]),
      ]),
      el('div', {}, [
        el('h2', { texto: 'Colunas extras não reconhecidas' }),
        bloco([listaColunas(v.unknown_columns, 'nenhuma coluna extra')]),
      ]),
    ]),
  ]));

  if (v.optional_found?.length) {
    partes.push(secao('Colunas opcionais encontradas', [
      bloco([listaColunas(v.optional_found)]),
    ]));
  }

  if (v.duplicate_standardized_columns?.length) {
    partes.push(secao('', [
      aviso('atencao',
        'Colunas duplicadas após a padronização: ' +
        v.duplicate_standardized_columns.join(', ') +
        '. As repetições receberam o sufixo __dupN.'),
    ]));
  }

  partes.push(secao('Mapeamento aplicado', [tabelaRemota('mapeamento')], 'ic-tabela'));
  partes.push(secao('Schema oficial', [tabelaRemota('schema')], 'ic-camadas'));
  partes.push(secao('Aliases reconhecidos', [tabelaRemota('aliases')], 'ic-camadas'));

  return el('div', { classe: 'pagina' }, partes);
}
