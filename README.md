# FifineControl — Prova de conceito de controle de áudio no Windows

> 📄 [English version available](README.en.md)

Este é o backend de nível 1 (apenas software) para controlar os endpoints de áudio do Windows expostos por um FIFINE AmpliGame SC3 ou outro dispositivo de áudio USB. O projeto não assume deliberadamente que o botão físico de mute, os LEDs, os pads ou os knobs expõem mensagens de controle USB.

## O que já funciona

- Enumera endpoints ativos de captura e reprodução do Windows Core Audio via NAudio.
- Lê e altera o mute e o volume master do endpoint, confirmando o valor resultante.
- Lê o medidor de pico do endpoint em tempo real.
- Captura um endpoint de entrada ativo para WAV.
- Grava arquivos como `.wav.partial`, fecha o cabeçalho WAV e só então renomeia para `.wav`.
- Repara gravações RIFF/WAV interrompidas e as preserva como `_recovered.wav`.
- Armazena perfis validados de forma atômica em JSON.
- Escreve logs de diagnóstico estruturados em JSON Lines.
- Oferece uma interface desktop WPF com tema escuro e suporte à bandeja do sistema.
- Conecta ao protocolo OBS WebSocket 5.x e inicia/para gravações no OBS.
- Registra atalhos globais do Windows configuráveis para mute de endpoint e gravação WAV local.
- Roteia o áudio capturado por ganho digital, noise gate, compressor e EQ paramétrico de três bandas até um endpoint de reprodução selecionado.
- Exibe medidores pré e pós-DSP na interface WPF e expõe um comando de roteamento limitado na CLI.
- Inicia opcionalmente com o usuário atual do Windows e gerencia com segurança as gravações recentes concluídas.

O mute de endpoint é um mute de software do Windows. Se o SC3 reflete isso em um LED de hardware — e se o mute físico altera a propriedade do Windows — precisa ser verificado no dispositivo real.

## Build e execução

Requer Windows e .NET 8 SDK ou superior.

```powershell
dotnet restore .\FifineControl.sln
dotnet build .\FifineControl.sln -c Release
dotnet test .\FifineControl.sln -c Release --no-build
dotnet run --project .\src\FifineControl.App -c Release
dotnet run --project .\src\FifineControl.Cli -- devices
```

Um build dependente de framework para Windows está disponível em
`artifacts\publish\FifineControl\FifineControl.exe`. Esta máquina já possui o
runtime necessário do .NET 8 Windows Desktop. Para recriar:

```powershell
dotnet publish .\src\FifineControl.App\FifineControl.App.csproj -c Release --no-restore -o .\artifacts\publish\FifineControl
```

O app desktop permite selecionar endpoints de captura e reprodução, controlar o mute e o volume do endpoint de captura do Windows, acompanhar o medidor de pico em tempo real, gravar arquivos WAV, gerenciar o estado de perfis, abrir gravações recentes e copiar diagnósticos. Minimizar pode ocultar a janela na área de notificação; clique duplo no ícone da bandeja para restaurá-la.

## OBS e atalhos globais

No OBS Studio, ative o servidor WebSocket em **Ferramentas > Configurações do servidor WebSocket**. O app usa por padrão `ws://127.0.0.1:4455`, a porta padrão do OBS WebSocket 5.x. Digite a senha do OBS no app e clique em **Conectar**; a senha é mantida apenas na memória do processo, não é registrada em log e nunca é gravada em `settings.json`. O endereço do servidor e a opção de conectar ao iniciar são persistidos.

Os atalhos globais padrão são `Ctrl+Shift+M` para mute de endpoint e `Ctrl+Shift+R` para gravação WAV local. Eles utilizam a API `RegisterHotKey` do Windows e são desregistrados quando o app fecha. Os modificadores e os valores de tecla virtual podem ser alterados em `%LOCALAPPDATA%\FifineControl\settings.json` na chave `hotkeys`; reinicie o app após editar. Se outro aplicativo já tiver registrado um atalho, a interface exibe o erro de registro Win32.

O Discord não é controlado via integração específica de aplicativo. Selecione o endpoint de captura do SC3 diretamente no Discord, ou selecione a saída de um cabo virtual de áudio instalado separadamente. O FifineControl não instala um driver de áudio virtual.

