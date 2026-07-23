# Roteamento e DSP em tempo real

Esta camada implementa o caminho de software do Nível 1:

```text
SC3 (endpoint de captura)
    → WASAPI compartilhado
    → buffer de 500 ms (descarta o bloco mais antigo em overflow)
    → ganho digital
    → noise gate
    → compressor
    → EQ paramétrico de 3 bandas
    → limitador de segurança [-1, +1]
    → WASAPI compartilhado
    → endpoint de renderização selecionado
```

Ela não instala nem cria um driver de áudio virtual. Para entregar o áudio processado a Discord, OBS ou jogos, selecione como destino um endpoint de renderização de uma solução já instalada, por exemplo **CABLE Input**. No aplicativo consumidor, selecione o endpoint de captura correspondente, normalmente **CABLE Output**. No VoiceMeeter, use uma entrada virtual equivalente.

## API

O contrato público é `IAudioRoutingService`; a implementação Windows é `WasapiAudioRoutingService`.

```csharp
await using var route = new WasapiAudioRoutingService(logger);

var dsp = new DspSettings
{
    DigitalGainDb = 3,
    NoiseGate = new NoiseGateSettings
    {
        ThresholdDb = -42,
        AttackMs = 4,
        ReleaseMs = 120
    },
    Compressor = new CompressorSettings
    {
        ThresholdDb = -16,
        Ratio = 3,
        AttackMs = 10,
        ReleaseMs = 100,
        MakeupGainDb = 2
    },
    EqualizerBands =
    [
        new() { Name = "Low", FrequencyHz = 120, Q = 0.8f, GainDb = -2 },
        new() { Name = "Mid", FrequencyHz = 1_200, Q = 1.0f, GainDb = 1.5f },
        new() { Name = "High", FrequencyHz = 8_000, Q = 0.8f, GainDb = 2 }
    ]
};

var session = await route.StartAsync(captureEndpointId, renderEndpointId, dsp);
Console.WriteLine(session.Warning);
Console.WriteLine($"Antes: {route.PreDspPeak:P0}; depois: {route.PostDspPeak:P0}");

// Os parâmetros podem ser trocados durante a rota.
route.UpdateSettings(dsp with { DigitalGainDb = 0 });
await route.StopAsync();
```

Cada estágio possui bypass independente:

- `DspSettings.GainBypassed`
- `NoiseGateSettings.Bypassed`
- `CompressorSettings.Bypassed`
- `ParametricEqBandSettings.Bypassed`, por banda

`PreDspPeak` mede o maior valor absoluto do último bloco antes do processamento. `PostDspPeak` mede o bloco depois de toda a cadeia e do limitador.

## Proteção contra feedback e rotas inválidas

A inicialização falha antes de abrir a rota quando:

- os IDs de captura e renderização são iguais;
- os nomes normalizados dos endpoints são iguais;
- a origem não é um endpoint de captura ativo;
- o destino não é um endpoint de renderização ativo.

Quando os nomes indicam o mesmo equipamento físico entre parênteses, por exemplo `Microphone (fifine SC3)` e `Speakers (fifine SC3)`, a rota ainda pode ser válida para fones, mas `AudioRoutingSession.HasFeedbackRisk` e `Warning` avisam do risco. Essa verificação não consegue detectar todas as rotas acústicas ou loops criados dentro de VoiceMeeter/OBS.

Antes de monitorar em saída física:

1. use fones de ouvido;
2. reduza o volume de saída;
3. desative monitoramento duplicado no Windows, OBS e VoiceMeeter;
4. aumente o volume gradualmente;
5. pare a rota imediatamente se surgir realimentação.

Nunca direcione a saída virtual de volta para a mesma entrada lógica dentro de outro aplicativo. Loops digitais podem atingir nível máximo mesmo sem alto-falantes.

## Comportamento operacional

- Captura e saída usam WASAPI em modo compartilhado.
- A saída começa consumindo silêncio antes da captura, evitando underrun na partida.
- Um buffer limitado absorve pequenas variações de agendamento; em overflow, áudio antigo é descartado em vez de aumentar a latência indefinidamente.
- Parada explícita desinscreve eventos, encerra e libera captura/saída separadamente.
- Desconexão ou falha inesperada de uma ponta agenda a limpeza da rota inteira e registra o motivo.
- Formatos incompatíveis, endpoints em modo exclusivo ou dispositivos desconectados causam falha registrada; nenhuma configuração do driver é modificada.

O monitoramento por software tem latência. O valor real depende dos drivers, do formato negociado e do endpoint virtual/físico.

## Testes

Os testes DSP usam vetores determinísticos e não acessam hardware:

```powershell
dotnet test .\tests\FifineControl.Core.Tests\FifineControl.Core.Tests.csproj -c Release
```

Eles verificam ganho e picos, fechamento do gate, curva do compressor, ganho do EQ na frequência central, bypass transparente, validação de Nyquist e bloqueios/avisos de rota.

Um teste completo de áudio real deve ser feito manualmente porque depende dos endpoints instalados. Nesta máquina não foi aberta automaticamente uma rota SC3 → alto-falantes: isso poderia produzir feedback e alterar o áudio ouvido pelo usuário. Prefira validar primeiro com um cabo virtual e um medidor no destino.
