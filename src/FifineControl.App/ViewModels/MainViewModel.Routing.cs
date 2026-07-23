using FifineControl.Core.Dsp;
using FifineControl.Core.Routing;

namespace FifineControl.App.ViewModels;

public sealed partial class MainViewModel
{
    private IAudioRoutingService routing = null!;
    private bool isRouting;
    private double routingPrePeakPercent;
    private double routingPostPeakPercent;
    private string routingStatusText = "Rota DSP parada.";
    private bool gateBypassed;
    private double gateThresholdDb = -48;
    private bool compressorBypassed;
    private double compressorThresholdDb = -16;
    private double compressorRatio = 3;
    private string selectedEqPreset = "Voz neutra";

    public AsyncRelayCommand ToggleRoutingCommand { get; private set; } = null!;
    public IReadOnlyList<string> EqPresets { get; } = ["Voz neutra", "Presença", "Broadcast", "Bypass"];

    public bool IsRouting
    {
        get => isRouting;
        private set
        {
            if (SetProperty(ref isRouting, value))
            {
                OnPropertyChanged(nameof(RoutingButtonText));
            }
        }
    }

    public double RoutingPrePeakPercent
    {
        get => routingPrePeakPercent;
        private set => SetProperty(ref routingPrePeakPercent, Math.Clamp(value, 0, 100));
    }

    public double RoutingPostPeakPercent
    {
        get => routingPostPeakPercent;
        private set => SetProperty(ref routingPostPeakPercent, Math.Clamp(value, 0, 100));
    }

    public string RoutingStatusText
    {
        get => routingStatusText;
        private set => SetProperty(ref routingStatusText, value);
    }

    public bool GateBypassed
    {
        get => gateBypassed;
        set
        {
            if (SetProperty(ref gateBypassed, value))
            {
                UpdateActiveRoutingSettings();
            }
        }
    }

    public double GateThresholdDb
    {
        get => gateThresholdDb;
        set
        {
            if (SetProperty(ref gateThresholdDb, Math.Clamp(value, -80, -5)))
            {
                UpdateActiveRoutingSettings();
            }
        }
    }

    public bool CompressorBypassed
    {
        get => compressorBypassed;
        set
        {
            if (SetProperty(ref compressorBypassed, value))
            {
                UpdateActiveRoutingSettings();
            }
        }
    }

    public double CompressorThresholdDb
    {
        get => compressorThresholdDb;
        set
        {
            if (SetProperty(ref compressorThresholdDb, Math.Clamp(value, -60, 0)))
            {
                UpdateActiveRoutingSettings();
            }
        }
    }

    public double CompressorRatio
    {
        get => compressorRatio;
        set
        {
            if (SetProperty(ref compressorRatio, Math.Clamp(value, 1, 20)))
            {
                UpdateActiveRoutingSettings();
            }
        }
    }

    public string SelectedEqPreset
    {
        get => selectedEqPreset;
        set
        {
            if (SetProperty(ref selectedEqPreset, value))
            {
                UpdateActiveRoutingSettings();
            }
        }
    }

    public string RoutingButtonText => IsRouting ? "Parar rota DSP" : "Iniciar rota DSP";

    private void InitializeRouting()
    {
        routing = new WasapiAudioRoutingService(logger);
        ToggleRoutingCommand = new AsyncRelayCommand(
            ToggleRoutingAsync,
            () => SelectedCaptureDevice is not null && SelectedMonitorDevice is not null);
    }

