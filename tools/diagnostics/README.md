# Diagnóstico local do FIFINE AmpliGame SC3

Estas ferramentas fazem somente leituras das APIs PnP/CIM e Core Audio do Windows. Elas não instalam drivers, não escrevem no dispositivo, não abrem interfaces HID para envio de relatórios e não alteram firmware, volume ou mute.

## Requisitos

- Windows PowerShell 5.1 ou PowerShell 7 em Windows.
- O mixer conectado por USB.
- Uma sessão comum de usuário costuma ser suficiente; execute como administrador apenas se o Windows negar a leitura de alguma propriedade.

## Inventário

No PowerShell, a partir da raiz do repositório:

```powershell
.\tools\diagnostics\Get-SC3Inventory.ps1 -PresentOnly | Format-List
```

Para gerar JSON que possa ser enviado para análise (a gravação do arquivo é feita pelo redirecionamento do próprio PowerShell):

```powershell
.\tools\diagnostics\Get-SC3Inventory.ps1 -PresentOnly -Json -IncludeAllAudioEndpoints |
  Set-Content -Encoding utf8 .\sc3-inventory.json
```

O relatório inclui:

- VID/PID encontrados em IDs USB;
- nós PnP diretamente correspondentes e funções do mesmo VID/PID;
- IDs de hardware e compatibilidade, pai, container, localização, serviço e driver;
- interfaces USB, HID, áudio, candidatos MIDI e portas COM relacionadas;
- endpoints Core Audio de captura e reprodução, estado, quantidade de canais, mute e volumes por canal;
- opcionalmente, todos os endpoints de áudio para descobrir nomes inesperados.

Se o nome do dispositivo for diferente, ajuste a expressão regular:

```powershell
.\tools\diagnostics\Get-SC3Inventory.ps1 -Match 'FIFINE|SC3|nome visto no Gerenciador' -PresentOnly
```

## Monitorar alterações

O comando abaixo observa por 60 segundos as mudanças de topologia PnP e as propriedades de volume/mute dos endpoints Core Audio correspondentes:

```powershell
.\tools\diagnostics\Watch-SC3Changes.ps1 -DurationSeconds 60 -IntervalSeconds 0.5 -ShowInitialState
```

Durante a execução, conecte/desconecte o SC3 e teste mute, volumes e botões. `Changed` em um endpoint Core Audio mostra o estado anterior e o novo. Sem `-DurationSeconds`, o monitor continua até `Ctrl+C`.

## Limites importantes

- O Windows expõe as funções/interfaces USB por PnP, mas não uma lista descritiva dos endpoints internos do descritor USB. Para descritores completos, a próxima etapa segura é usar uma ferramenta de leitura como USB Device Tree Viewer ou uma rotina WinUSB/libusb que apenas consulte descritores.
- Um botão físico só aparecerá neste monitor se provocar mudança PnP ou alteração observável de mute/volume no Core Audio. Relatórios HID, MIDI e pacotes proprietários não ficam visíveis por essas APIs genéricas.
- A presença de `HidInterfaces` ou `MidiCandidates` confirma uma rota possível para uma captura dedicada, mas este conjunto deliberadamente não envia comandos.
- USBPcap/Wireshark é a etapa apropriada para comparar tráfego de controles físicos. A instalação de um filtro de captura muda o sistema e, portanto, não é feita automaticamente por estes scripts.

Para um teste útil, execute primeiro o inventário, depois o monitor e devolva o JSON e as linhas produzidas ao pressionar cada controle, identificando qual controle foi usado em cada momento.
