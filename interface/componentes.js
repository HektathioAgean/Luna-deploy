// @ts-check
// Peças de tela reusadas pelas abas. Nada aqui conhece o estado nem a API.

/**
 * Cria um elemento. `atributos.texto` vira textContent; `atributos.html` vira
 * innerHTML (use só com conteúdo montado aqui); o resto vira atributo.
 * @param {string} tag
 * @param {Record<string, any>} [atributos]
 * @param {(Node|string|null|false|undefined)[]} [filhos]
 */
export function el(tag, atributos = {}, filhos = []) {
  const no = document.createElement(tag);
  for (const [chave, valor] of Object.entries(atributos)) {
    if (valor === null || valor === undefined || valor === false) continue;
    if (chave === 'texto') no.textContent = String(valor);
    else if (chave === 'html') no.innerHTML = String(valor);
    else if (chave === 'classe') no.className = String(valor);
    else if (chave.startsWith('on') && typeof valor === 'function') {
      no.addEventListener(chave.slice(2), valor);
    } else no.setAttribute(chave, valor === true ? '' : String(valor));
  }
  for (const filho of filhos) {
    if (filho === null || filho === undefined || filho === false) continue;
    no.append(typeof filho === 'string' ? document.createTextNode(filho) : filho);
  }
  return no;
}

/** @param {string} nome id do symbol no sprite de interface/index.html */
export function icone(nome) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('class', 'ic');
  svg.setAttribute('aria-hidden', 'true');
  const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
  use.setAttribute('href', `#${nome}`);
  svg.append(use);
  return svg;
}

/**
 * @param {string} rotulo @param {string|number} valor
 * @param {string} [delta]
 */
export function metrica(rotulo, valor, delta) {
  return el('div', { classe: 'metrica', title: `${rotulo}: ${valor}` }, [
    el('span', { classe: 'rot', texto: rotulo }),
    el('span', { classe: 'valor', texto: valor }),
    delta ? el('span', { classe: 'delta', texto: delta }) : null,
  ]);
}

/** @param {{rotulo:string, valor:string|number, delta?:string}[]} itens */
export function metricas(itens) {
  return el('div', { classe: 'metricas' },
    itens.map((i) => metrica(i.rotulo, i.valor, i.delta)));
}

/**
 * @param {'ok'|'erro'|'atencao'|'neutro'} tipo
 * @param {string} texto
 */
export function aviso(tipo, texto) {
  const icones = { ok: 'ic-check', erro: 'ic-alerta', atencao: 'ic-alerta',
    neutro: 'ic-arquivo' };
  return el('div', { classe: `aviso ${tipo}` }, [
    icone(icones[tipo]),
    el('span', { texto }),
  ]);
}

/**
 * @param {string} titulo
 * @param {(Node|null|false)[]} filhos
 * @param {string} [icone_]
 */
export function secao(titulo, filhos, icone_) {
  return el('section', { classe: 'secao' }, [
    titulo ? el('h2', {}, [icone_ ? icone(icone_) : null, titulo]) : null,
    ...filhos,
  ]);
}

/** @param {(Node|null|false)[]} filhos @param {boolean} [rente] */
export function bloco(filhos, rente = false) {
  return el('div', { classe: rente ? 'bloco rente' : 'bloco' }, filhos);
}

/** @param {Record<string, string|number|null|undefined>} pares */
export function ficha(pares) {
  return el('dl', { classe: 'ficha' },
    Object.entries(pares).map(([rot, valor]) =>
      el('div', {}, [
        el('dt', { texto: rot }),
        el('dd', { texto: valor === null || valor === undefined || valor === ''
          ? '-' : String(valor) }),
      ])));
}

/** @param {string[]} nomes @param {string} [vazio] */
export function listaColunas(nomes, vazio = 'nenhuma') {
  if (!nomes || nomes.length === 0) {
    return el('p', { classe: 'tabela-vazia', texto: vazio });
  }
  return el('ul', { classe: 'colunas' },
    nomes.map((n) => el('li', { texto: n })));
}

/**
 * Abas internas de uma página.
 * @param {{id:string, rotulo:string}[]} itens
 * @param {string} ativo
 * @param {(id:string) => void} aoTrocar
 */
export function abas(itens, ativo, aoTrocar) {
  return el('div', { classe: 'abas', role: 'tablist' },
    itens.map((item) => el('button', {
      classe: 'aba',
      type: 'button',
      role: 'tab',
      'aria-selected': item.id === ativo ? 'true' : 'false',
      texto: item.rotulo,
      onclick: () => aoTrocar(item.id),
    })));
}

/**
 * Campo de formulário com rótulo e dica.
 * @param {string} rotulo @param {Node} controle @param {string} [dica]
 */
export function campo(rotulo, controle, dica) {
  return el('label', { classe: 'campo' }, [
    el('span', { classe: 'rot', texto: rotulo }),
    controle,
    dica ? el('span', { classe: 'dica', texto: dica }) : null,
  ]);
}

/**
 * @param {number} valor
 * @param {(v:number) => void} aoMudar
 * @param {{min?:number, max?:number, passo?:number}} [limites]
 */
export function numero(valor, aoMudar, limites = {}) {
  return el('input', {
    type: 'number',
    value: valor,
    min: limites.min ?? 1,
    max: limites.max,
    step: limites.passo ?? 1,
    oninput: (/** @type {Event} */ evento) => {
      const alvo = /** @type {HTMLInputElement} */ (evento.target);
      if (alvo.value === '') return;
      aoMudar(Number(alvo.value));
    },
  });
}

/**
 * @param {string[]} valores @param {string} atual
 * @param {(v:string) => void} aoMudar
 */
export function selecao(valores, atual, aoMudar) {
  return el('select', {
    onchange: (/** @type {Event} */ evento) =>
      aoMudar(/** @type {HTMLSelectElement} */ (evento.target).value),
  }, valores.map((v) => el('option', {
    value: v, texto: v, selected: v === atual ? true : null,
  })));
}

/**
 * @param {string} rotulo @param {boolean} marcado
 * @param {(v:boolean) => void} aoMudar
 */
export function marcavel(rotulo, marcado, aoMudar) {
  return el('label', { classe: 'marcavel' }, [
    el('input', {
      type: 'checkbox',
      checked: marcado ? true : null,
      onchange: (/** @type {Event} */ evento) =>
        aoMudar(/** @type {HTMLInputElement} */ (evento.target).checked),
    }),
    el('span', { texto: rotulo }),
  ]);
}

/**
 * @param {string} rotulo @param {number} valor
 * @param {(v:number) => void} aoMudar
 * @param {{min:number, max:number, passo:number, sufixo?:string}} limites
 */
export function faixa(rotulo, valor, aoMudar, limites) {
  const leitura = el('span', { classe: 'dica',
    texto: `${valor}${limites.sufixo || ''}` });
  const controle = el('input', {
    type: 'range',
    value: valor,
    min: limites.min,
    max: limites.max,
    step: limites.passo,
    oninput: (/** @type {Event} */ evento) => {
      const novo = Number(/** @type {HTMLInputElement} */ (evento.target).value);
      leitura.textContent = `${novo}${limites.sufixo || ''}`;
      aoMudar(novo);
    },
  });
  return el('label', { classe: 'campo' }, [
    el('span', { classe: 'rot', texto: rotulo }),
    controle,
    leitura,
  ]);
}