    private async Task ToggleRoutingAsync()
    {
        if (IsRouting)
        {
            await StopRoutingAsync();
            return;
        }

        if (SelectedCaptureDevice is null || SelectedMonitorDevice is null)
        {
            LastMessage = "Selecione os endpoints de captura e saída.";
            return;
        }

        try
        {
            var feedbackWarning = RoutingSafety.GetFeedbackWarning(
                SelectedCaptureDevice.Name,
                SelectedMonitorDevice.Name);
            if (feedbackWarning is not null)
            {
                var confirmation = System.Windows.MessageBox.Show(
                    $"{feedbackWarning}{Environment.NewLine}{Environment.NewLine}Deseja iniciar mesmo assim?",
                    "Risco de feedback",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.No);
                if (confirmation != System.Windows.MessageBoxResult.Yes)
                {
                    RoutingStatusText = "Rota cancelada: selecione um cabo virtual ou confirme conscientemente o risco.";
                    LastMessage = "Rota DSP não iniciada.";
                    return;
                }
            }

            var session = await routing.StartAsync(
                SelectedCaptureDevice.Id,
                SelectedMonitorDevice.Id,
                CreateRoutingSettings());
            IsRouting = true;
            RoutingStatusText = session.Warning ??
                $"Ativa: {session.CaptureDeviceName} → {session.RenderDeviceName}";
            LastMessage = "Rota DSP iniciada. Use fones e evite monitoramento duplicado.";
        }
        catch (Exception ex)
        {
            HandleError("routing.ui.start.failed", "Não foi possível iniciar a rota DSP.", ex);
            RoutingStatusText = "Falha ao iniciar; consulte os logs.";
        }
    }

    private async Task StopRoutingAsync()
    {
        try
        {
            await routing.StopAsync();
            LastMessage = "Rota DSP encerrada.";
        }
        catch (Exception ex)
        {
            HandleError("routing.ui.stop.failed", "Falha ao encerrar a rota DSP.", ex);
        }
        finally
        {
            IsRouting = false;
            RoutingPrePeakPercent = 0;
            RoutingPostPeakPercent = 0;
            RoutingStatusText = "Rota DSP parada.";
        }
    }

    private void UpdateActiveRoutingSettings()
    {
        if (!IsRouting)
        {
            return;
        }

        try
        {
            routing.UpdateSettings(CreateRoutingSettings());
        }
        catch (Exception ex)
        {
            HandleError("routing.ui.update.failed", "Não foi possível atualizar o DSP da rota.", ex);
        }
    }

    private void UpdateRoutingMeters()
    {
        if (!IsRouting)
        {
            return;
        }

        if (routing.Current is null)
        {
            IsRouting = false;
            RoutingStatusText = "A rota foi interrompida; consulte os logs.";
            return;
        }

        RoutingPrePeakPercent = routing.PreDspPeak * 100;
        RoutingPostPeakPercent = routing.PostDspPeak * 100;
    }

    private DspSettings CreateRoutingSettings() => new()
    {
        DigitalGainDb = (float)DigitalGainDb,
        NoiseGate = new NoiseGateSettings
        {
            Bypassed = GateBypassed,
            ThresholdDb = (float)GateThresholdDb
        },
        Compressor = new CompressorSettings
        {
            Bypassed = CompressorBypassed,
            ThresholdDb = (float)CompressorThresholdDb,
            Ratio = (float)CompressorRatio
        },
        EqualizerBands = CreateEqBands(SelectedEqPreset)
    };

    private static IReadOnlyList<ParametricEqBandSettings> CreateEqBands(string preset) => preset switch
    {
        "Presença" =>
        [
            Band("Low", 120, -2),
            Band("Mid", 2_500, 3),
            Band("High", 8_000, 2)
        ],
        "Broadcast" =>
        [
            Band("Low", 100, 3),
            Band("Mid", 500, -2),
            Band("High", 6_000, 2)
        ],
        "Bypass" =>
        [
            Band("Low", 120, 0, true),
            Band("Mid", 1_200, 0, true),
            Band("High", 8_000, 0, true)
        ],
        _ =>
        [
            Band("Low", 120, 0),
            Band("Mid", 1_200, 0),
            Band("High", 8_000, 0)
        ]
    };

    private static ParametricEqBandSettings Band(string name, float frequency, float gain, bool bypassed = false) => new()
    {
        Name = name,
        FrequencyHz = frequency,
        Q = 0.9f,
        GainDb = gain,
        Bypassed = bypassed
    };

    private void RaiseRoutingCanExecuteChanged() => ToggleRoutingCommand?.RaiseCanExecuteChanged();

    private void DisposeRouting() => routing.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
