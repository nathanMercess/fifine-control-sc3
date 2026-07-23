# Plano progressivo de investigação de hardware do SC3

Este plano começa com observação e termina antes de qualquer modificação irreversível. **Não abrir, energizar aberto, soldar, cortar trilhas, injetar sinal ou conectar um microcontrolador antes de concluir os gates correspondentes.** O SC3 possui phantom power anunciado pelo fabricante; portanto, não se deve presumir que todos os pontos internos estejam limitados à tensão USB. Consulte a [página oficial do produto](https://fifinemicrophone.com/products/fifine-ampligame-sc3-audio-mixer).

## Gate 0 — esgotar caminhos externos

Antes de abrir o equipamento:

1. concluir o mapa de input reports HID;
2. comparar cada controle físico com Core Audio;
3. validar o Nível 1 com mute, gravação, perfis e DSP/roteamento virtual;
4. avaliar um controle auxiliar totalmente externo.

Abrir o SC3 só se houver uma função concreta que os níveis externos não resolvam.

## Gate 1 — fotos necessárias

Solicitar imagens nítidas, sem o equipamento energizado:

- exterior frontal, traseiro, laterais, base, etiquetas e parafusos;
- interior antes de desconectar qualquer cabo;
- placa inteira, frente e verso, perpendicular à câmera e em alta resolução;
- macros legíveis de todos os CIs, cristais, reguladores, conectores e componentes próximos ao USB, XLR, phantom power, botões, pads, knobs e LEDs;
- conectores/cabos fotografados antes da remoção, com marcação de orientação;
- régua ou referência de escala ao lado da placa;
- fotos com luz lateral para seguir trilhas, sem flash estourado.

Não energizar a placa apenas para obter fotos. Guardar parafusos por posição e interromper se a abertura exigir força, quebrar lacre ou remover peça colada.

### Entregável do Gate 1

Um diagrama visual com componentes identificados apenas pelo que está escrito ou pelo datasheet correspondente. Componentes sem marcação permanecem `U?`, `Q?`, `D?`, `R?`, `C?`; não adivinhar função.

## Gate 2 — mapa passivo, sem alimentação

Equipamento desconectado de USB, áudio, fones, line-in/out, XLR e phantom power:

1. confirmar que não há alimentação externa;
2. usar proteção ESD e pontas finas isoladas;
3. localizar shield USB, planos de terra e continuidade entre terras, sem assumir que todo shield é GND de sinal;
4. mapear contatos dos botões com continuidade/resistência nos estados solto e pressionado;
5. mapear terminais dos potenciômetros sem girá-los bruscamente;
6. identificar conectores removíveis e test points existentes;
7. não usar continuidade em placa energizada e não medir resistência sobre circuitos alimentados.

### Tabela de pontos a preencher

| Rótulo provisório | Localização/foto | Medição sem energia | Medição energizada | Estado |
|---|---|---|---|---|
| `TP_GND?` | a identificar | continuidade para referência confirmada | não medir até confirmar | desconhecido |
| `BTN_MIC_MUTE_A/B` | a identificar | resistência solto/pressionado | tensão DC solto/pressionado | desconhecido |
| `BTN_*_A/B` | a identificar | resistência solto/pressionado | tensão DC solto/pressionado | desconhecido |
| `POT_*_END1/WIPER/END2` | a identificar | resistência entre terminais | faixa DC durante movimento | desconhecido |
| `LED_*_A/K?` | a identificar | modo diodo, se seguro | forma de onda/tensão com LED off/on | desconhecido |
| `VRAIL_*` | a identificar | resistência para GND apenas como comparação | tensão DC e ripple | desconhecido |

Nenhuma tensão esperada deve ser preenchida antes da medição. Valores como 1,8 V, 3,3 V, 5 V ou tensões maiores são possibilidades comuns em eletrônica, não fatos sobre esta placa.

## Gate 3 — medições energizadas

Somente para pessoa habituada a medir placas energizadas. Remover objetos metálicos soltos, apoiar a placa sem curvar, prender a referência antes de energizar e usar pontas com mínimo metal exposto.

### Ordem de medição

1. Com o SC3 fechado, medir primeiro o comportamento externo e confirmar que continua funcional.
2. Com as fotos e o mapa passivo prontos, confirmar uma referência de terra por continuidade **com a energia desligada**.
3. Energizar sem XLR, line-in/out ou fones; manter phantom power desligado.
4. Medir tensão DC das alimentações identificadas, sem pressupor valor.
5. Medir ambos os lados de cada contato de botão em relação à referência, solto e pressionado.
6. Medir os três terminais de cada potenciômetro nas posições mínima, central e máxima.
7. Para sinais multiplexados, PWM ou scanning, parar de interpretar apenas pelo multímetro e usar osciloscópio com probe adequado e terra curto.
8. Desenergizar antes de mudar garras, conectores ou escala de resistência.

### Regras de parada

Interromper imediatamente se houver aquecimento, cheiro, reset, áudio anormal, tensão acima da faixa do instrumento/probe, ponto instável, dúvida sobre terra, proximidade da seção phantom ou risco de curto entre pinos. Não sondar pinos finos de MCU/codec até identificar o CI e consultar pinout/datasheet.

## Como escolher a interface elétrica

A escolha depende das medições, não da aparência do botão.

| Resultado observado | Opção a avaliar | Proteção/restrição |
|---|---|---|
| contato simples para GND, baixa frequência | PhotoMOS, transistor open-drain ou optoacoplador apropriado em paralelo | verificar polaridade, corrente de fuga, tensão e estado no boot; nunca unir terras por acidente |
| contato simples flutuante | PhotoMOS/relé de sinal | respeitar tensão/corrente e resistência on; preferência por isolamento galvânico |
| matriz de teclas/scanning bidirecional | chave analógica bilateral compatível | opto comum pode distorcer scanning; medir amplitude, frequência, direção e capacitância tolerada |
| botão capacitivo | nenhuma conexão até identificar controlador/eletrodo | fios e capacitância alteram sensibilidade; considerar sensor externo ou comando por software |
| LED DC dedicado | buffer de altíssima impedância ou optoacoplador de leitura | não alimentar o LED pela placa auxiliar; medir polaridade/corrente e PWM |
| LED multiplexado/PWM | buffer/isolador rápido e decodificação temporal | multímetro não basta; preservar carga e temporização |
| potenciômetro analógico | buffer de entrada de alta impedância para ADC | limitar/clamp de entrada conforme tensão medida; não mover o wiper eletronicamente nesta fase |
| encoder incremental | entradas protegidas ou isoladas por canal | medir pull-ups, níveis e debounce |

Um optoacoplador não é solução universal: corrente de LED, CTR, fuga, atraso e polaridade podem interferir em uma matriz. Uma chave analógica também não é universal: tensão além dos rails, resistência on e injeção de carga podem danificar ou alterar o circuito.

## Nível 3 recomendado — auxiliar externo

Antes de tocar na placa, construir um dispositivo separado com ESP32-S3, Raspberry Pi Pico, Arduino Pro Micro ou equivalente:

- botões próprios para hotkeys de mute, gravação, perfil e OBS;
- USB HID ou serial para o aplicativo;
- display/LED alimentado pelo próprio auxiliar;
- nenhuma conexão elétrica ao SC3.

Essa versão valida firmware, UX, macros e protocolo com o PC. Se atender ao objetivo, elimina os riscos do Nível 4.

## Nível 4 — placa auxiliar reversível

Somente após os Gates 1–3:

- preferir test hooks, pogo pins, interposer em conector existente ou chicote removível;
- adicionar resistores série, proteção ESD e fusível/resettable fuse na alimentação auxiliar quando aplicável;
- manter alimentação e terra separadas quando o isolamento for necessário;
- fixar mecanicamente sem pressionar trilhas/componentes e sem adesivo permanente;
- não cortar trilhas, raspar máscara ou remover componentes;
- documentar cada fio nas duas pontas e usar conectores polarizados;
- validar primeiro uma única entrada, com limite de tempo e temperatura monitorada.

### Plano de reversão

1. Fotografar estado original e guardar todos os componentes/fixadores.
2. Manter um chicote de bypass ou desconexão simples da placa auxiliar.
3. Testar o SC3 original após remover totalmente o auxiliar.
4. Preservar logs de tensões, continuidade e comportamento antes/depois.
5. Se a reversão não restaurar áudio, USB e controles originais, parar; não avançar para firmware.

## Nível 5 — eletrônica/firmware avançado

Pré-requisitos mínimos:

- MCU/SoC, codec, flash/EEPROM, reguladores e clock identificados por marcação e datasheet;
- interfaces de debug/programação confirmadas eletricamente;
- método de leitura não destrutiva e backup repetido com hashes iguais;
- conhecimento de readout protection, boot mode e recuperação;
- unidade de teste que possa ser perdida sem comprometer o equipamento principal.

Não aplicar tensão a pads de debug, não apagar memória, não remover proteção e não atualizar firmware sem imagem íntegra, ferramenta de gravação conhecida e procedimento de recuperação testado. Nada disso está autorizado ou pronto hoje.

## Riscos e mitigação

| Risco | Consequência | Mitigação mínima |
|---|---|---|
| phantom power ou rail desconhecido | choque local, arco, dano a probe/placa | phantom desligado; identificar seção; medir faixa antes de conectar lógica |
| curto com ponta de prova | reset ou dano permanente | pontas isoladas, placa firme, uma mão, garras colocadas sem energia |
| terra incorreto/loop de terra | ruído, corrente por USB, dano | confirmar continuidade e arquitetura; isolamento quando necessário |
| carga extra em botão/LED | falsos acionamentos, brilho alterado, travamento | entrada alta impedância; medir fuga/capacitância; testar um canal |
| ESD | falha imediata ou latente | pulseira/tapete ESD e manuseio pelas bordas |
| cabo invertido | dano em alimentação/sinal | fotos, marcação, conector polarizado e checklist |
| perda de garantia/integridade mecânica | custo e perda de reversibilidade | verificar termos, parar diante de lacres/cola, não forçar |
| interpretação errada de sinal multiplexado | projeto incompatível | osciloscópio e datasheet; não inferir por média do multímetro |

## Dados que devem retornar antes do próximo passo físico

1. Fotos do Gate 1 em resolução original, não apenas capturas comprimidas de mensageiro.
2. Identificação de revisão da placa e todas as marcações legíveis.
3. Tabela do Gate 2 preenchida, incluindo instrumento e escala usados.
4. Confirmação de que o SC3 ainda funciona integralmente após remontagem.
5. Objetivo físico prioritário: ler botão, simular botão, ler LED ou controlar display externo.

Com esses dados será possível marcar pontos de teste sobre as fotos e produzir um esquema de conexão específico. Antes deles, qualquer ponto, tensão ou componente seria especulação.
