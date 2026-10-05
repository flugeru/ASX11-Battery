using System;
using ASX11Battery.Core.Abstractions;
using ASX11Battery.App.ViewModels;

namespace ASX11Battery.App.ViewModels;

/// <summary>View model for a single device shown in the dashboard.</summary>
public sealed class DeviceItemViewModel : ViewModelBase
{
    private int? _batteryPercent;
    private bool? _charging;
    private ConnectionType _connection;
    private DeviceState _state;
    private string _statusMessage = string.Empty;
    private DateTimeOffset? _lastUpdate;

    public DeviceItemViewModel(DeviceSnapshot snapshot) => Update(snapshot);

    public string Id { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Glyph { get; private set; } = "\uE7F4"; // Mouse glyph

    public int? BatteryPercent
    {
        get => _batteryPercent;
        private set => SetProperty(ref _batteryPercent, value);
    }

    public bool? Charging
    {
        get => _charging;
        private set => SetProperty(ref _charging, value);
    }

    public ConnectionType Connection
    {
        get => _connection;
        private set => SetProperty(ref _connection, value);
    }

    public DeviceState State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public DateTimeOffset? LastUpdate
    {
        get => _lastUpdate;
        private set => SetProperty(ref _lastUpdate, value);
    }

    public string RingCaption =>
        State == DeviceState.Disconnected ? "Sem conexão"
        : State == DeviceState.BatteryUnavailable ? "Sem leitura"
        : Charging == true ? "Carregando"
        : "Conectado";

    public string ConnectionText =>
        State == DeviceState.Disconnected ? "Sem conexão"
        : Connection switch
        {
            ConnectionType.UsbWired when Charging == true => "USB · carregando",
            ConnectionType.UsbWired => "USB",
            ConnectionType.Bluetooth => "Bluetooth",
            ConnectionType.Wireless24Ghz => "2.4 GHz",
            _ => "Conectado",
        };

    public string StateText =>
        State == DeviceState.Disconnected ? "Desconectado"
        : Charging == true ? "Carregando"
        : "Conectado";

    public string LastUpdateText =>
        LastUpdate.HasValue ? $"Última leitura · {LastUpdate.Value:HH:mm:ss}"
        : "Aguardando primeira leitura";

    public void Apply(DeviceSnapshot snapshot) => Update(snapshot);

    public void Update(DeviceSnapshot snapshot)
    {
        Id = snapshot.Id;
        Name = snapshot.DisplayName;
        BatteryPercent = snapshot.BatteryPercent;
        Connection = snapshot.Connection;
        Charging = snapshot.Charging;
        State = snapshot.State;
        StatusMessage = snapshot.StatusMessage ?? string.Empty;
        LastUpdate = snapshot.LastUpdate;

        // These are calculated presentation properties and therefore have to be
        // invalidated when their source values change.
        OnPropertyChanged(nameof(RingCaption));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(LastUpdateText));
        OnPropertyChanged(nameof(ConnectionText));
    }
}