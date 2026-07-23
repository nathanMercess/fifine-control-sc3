# Investigação do FIFINE AmpliGame SC3

Estado da investigação em **21 de julho de 2026**. Este documento distingue quatro tipos de informação:

- **Medido:** observado no SC3 conectado a este computador.
- **Implementado:** existe código executável neste repositório.
- **Hipótese:** explicação plausível que ainda precisa de experimento.
- **Planejado:** caminho alternativo, ainda não implementado.

Essa separação é importante: a existência de um endpoint, Usage HID ou comprimento de report não prova o significado de um botão nem autoriza o envio de comandos ao equipamento.

## Resumo executivo

O SC3 funciona no Windows como um dispositivo USB composto. Foram medidos uma função de áudio USB, dois endpoints Core Audio e duas coleções HID. Uma delas é Consumer Control; a outra usa uma Usage Page definida pelo fornecedor. Não foram encontrados dispositivos MIDI nem portas COM associados.

O caminho funcional imediato é usar o SC3 como interface de áudio e controlar o endpoint do Windows. O protótipo final já enumera endpoints, lê nível, altera mute/volume do endpoint, grava WAV, recupera gravações interrompidas, mantém perfis e oferece CLI e interface WPF. Também estão implementados roteamento WASAPI com DSP, hotkeys globais para mute/gravação e integração OBS WebSocket 5.x para consultar, iniciar e parar a gravação. Isso não significa que o mute físico ou os LEDs estejam sincronizados com o Windows.

O código compila e, na validação final deste workspace, **26 de 26 testes automatizados** passaram. OBS não estava disponível para teste ao vivo e não havia VB-Cable/VoiceMeeter ou outro endpoint virtual instalado para validar a rota completa até Discord/OBS. O roteamento e o DSP estão implementados e cobertos por testes de lógica, mas essa cadeia virtual específica continua pendente de validação no ambiente real.

A melhor pista para integração física sem abrir o aparelho é a interface HID `MI_04`. Ela aceita input reports, mas nenhum report gerado por botão foi registrado ainda. A próxima ação é um experimento somente leitura, com um controle físico por vez.

## O que foi medido

### USB, PnP e áudio

| Item | Resultado medido |
|---|---|
| Identificação USB | `VID_3142`, `PID_0C33`, versão `0x0100` |
| Produto/fabricante/serial HID | `fifine SC3` / `MV-SILICON` / `20190808` |
| Dispositivo composto | `USB Composite Device`, serviço `usbccgp` |
| Interface de áudio | `MI_00`, classe USB Audio, serviço `usbaudio` |
| Reprodução | `Alto-falantes (fifine SC3)`, ativo, 2 canais |
| Captura | `Microphone (fifine SC3)`, ativo, 2 canais |
| MIDI relacionado | nenhum encontrado |
| Porta COM relacionada | nenhuma encontrada |

No momento da medição, o endpoint de reprodução estava em 100%, sem mute; o endpoint de captura estava em aproximadamente 92,55%, sem mute. Esses valores são estado momentâneo do Windows, não especificações do equipamento.

### HID

| Interface | Usage Page / Usage | Input | Output | Feature | Interpretação permitida |
|---|---:|---:|---:|---:|---|
| `MI_03` | `0x000C / 0x0001` | 2 bytes | 0 | 0 | coleção Consumer Control; ainda não se sabe qual controle do SC3 a utiliza |
| `MI_04` | `0xFF00 / 0x55AA` | 257 bytes | 257 bytes | 9 bytes | coleção definida pelo fornecedor; protocolo e semântica desconhecidos |

