using HarmonyLib;
using MelonLoader;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: MelonInfo(typeof(LaunderingStatus.Mod), "Businesses", "0.1.3", "Pratik")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]

namespace LaunderingStatus;

public sealed class Mod : MelonMod
{
    internal static Mod Instance = null!;
    private bool _testLoaded;
    private bool _dumped;
    private float _nextTick;
    private PhoneStatusApp? _app;
    private bool _appFailed;
    internal static string? TestSave => Argument("--laundry-test-save");
    internal static bool Diagnostics => Environment.GetCommandLineArgs().Contains("--laundry-diagnostics");

    public override void OnInitializeMelon()
    {
        Instance = this;
        LoggerInstance.Msg("Read-only laundering phone app initializing. No economy or save-data patches.");
        HarmonyInstance.Patch(AccessTools.Method(typeof(Phone), nameof(Phone.RequestCloseApp)),
            prefix: new HarmonyMethod(typeof(Mod), nameof(ClosePhoneApp)));
        // GameInput.LateUpdate clears buttonsDownThisFrame. A Melon late-update
        // callback runs after that clear; inspect input immediately before it.
        HarmonyInstance.Patch(AccessTools.Method(typeof(Il2CppScheduleOne.GameInput), "LateUpdate"),
            prefix: new HarmonyMethod(typeof(Mod), nameof(BeforeInputReset)));
    }

    private static void ClosePhoneApp() => Instance._app?.Close();

    private static void BeforeInputReset()
    {
        try { Instance._app?.HandleInput(); }
        catch (Exception e) { Instance.LoggerInstance.Error("Phone input: " + e); }
    }

    public override void OnUpdate()
    {
        if (Time.realtimeSinceStartup < _nextTick) return;
        _nextTick = Time.realtimeSinceStartup + 1;
        try
        {
            if (Singleton<LoadManager>.InstanceExists && Singleton<LoadManager>.Instance.IsGameLoaded)
            {
                if ((_app == null || !_app.Alive) && !_appFailed && HomeScreen.InstanceExists && Phone.InstanceExists)
                {
                    _app?.Release();
                    try { _app = new PhoneStatusApp(HomeScreen.Instance); }
                    catch { _appFailed = true; throw; }
                }
                _app?.Tick();
            }
            if (!_testLoaded && TestSave is { } path && Singleton<LoadManager>.InstanceExists &&
                !Singleton<LoadManager>.Instance.IsLoading && !Singleton<LoadManager>.Instance.IsGameLoaded &&
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Menu")
            {
                _testLoaded = true;
                if (!path.Contains("playtest", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Test-save path must contain 'playtest'.");
                if (!LoadManager.TryLoadSaveInfo(path, 4, out var save, true))
                    throw new InvalidOperationException("Cannot read test save " + path);
                LoggerInstance.Msg("Loading isolated playtest copy: " + path);
                Singleton<LoadManager>.Instance.StartGame(save, false, false);
            }
            if (!_dumped && Diagnostics && Singleton<LoadManager>.InstanceExists &&
                Singleton<LoadManager>.Instance.IsGameLoaded && Singleton<LoadManager>.Instance.TimeSinceGameLoaded > 3)
            {
                _dumped = true;
                Dump();
            }
        }
        catch (Exception e) { LoggerInstance.Error(e); }
    }

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
        if (sceneName != "Main") return;
        _app?.Release();
        _app = null;
        _appFailed = false;
        _dumped = false;
    }

    internal static string? Argument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    internal static void Dump()
    {
        var lines = new List<string>();
        var interfaces = Object.FindObjectsOfType<LaunderingInterface>(true);
        lines.Add("Laundering interfaces: " + interfaces.Length);
        foreach (var ui in interfaces)
        {
            lines.Add($"INTERFACE {ui.name} business={ui.Business?.PropertyName} root={PathOf(ui.transform)}");
            lines.Add($"container={PathOf(ui.container)} entries={PathOf(ui.entryContainer)} canvas={PathOf(ui.canvas?.transform)}");
            if (ui.container != null) Tree(ui.container, lines, 0);
            if (ui.entryPrefab != null) { lines.Add("ENTRY PREFAB"); Tree(ui.entryPrefab.transform, lines, 0); }
            if (ui.timelineNotchPrefab != null) { lines.Add("MARKER PREFAB"); Tree(ui.timelineNotchPrefab.transform, lines, 0); }
        }
        foreach (var home in Object.FindObjectsOfType<HomeScreen>(true))
        {
            lines.Add("HOME " + PathOf(home.transform));
            Tree(home.transform.parent, lines, 0, 3);
        }
        if (Business.OwnedBusinesses != null)
            foreach (var bus in Business.OwnedBusinesses)
            {
                lines.Add($"BUSINESS {bus.PropertyName} pending={bus.currentLaunderTotal} limit={bus.appliedLaunderLimit}");
                foreach (var op in bus.LaunderingOperations)
                    lines.Add($"OP amount={op.amount} elapsed={op.minutesSinceStarted} duration={op.completionTime_Minutes}");
            }
        var file = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "LaunderingStatus-inspection.txt");
        File.WriteAllLines(file, lines);
        Instance.LoggerInstance.Msg("Diagnostics written: " + file);
    }

    private static string PathOf(Transform? t) => t == null ? "null" : (t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name);
    private static void Tree(Transform t, List<string> lines, int level, int max = 7)
    {
        if (level > max) return;
        var rect = t.GetComponent<RectTransform>();
        var text = t.GetComponent<Il2CppTMPro.TMP_Text>();
        var legacy = t.GetComponent<UnityEngine.UI.Text>();
        lines.Add(new string(' ', level * 2) + t.name + " rect=" + (rect == null ? "none" : rect.rect.ToString() + " anchors=" + rect.anchorMin + "-" + rect.anchorMax + " offset=" + rect.anchoredPosition + " sizeDelta=" + rect.sizeDelta) +
                  " components=" + string.Join(",", t.GetComponents<Component>().Select(c => c.GetIl2CppType().Name)) +
                  " text=" + (text != null ? text.text : legacy != null ? legacy.text : ""));
        for (var i = 0; i < t.childCount; i++) Tree(t.GetChild(i), lines, level + 1, max);
    }
}
