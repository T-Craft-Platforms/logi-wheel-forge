using LogiWheelForge.Models;
using LogiWheelForge.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiWheelForge.Core.Tests;

[TestClass]
public sealed class WheelSelectionTests
{
    private static WheelInterface Wheel(string id, string name = "G29", string? path = null) => new(id, path ?? id + "/input", name, "g29", true, false);
    [TestMethod]
    public void GroupsInterfaces_DistinguishesIdenticalWheelsAndExcludesUsbPedals()
    {
        var devices = ConnectedWheelSelection.GroupInterfaces([Wheel("b"), Wheel("a"),
            new("a", "a/led", "G29", "g29", false, false), new("pedals", "pedal/input", "USB pedals", "pedals", true, true)]);
        Assert.AreEqual(2, devices.Count); Assert.AreEqual("a", devices[0].InstanceId);
        Assert.AreEqual(2, devices[0].HidInterfaces.Count);
        Assert.AreNotEqual(devices[0].PickerLabel, devices[1].PickerLabel);
        Assert.AreEqual("a", devices[0].ResistanceDeviceIdentity);
    }
    [TestMethod]
    public void Selection_IsDeterministicPreservesCurrentOnArrivalAndReplacesOnDisconnect()
    {
        var selection = new ConnectedWheelSelection();
        selection.Update([]); Assert.IsNull(selection.ActiveDevice);
        selection.Update([Wheel("b", "Zulu"), Wheel("a", "Alpha")]); Assert.AreEqual("a", selection.ActiveWheelId);
        selection.Update([Wheel("c", "Aardvark"), Wheel("b", "Zulu"), Wheel("a", "Alpha")]); Assert.AreEqual("a", selection.ActiveWheelId);
        selection.Update([Wheel("b", "Zulu"), Wheel("c", "Aardvark")]); Assert.AreEqual("c", selection.ActiveWheelId);
        selection.Update([]); Assert.IsNull(selection.ActiveDevice);
    }
    [TestMethod]
    public void DisconnectedAndLegacyPreferences_DoNotBlockDetectionOrReclaimReplacement()
    {
        var selection = new ConnectedWheelSelection { PreferredInstanceId = "g29" };
        selection.Update([Wheel("a")]); Assert.AreEqual("a", selection.ActiveWheelId);
        selection.PreferredInstanceId = "unplugged";
        selection.Update([Wheel("a")]); Assert.AreEqual("a", selection.ActiveWheelId);
        selection.Update([Wheel("a"), Wheel("unplugged")]); Assert.AreEqual("a", selection.ActiveWheelId);
        selection.PreferredInstanceId = "unplugged";
        selection.Update([Wheel("a"), Wheel("unplugged")]); Assert.AreEqual("unplugged", selection.ActiveWheelId);
    }
    [TestMethod]
    public void DiscoverySnapshots_UpdateWhileSettingsIsMounted()
    {
        var selection = new ConnectedWheelSelection(); var updates = 0;
        selection.DevicesChanged += (_, _) => updates++;
        selection.Update([Wheel("a")]);
        selection.Update([Wheel("a")]); Assert.AreEqual(1, updates);
        selection.Update([Wheel("a"), new("a", "a/led", "G29", "g29", false, false)]);
        Assert.AreEqual(2, updates); Assert.AreEqual(2, selection.ActiveDevice!.HidInterfaces.Count);
    }
}