A USB-IF define Usages como identificadores do propósito de coleções e campos de reports. Páginas definidas pelo fornecedor não ganham semântica pública apenas por aparecerem no descritor. Consulte a [página oficial de especificações HID da USB-IF](https://www.usb.org/hid).

Os comprimentos de output e feature de `MI_04` são apenas capacidades descritas pelo dispositivo. O monitor deste repositório abre `GENERIC_READ`, valida novamente VID/PID e não declara nem chama rotinas de escrita.

### Informações oficiais do produto

A página oficial apresenta o SC3 como mixer para jogos/streaming com conexão digital USB, entrada XLR/TRS, phantom power de 48 V, line-in, line-out, monitoramento por fones, controles físicos e efeitos. A FAQ da própria página informa que o SC3 não é compatível com o software FIFINE Genie. Esses dados descrevem o produto; não constituem uma API de controle. Consulte a [página oficial do FIFINE AmpliGame SC3](https://fifinemicrophone.com/products/fifine-ampligame-sc3-audio-mixer) e o [manual V1.1 vinculado pela FIFINE](https://drive.google.com/file/d/1K09QO09fxLu_PAPifYm-F66Y-AevwAI1/view?usp=sharing).

## O que já está implementado

| Componente | Capacidade atual | Limite atual |
|---|---|---|
| `tools/diagnostics` | inventário PnP, VID/PID, propriedades, endpoints, canais, mute/volume; monitor de mudanças | não captura reports HID nem URBs USB |
| `tools/SC3.HidMonitor` | descritores HID e input reports com timestamp/hex, filtro rígido `3142:0C33`, duração limitada | não interpreta reports e não envia output/feature |
| `FifineControl.Core/Audio` | enumeração Core Audio, mute, volume e peak meter | controla o endpoint; não prova alteração do estado físico/LED |
| `FifineControl.Core/Recording` | gravação WAV com arquivo parcial e recuperação | não oferece FLAC ou formatos comprimidos |
| `FifineControl.Core/Dsp` | ganho digital, noise gate, compressor, EQ paramétrico de três bandas, bypass e medidores pré/pós-DSP | não inclui redução de ruído espectral; qualidade/latência precisam de teste auditivo |
| `FifineControl.Core/Routing` | captura WASAPI → buffer → DSP → endpoint de render, atualização de parâmetros em execução e proteção contra rotas perigosas | rota até cabo virtual não testada porque não havia endpoint virtual instalado |
| `FifineControl.Core/Hotkeys` | `RegisterHotKey`/`WM_HOTKEY`, registro e liberação de atalhos globais | ações atuais na WPF: alternar mute e gravação WAV; conflitos dependem do Windows/aplicações locais |
| `FifineControl.Core/Integrations/Obs` | OBS WebSocket 5.x, autenticação, estado, início/fim e consulta de gravação | implementação não testada contra uma instância OBS ao vivo neste ambiente |
| `FifineControl.Core/Configuration` | perfis JSON validados; endpoints, volume, ganho digital e diretório; configurações globais de OBS/hotkeys | parâmetros completos do gate/compressor/EQ não são persistidos por perfil; senha OBS fica somente em memória |
| `FifineControl.Cli` | diagnóstico, mute/volume, nível, WAV, recuperação, perfis e comando `route` com ganho DSP e medidores pré/pós | não expõe OBS, hotkeys, configuração completa do DSP ou HID |
| `FifineControl.App` | UI WPF, bandeja, endpoints, mute, nível, WAV, recentes, perfis, rota DSP, gate, compressor, presets de EQ, OBS e hotkeys | OBS e rota para cabo virtual ainda não foram validados ao vivo |
| `FifineControl.Core.Tests` | 26/26 no build final: DSP, persistência/validação, autenticação OBS, segurança de routing, gravações e inicialização do Windows | testes automatizados não substituem OBS, áudio virtual nem controles físicos reais |

A API EndpointVolume do Windows pode representar controle em hardware ou implementar volume/mute em software quando o endpoint não oferece isso. Portanto, o sucesso de `SetMute` não demonstra que o LED ou circuito de mute do SC3 foi acionado. Essa distinção é documentada pela [Microsoft na EndpointVolume API](https://learn.microsoft.com/en-us/windows/win32/coreaudio/endpointvolume-api).

## Matriz: controle direto e alternativa

| Objetivo | Direto hoje | Situação | Alternativa real | Próxima validação |
|---|---|---|---|---|
| Mutar/desmutar no computador | Core Audio `IAudioEndpointVolume` | implementado | mute em cadeia DSP/virtual antes do aplicativo consumidor | comparar áudio, propriedade do Windows e LED físico |
| Detectar mute físico | nenhuma conclusão | desconhecido | `MI_03`, `MI_04`, variação do peak meter ou microcontrolador auxiliar | experimento HID controlado |
| Sincronizar mute físico/visual | somente estado do endpoint na UI | parcial | regra de estado no software; depois HID ou sensor elétrico isolado | determinar se o botão gera evento estável e se é momentâneo/latching |
| Volume de captura/reprodução | volume mestre dos endpoints e ganho digital da rota | implementado | rota WASAPI para endpoint virtual | validar qualidade/latência com cabo virtual instalado; verificar knobs físicos |
| Volumes físicos mic/fone/música | nenhuma conclusão | desconhecido | DSP/virtual mixer já implementado em software; integração HID se houver reports | um knob por vez durante captura HID |
| Nível em tempo real | peak meter Core Audio e medidores pré/pós-DSP | implementado | histórico/telemetria adicional | validar comportamento durante mute físico |
| Gravação e recuperação | WASAPI para WAV | implementado | encoder FLAC/AAC/Opus em etapa posterior | testes longos, disco cheio e remoção do dispositivo |
| Perfis | endpoints, volume, ganho digital e diretório | parcial | persistir parâmetros completos de DSP e integrações por perfil | definir esquema versionado e migração |
| Gate/compressor/EQ/ruído | ganho, gate, compressor e EQ de três bandas | implementado e testado em unidade | redução de ruído espectral ou filtros OBS | teste auditivo, latência e ajuste de presets no SC3 real |
| Hotkeys globais | mute do endpoint e alternância da gravação WAV | implementado na WPF | remapeamento/mais ações no esquema de configuração | teste interativo de conflitos com jogos e aplicativos |
| Discord/jogos | rota WASAPI para um endpoint de render escolhido | implementado, sem validação com cabo virtual | instalar VB-Cable/VoiceMeeter e escolher sua saída virtual | PoC ao vivo antes de considerar driver próprio |
| OBS | WebSocket 5.x com autenticação, estado e início/fim da gravação | implementado, não testado ao vivo | manter controles manuais/atalhos do próprio OBS | validar com OBS local e senha temporária |
| Pads/botões como macros | nenhuma associação medida | desconhecido | HID somente leitura; hotkey via microcontrolador externo | criar mapa ação → bytes após repetição |
| Controle de LEDs/efeitos | capacidade HID de saída existe, sem protocolo | bloqueado por segurança | deixar hardware intacto; display/LED auxiliar | só considerar escrita após protocolo confirmado e plano de recuperação |

## Arquitetura recomendada por níveis

### Nível 1 — somente software

Fluxo recomendado:

`SC3 → endpoint de captura → aplicação/DSP → dispositivo virtual → Discord/OBS/jogo/gravador`

O backend Core Audio, a gravação, o DSP, o roteamento WASAPI, os hotkeys globais e o cliente OBS WebSocket já estão implementados. A CLI oferece `route`; a WPF permite escolher origem/destino, iniciar/parar a rota, ajustar ganho/gate/compressor/EQ e observar níveis antes/depois do DSP. O próximo gate é instalar conscientemente um dispositivo virtual de mercado e validar a cadeia até Discord/OBS; isso **não foi testado neste computador porque esse endpoint não estava presente**. Um driver virtual próprio só se justifica depois de medir latência, estabilidade e limitações dessa solução. WASAPI é a API do Windows para mover áudio entre aplicações e endpoints; consulte a [visão geral oficial da Microsoft](https://learn.microsoft.com/en-us/windows/win32/coreaudio/wasapi).

### Nível 2 — integração USB/HID

Tratar `MI_03` e `MI_04` como fontes opcionais de eventos. O serviço HID nunca deve bloquear o áudio: se o dispositivo desaparecer, falhar ou mudar de versão, a aplicação continua com o Nível 1. A Microsoft documenta `HidD_GetAttributes` para ler atributos da coleção e `HidP_GetCaps` para obter capacidades; são as bases usadas pelo monitor atual ([HidD_GetAttributes](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_getattributes), [HidP_GetCaps](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidclass/nc-hidclass-phidp_getcaps)).

### Nível 3 — dispositivo auxiliar externo

Se o SC3 não expuser eventos úteis, usar um microcontrolador separado como teclado HID/porta serial para botões e macros próprios. Ele pode receber estado do aplicativo e acionar display/LED externo sem conexão elétrica interna ao mixer. É a primeira opção de hardware porque é reversível e não interfere no áudio.

### Nível 4 — modificação interna reversível

Somente depois das fotos e medições descritas em [HARDWARE_PLAN.md](./HARDWARE_PLAN.md), considerar uma placa auxiliar isolada ligada em paralelo a contatos ou LEDs já identificados. Usar chicote/conector removível, test points e nenhum corte de trilha.

### Nível 5 — customização avançada

Substituir eletrônica, alterar firmware ou criar controlador próprio exige identificação completa de MCU, codec, alimentação, clock, memória, boot/recovery e um backup verificável. Não há dados suficientes para iniciar esse nível.

## Hipótese HID e sequência experimental segura

### Hipóteses testáveis

1. `MI_03` pode emitir Consumer Control para algum botão de mídia. Isso é hipótese, não associação medida.
2. `MI_04` pode transportar eventos dos botões, pads, knobs ou estado interno. O tamanho de 257 bytes torna a interface interessante, mas não prova que todos os controles sejam reportados.
3. Alguns controles podem atuar apenas no DSP interno e não gerar tráfego novo depois da configuração USB inicial.

### Procedimento

1. Fechar aplicações que possam abrir o HID e manter o áudio em uma condição estável.
2. Enumerar novamente e salvar o resultado:

   ```powershell
   New-Item -ItemType Directory -Force .\artifacts\sc3-investigation | Out-Null
   dotnet run --project .\tools\SC3.HidMonitor -- list --json |
     Set-Content -Encoding utf8 .\artifacts\sc3-investigation\hid-descriptors.json
   ```

3. Capturar 15 segundos sem tocar no mixer para medir reports espontâneos:

   ```powershell
   dotnet run --project .\tools\SC3.HidMonitor -- monitor --duration 15 --interface MI_04 |
     Set-Content -Encoding utf8 .\artifacts\sc3-investigation\mi04-baseline.tsv
   ```

4. Para cada controle, iniciar um arquivo novo, esperar dois segundos, executar exatamente cinco ações com intervalo regular e esperar mais dois segundos. Testar separadamente: mute do microfone, cada outro botão, um pad, RGB/efeito e movimento de cada knob em uma direção curta.
5. Repetir a mesma ação em uma segunda captura. Um campo só vira candidato quando muda de forma reprodutível e não muda no baseline.
6. Em outro terminal, correlacionar com Core Audio:

   ```powershell
   .\tools\diagnostics\Watch-SC3Changes.ps1 -DurationSeconds 60 -IntervalSeconds 0.5 -ShowInitialState |
     Export-Csv -NoTypeInformation -Encoding utf8 .\artifacts\sc3-investigation\core-audio-events.csv
   ```

7. Registrar junto de cada arquivo: hora, interface, ação, direção, posição inicial/final aproximada e resultado visível/sonoro.
8. Não reproduzir bytes e não chamar output/feature reports nesta fase. O próximo passo após identificar um padrão ainda é decodificar o descritor/report e confirmar checksums, contadores e campos variáveis.

### Critério de decisão

- **Report estável e exclusivo:** criar parser somente leitura com testes usando capturas gravadas.
- **Report existe, mas é ambíguo:** aumentar repetições e variar apenas um fator.
- **Nenhum report:** observar Core Audio; depois considerar USBPcap ou o Nível 3.
- **Interface ocupada:** testar `MI_03` e `MI_04` separadamente; não substituir o driver HID.

## USBPcap/Wireshark: etapa opcional, não instalada

USBPcap é um driver de filtro para captura USB no Windows. Sua instalação altera a pilha do sistema e pode exigir reinicialização; por isso ela não é automatizada nem necessária para os testes HID iniciais. O instalador oficial do Wireshark para Windows pode oferecer USBPcap como componente opcional, segundo a [documentação de distribuição do Wireshark](https://www.wireshark.org/docs/wsdg_html_chunked/ChIntroReleases.html). O [tour oficial do USBPcap](https://desowin.org/usbpcap/tour.html) explica a seleção do root hub e recomenda iniciar a captura antes de reconectar o dispositivo para obter descritores.

Se essa etapa for aprovada posteriormente:

1. Salvar inventário atual, criar um ponto de restauração quando disponível e confirmar que há outro meio de entrada caso o hub inclua teclado/mouse.
2. Preferir um hub/porta dedicada ao SC3. Uma captura do root hub pode incluir tráfego de outros dispositivos; não compartilhar PCAP sem revisar dados sensíveis.
3. Capturar primeiro um baseline curto; depois uma ação por arquivo, com os mesmos cinco acionamentos usados no experimento HID.
4. Filtrar pelo endereço USB atribuído ao `3142:0C33`, não apenas pelo root hub inteiro. Os campos de filtro USB disponíveis estão na [referência oficial do Wireshark](https://www.wireshark.org/docs/dfref/u/usb.html).
5. Preservar os PCAPs originais e analisar cópias. Não usar replay automático.
6. Desinstalar o filtro se ele causar instabilidade e validar áudio, HID, suspensão e reinicialização.

## Lacunas que permanecem

- Nenhuma captura HID com acionamento de um controle físico foi realizada; nenhum input report foi associado a botão, pad ou knob.
- Não foi demonstrado se mute/volume do Windows altera LEDs ou DSP interno.
- Não foi demonstrado se knobs e pads geram USB após a enumeração.
- O protocolo vendor-defined não foi decodificado.
- OBS WebSocket está implementado, mas não foi testado contra OBS ao vivo neste ambiente.
- DSP/routing está implementado na WPF e CLI, mas não foi validado com cabo virtual porque nenhum VB-Cable/VoiceMeeter equivalente estava instalado.
- Nenhuma escrita HID, troca de driver, USBPcap ou modificação física foi realizada.
- Fotos da placa, componentes, pontos de teste e tensões internas ainda não existem.

Essas lacunas são os gates que impedem afirmar controle direto do hardware.
