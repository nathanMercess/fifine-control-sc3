# SC3 HID Monitor

Ferramenta Windows/.NET 8 somente leitura para enumerar e observar as interfaces HID do FIFINE AmpliGame SC3.

## Segurança

- O filtro é fixo em VID `0x3142` e PID `0x0C33`.
- Paths HID sem esse VID/PID são descartados antes de serem abertos; teclados e mouses genéricos não são capturados.
- `HidD_GetAttributes` confirma novamente VID/PID antes da leitura.
- O monitor abre `GENERIC_READ` e lê apenas input reports.
- O código não declara nem chama rotinas para enviar output reports ou feature reports.
- Nenhum driver ou filtro de captura é instalado e nenhum firmware é alterado.

Os comprimentos de output/feature exibidos vêm do descritor HID e não significam que a ferramenta os utiliza.

## Compilar

```powershell
dotnet build .\tools\SC3.HidMonitor\SC3.HidMonitor.csproj -c Release
```

## Enumerar descritores

```powershell
dotnet run --project .\tools\SC3.HidMonitor -- list
dotnet run --project .\tools\SC3.HidMonitor -- list --json
dotnet run --project .\tools\SC3.HidMonitor -- list --interface MI_04
```

A saída inclui path, interface USB, VID/PID confirmado, versão, Usage Page/Usage, comprimentos dos input/output/feature reports e strings expostas pelo dispositivo.

## Monitorar input reports

```powershell
dotnet run --project .\tools\SC3.HidMonitor -- monitor --duration 30
dotnet run --project .\tools\SC3.HidMonitor -- monitor --duration 60 --interface MI_04
```

Cada report recebido gera uma linha TSV com timestamp ISO 8601, interface, número de bytes e conteúdo hexadecimal. A primeira posição normalmente é o Report ID quando o descritor usa IDs.

Use uma duração curta e pressione um controle físico por vez, anotando o horário e a ação. `Ctrl+C` cancela antecipadamente. Algumas interfaces de Consumer Control podem estar ocupadas pelo Windows e retornar acesso negado; isso não impede tentar a interface vendor-defined `MI_04` separadamente.
