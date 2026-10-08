// @ts-check
// Navegador de pastas e bases. Usado pela aba Base (escolher o arquivo) e pela
// aba Exportacao (escolher a pasta de destino).
//
// Ele enxerga tudo que a conta que abriu o Luna ja enxerga pelo Explorer,
// inclusive os drives de rede: e exatamente para isso que existe.

import { api } from './api.js';
import { el, icone } from './componentes.js';

/**
 * @param {{apenasPastas?:boolean,
 *          aoEscolherArquivo?:(caminho:string) => void,
 *          aoEscolherPasta?:(caminho:string) => void}} opcoes
 */
export function criarNavegador(opcoes = {}) {
  const caixa = el('div');
  let aberto = false;
  let ultimoCaminho = '';

  async function abrir(caminho) {
    aberto = true;
    ultimoCaminho = caminho || ultimoCaminho;
    caixa.replaceChildren(el('div', { classe: 'navegador' }, [
      el('p', { classe: 'navegador-vazio', texto: 'Lendo a pasta...' }),
    ]));
    try {
      const dados = await api.procurar(ultimoCaminho);
      ultimoCaminho = dados.caminho || '';
      caixa.replaceChildren(desenhar(dados));
    } catch (erro) {
      caixa.replaceChildren(el('div', { classe: 'navegador' }, [
        el('p', {
          classe: 'navegador-vazio',
          texto: erro instanceof Error ? erro.message : String(erro),
        }),
      ]));
    }
  }

  function fechar() {
    aberto = false;
    caixa.replaceChildren();
  }

  function alternar(caminho) {
    if (aberto) fechar();
    else abrir(caminho);
  }

  /** @param {any} dados resposta de /api/procurar */
  function desenhar(dados) {
    const lista = [];

    if (dados.pai) {
      lista.push(entrada('ic-cima', '.. (pasta acima)', null,
        () => abrir(dados.pai), dados.pai));
    }

    for (const pasta of dados.pastas) {
      const linha = entrada('ic-pasta', pasta.nome, null,
        () => abrir(pasta.caminho), pasta.caminho);
      lista.push(linha);
    }

    if (!opcoes.apenasPastas) {
      for (const arquivo of dados.arquivos) {
        lista.push(entrada(
          'ic-arquivo', arquivo.nome,
          `${arquivo.medida} · ${arquivo.modificado}`,
          () => {
            opcoes.aoEscolherArquivo?.(arquivo.caminho);
            fechar();
          },
          arquivo.caminho,
        ));
      }
    }

    const topo = el('div', { classe: 'navegador-topo' }, [
      icone('ic-pasta'),
      el('span', {
        classe: 'atual',
        texto: dados.caminho || 'Escolha uma unidade',
        title: dados.caminho,
      }),
      opcoes.apenasPastas && dados.caminho
        ? el('button', {
            classe: 'botao', type: 'button',
            onclick: () => {
              opcoes.aoEscolherPasta?.(dados.caminho);
              fechar();
            },
          }, [icone('ic-check'), 'Usar esta pasta'])
        : null,
    ]);

    return el('div', { classe: 'navegador' }, [
      topo,
      el('div', { classe: 'navegador-drives' },
        dados.drives.map((/** @type {string} */ drive) => el('button', {
          classe: 'drive', type: 'button', texto: drive,
          onclick: () => abrir(drive),
        }))),
      lista.length
        ? el('div', { classe: 'navegador-lista' }, lista)
        : el('p', {
            classe: 'navegador-vazio',
            texto: opcoes.apenasPastas
              ? 'Nenhuma subpasta aqui.'
              : 'Nenhuma pasta nem base .csv/.xlsx aqui.',
          }),
    ]);
  }

  /**
   * @param {string} nomeIcone @param {string} nome @param {string|null} medida
   * @param {() => void} aoClicar @param {string} titulo
   */
  function entrada(nomeIcone, nome, medida, aoClicar, titulo) {
    return el('button', {
      classe: 'entrada', type: 'button', title: titulo, onclick: aoClicar,
    }, [
      icone(nomeIcone),
      el('span', { classe: 'nome', texto: nome }),
      medida ? el('span', { classe: 'medida', texto: medida }) : null,
    ]);
  }

  return { elemento: caixa, abrir, fechar, alternar };
}
