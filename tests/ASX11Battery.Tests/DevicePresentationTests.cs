using ASX11Battery.App.ViewModels;
using ASX11Battery.Core.Abstractions;
using ASX11Battery.Core.Services;
using Xunit;

namespace ASX11Battery.Tests;

/// <summary>
/// What the user is shown, from the snapshot upwards.
/// </summary>
/// <remarks>
/// The single rule under test: a read that failed is displayed as unavailable. It
/// is never rounded, extrapolated, carried over from a previous good reading, or
/// replaced by a plausible-looking default. A battery tool that invents a number
/// is worse than one that shows nothing, because the number gets acted on.
/// </remarks>
public sealed class DevicePresentationTests
{
    private static DeviceSnapshot Snapshot(
        bool connected = true,
        DeviceState state = DeviceState.Connected,
        int? percent = 90,
        bool? charging = null) => new(
        Id: "attackshark-x11",
        Name: "Attack Shark X11",
        Manufacturer: "Attack Shark",
        Model: "Attack Shark X11",
        IconKind: "mouse",
        Connection: ConnectionKind.TwoPointFourGigahertz,
        Connected: connected,
        State: state,
        BatteryPercent: percent,
        Charging: charging,
        LastUpdate: DateTimeOffset.Now,
        StatusMessage: "Conectado",
        ProtocolNote: null);

    [Fact]
    public void ShowsTheRealPercentageWhenTheReadSucceeded()
    {
        var vm = new DeviceItemViewModel(Snapshot(percent: 90));

        Assert.Equal(90, vm.BatteryPercent);
        Assert.Equal("Conectado", vm.RingCaption);
    }

    [Fact]
    public void SaysUnavailableRatherThanShowingNothingWhenTheReadFailed()
    {
        var vm = new DeviceItemViewModel(
            Snapshot(state: DeviceState.BatteryUnavailable, percent: null));

        Assert.Null(vm.BatteryPercent);
        Assert.Equal("Bateria indisponÃ­vel", vm.RingCaption);
        Assert.Equal("Bateria indisponÃ­vel", vm.StateText);
    }

    [Fact]
    public void SaysDisconnectedRatherThanUnavailableWhenThereIsNoDevice()
    {
        // "Unplugged" and "present but silent" are different facts, and a user
        // chasing a charging problem needs to be able to tell them apart.
        var vm = new DeviceItemViewModel(
            Snapshot(connected: false, state: DeviceState.Disconnected, percent: null));

        Assert.Equal("Desconectado", vm.RingCaption);
        Assert.Equal("Desconectado", vm.StateText);
    }

    [Fact]
    public void KeepsUnknownChargingUnknownRatherThanAssumingNotCharging()
    {
        // Charging is a three-state value on this device: the wired identity is
        // what proves it. Coercing null to false would state something the app
        // does not know.
        var vm = new DeviceItemViewModel(Snapshot(charging: null));

        Assert.Null(vm.Charging);
        Assert.Equal("Conectado", vm.RingCaption);
    }

    [Fact]
    public void ReportsChargingWhenTheWiredIdentityIsPresent()
    {
        var vm = new DeviceItemViewModel(Snapshot(charging: true));

        Assert.True(vm.Charging);
        Assert.Equal("Carregando", vm.StateText);
        Assert.Equal("Carregando", vm.RingCaption);
    }

    [Fact]
    public void KeepsThePreviousLevelOutOfTheSnapshotWhenARereadFails()
    {
        // The real regression this guards: a device that reports 90% and then goes
        // quiet must show "indisponÃ­vel", not a stale 90% that looks current.
        var vm = new DeviceItemViewModel(Snapshot(percent: 90));

        vm.Apply(Snapshot(state: DeviceState.BatteryUnavailable, percent: null));

        Assert.Null(vm.BatteryPercent);
        Assert.Equal("Bateria indisponÃ­vel", vm.RingCaption);
    }

    [Fact]
    public void TheOnlyGlyphIsTheMouseOne()
    {
        Assert.Equal("\uE7F4", new DeviceItemViewModel(Snapshot()).Glyph);
    }

    [Fact]
    public void ClampsThePollIntervalToSomethingSane()
    {
        // A zero or negative interval would spin the poll loop; the clamp is what
        // keeps a hand-edited settings file from turning into a busy wait.
        Assert.Equal(TimeSpan.FromSeconds(1), new AppSettings { PollIntervalSeconds = 0 }.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(1), new AppSettings { PollIntervalSeconds = -99 }.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), new AppSettings { PollIntervalSeconds = 900 }.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(3), new AppSettings().PollInterval);
    }

    [Fact]
    public void SettingsLiveUnderTheApplicationName()
    {
        // The path is part of the app's identity: it must not still point at the
        // old name, and it must stay inside the user profile so the app never
        // needs elevation.
        Assert.EndsWith("ASX11Battery", SettingsStore.Directory, StringComparison.Ordinal);
        Assert.EndsWith("settings.json", SettingsStore.FilePath, StringComparison.Ordinal);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            SettingsStore.Directory,
            StringComparison.OrdinalIgnoreCase);
    }
}
