// @ts-check
// Aba Exportacao: grava o resultado numa pasta escolhida.
//
// Em aplicativo local nao faz sentido "baixar": o destino real e V: ou uma
// pasta de trabalho, nao a pasta Downloads do navegador. A rota grava o arquivo
// e devolve o caminho.

import { api } from './api.js';
import { aviso, bloco, campo, el, ficha, icone, secao } from './componentes.js';
import { criarNavegador } from './navegador.js';
import { estado } from './state.js';

const ABAS_EXCEL = [
  'Base Bruta', 'Base Validada', 'Base Detalhada', 'Inconsistencias',
  'Expurgados', 'Anomalias', 'Medianas Cliente', 'Janelas Atendimento',
];

export function abaExportacao() {
  const resultado = estado.resultado;
  if (!resultado) {
    return el('div', { classe: 'pagina' }, [
      el('h1', { texto: 'Exportação' }),
      secao('', [aviso('neutro', 'Processe uma base para exportar.')]),
    ]);
  }

  const campoDestino = el('input', {
    type: 'text',
    value: estado.destinoExportacao,
    placeholder: 'V:\\Logistica\\...\\pasta de saída',
    spellcheck: 'false',
    oninput: (/** @type {Event} */ e) => {
      estado.destinoExportacao = /** @type {HTMLInputElement} */ (e.target).value;
      atualizar();
    },
  });

  const navegador = criarNavegador({
    apenasPastas: true,
    aoEscolherPasta: (caminho) => {
      campoDestino.value = caminho;
      estado.destinoExportacao = caminho;
      atualizar();
    },
  });

  const botaoProcurar = el('button', {
    classe: 'botao', type: 'button',
    onclick: () => navegador.alternar(campoDestino.value.trim()),
  }, [icone('ic-pasta'), 'Procurar']);

  const recado = el('div');

  const botaoExcel = el('button', { classe: 'botao principal', type: 'button' },
    [icone('ic-baixar'), 'Gravar Excel consolidado']);
  const botaoZip = el('button', { classe: 'botao', type: 'button' },
    [icone('ic-baixar'), 'Gravar ZIP com CSVs']);

  function atualizar() {
    const vazio = !campoDestino.value.trim();
    botaoExcel.disabled = vazio;
    botaoZip.disabled = vazio;
  }

  /** @param {'xlsx'|'zip'} formato */
  async function gravar(formato) {
    botaoExcel.disabled = true;
    botaoZip.disabled = true;
    // A gravação é síncrona e não tem barra de progresso. Em base de centenas
    // de milhares de linhas o Excel leva minutos; o aviso diz isso.
    recado.replaceChildren(aviso('neutro',
      formato === 'xlsx'
        ? 'Gravando o Excel. Em base grande isto leva alguns minutos; não feche a janela.'
        : 'Gravando o ZIP...'));
    try {
      const resposta = await api.exportar(formato, campoDestino.value.trim());
      estado.ultimaExportacao = resposta.caminho;
      recado.replaceChildren(aviso('ok', `Gravado: ${resposta.caminho}`));
    } catch (erro) {
      recado.replaceChildren(
        aviso('erro', erro instanceof Error ? erro.message : String(erro)));
    } finally {
      atualizar();
    }
  }

  botaoExcel.addEventListener('click', () => gravar('xlsx'));
  botaoZip.addEventListener('click', () => gravar('zip'));

  atualizar();
  if (estado.ultimaExportacao) {
    recado.replaceChildren(aviso('ok', `Gravado: ${estado.ultimaExportacao}`));
  }

  const c = resultado.contagens;

  return el('div', { classe: 'pagina' }, [
    el('h1', { texto: 'Exportação' }),
    el('p', { classe: 'linha-apoio', texto: resultado.nome_base }),

    secao('Destino', [
      bloco([
        campo('Pasta de saída',
          el('div', { classe: 'linha-caminho' }, [campoDestino, botaoProcurar]),
          'O nome do arquivo sai do nome da base. Informe só a pasta.'),
        navegador.elemento,
        el('div', { classe: 'rodape-acao' }, [botaoZip, botaoExcel]),
        recado,
      ]),
    ], 'ic-pasta'),

    secao('O que vai no arquivo', [
      bloco([
        ficha({
          'Base Bruta': `${resultado.linhas_brutas.toLocaleString('pt-BR')} linhas`,
          'Base Validada': `${c.processados.toLocaleString('pt-BR')} linhas`,
          'Base Detalhada': `${c.base_detalhada.toLocaleString('pt-BR')} linhas`,
          'Inconsistências': `${c.inconsistencias.toLocaleString('pt-BR')} linhas`,
          'Expurgados': `${c.expurgados.toLocaleString('pt-BR')} linhas`,
          'Anomalias': `${c.anomalias.toLocaleString('pt-BR')} linhas`,
          'Medianas Cliente': `${c.medianas.toLocaleString('pt-BR')} linhas`,
          'Janelas Atendimento': `${c.janelas_entrega.toLocaleString('pt-BR')} linhas`,
        }),
        el('p', { classe: 'dica', texto:
          `Excel: uma aba por tabela (${ABAS_EXCEL.join(', ')}). ` +
          'ZIP: um CSV por tabela, separador ";" e encoding utf-8-sig.' }),
        el('p', { classe: 'dica', texto:
          'A base bruta é relida do arquivo original no momento da gravação, ' +
          'para não ocupar memória entre o processamento e a exportação.' }),
      ]),
    ], 'ic-camadas'),
  ]);
}