Referências de protocolo: [projeto OBS WebSocket](https://github.com/obsproject/obs-websocket) e [protocolo oficial 5.x](https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md).

## Inicialização e gravações recentes

**Iniciar com o Windows** é opcional e desativado por padrão. Grava apenas o valor `FifineControl` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, usando um caminho absoluto entre aspas, sem exigir direitos de administrador. Desativar a opção remove apenas esse valor e não altera nenhuma outra entrada de inicialização.

O card de gravações recentes permite abrir ou renomear um `.wav` concluído selecionado. Os nomes são sanitizados, componentes de diretório são rejeitados e um destino existente nunca é sobrescrito. **Mover para Lixeira** exibe uma confirmação Sim/Não com padrão Não, aceita apenas um `.wav` regular selecionado diretamente dentro do diretório de gravações configurado, rejeita reparse points e nunca gerencia arquivos `.partial`; a exclusão é recuperável pela Lixeira do Windows.

Os controles têm rótulos distintos: **Volume do endpoint (Windows)** altera o endpoint do Core Audio, enquanto **Ganho digital da rota** é aplicado apenas enquanto a rota de captura para reprodução via WASAPI está ativa. O endpoint de monitoramento selecionado é o destino da rota. O FifineControl não instala um driver de áudio virtual; portanto, rotear para o Discord ou OBS requer um endpoint virtual instalado separadamente quando esses aplicativos precisam consumir o sinal processado.

Copie o ID completo do endpoint de captura exibido para o **Mixer SC3**, incluindo as chaves, e coloque entre aspas:

```powershell
dotnet run --project .\src\FifineControl.Cli -- status "<endpoint-id>"
dotnet run --project .\src\FifineControl.Cli -- toggle "<endpoint-id>"
dotnet run --project .\src\FifineControl.Cli -- monitor "<endpoint-id>" 30
dotnet run --project .\src\FifineControl.Cli -- record "<endpoint-id>" 15 ".\recordings" "sc3-test"
dotnet run --project .\src\FifineControl.Cli -- route "<capture-id>" "<render-id>" 30 0
```

Outros comandos são exibidos por `dotnet run --project .\src\FifineControl.Cli -- help`. Configurações e logs são armazenados em `%LOCALAPPDATA%\FifineControl`.

## Arquitetura

- `FifineControl.Core/Audio`: enumeração, mute, volume e medição de endpoints do Core Audio.
- `FifineControl.Core/Dsp`: ganho digital, noise gate, compressor, EQ paramétrico de três bandas e medidores pré/pós.
- `FifineControl.Core/Routing`: roteamento limitado de captura para reprodução via WASAPI com atualização de parâmetros DSP em tempo real e verificações de segurança de rota.
- `FifineControl.Core/Recording`: captura WASAPI e ciclo de vida de arquivo WAV com tolerância a falhas.
- `FifineControl.Core/Configuration`: perfis e persistência atômica validada.
- `FifineControl.Core/Integrations/Obs`: cliente WebSocket nativo para autenticação no protocolo OBS e requisições de gravação.
- `FifineControl.Core/Hotkeys`: ciclo de vida do `RegisterHotKey` configurável e despacho de mensagens.
- `FifineControl.Core/Startup`: registro de inicialização do usuário atual isolado atrás de uma abstração de registro.
- `FifineControl.Core/Logging`: log de arquivo estruturado com baixa dependência.
- `FifineControl.Cli`: superfície de diagnóstico/controle executável, ideal para validar o SC3 real antes de adicionar uma GUI.
- `FifineControl.App`: interface desktop WPF, estado/comandos MVVM, timer de gravação, arquivos recentes, diagnósticos e ciclo de vida da bandeja.

As interfaces mantêm isolados o acesso a endpoints, roteamento/DSP, gravação, hotkeys, OBS e a investigação HID somente leitura. Decodificar o protocolo HID proprietário — e decidir se um driver de áudio virtual é justificado — são trabalhos futuros.

## Sequência segura de validação do SC3

1. Execute `devices` e identifique todos os endpoints cujo nome contém `Mixer SC3`.
2. Execute `status` para cada endpoint e salve a saída.
3. Execute `monitor` no endpoint de captura e fale no microfone.
4. Pressione o botão físico de mute durante o monitoramento. Verifique se o medidor cai e execute `status` novamente para ver se a propriedade de mute do Windows mudou.
5. Execute `toggle`; verifique o sinal capturado e o LED físico de forma independente.
6. Faça uma gravação curta e reproduza em um player confiável.

Nenhum firmware, descritor USB, driver ou estado de hardware é modificado por este PoC.
