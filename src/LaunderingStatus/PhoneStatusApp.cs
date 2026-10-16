using System.Globalization;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LaunderingStatus;

/// <summary>
/// Native phone shell following ScheduleOne-Sideload's MIT-licensed host pattern.
/// The contents are the game's own laundering computer panel, without its world
/// interaction controller or deposit screen. Only presentation objects are cloned.
/// </summary>
internal sealed class PhoneStatusApp
{
    private readonly HomeScreen _home;
    private readonly GameObject _panel;
    private readonly GameObject _icon;
    private readonly Text _placeholder;
    private readonly Vector3 _contentScale;
    private readonly Quaternion _contentRotation;
    private readonly Image _backdrop;
    private readonly Il2CppScheduleOne.UI.Phone.JournalApp _nativeApp;
    private Button? _nextButton;
    private Button? _backButton;
    private GameObject? _computerPanel;
    private GameObject? _entryTemplate;
    private GameObject? _markerTemplate;
    private RectTransform? _content;
    private GameObject? _noOperations;
    private RectTransform? _notches;
    private TMP_Text? _title;
    private TMP_Text? _pending;
    private TMP_Text? _capacity;
    private readonly List<Row> _rows = new();
    private readonly List<GameObject> _markers = new();
    private string _rowSignature = "";
    private string _markerSignature = "";
    private string? _selectedCode;
    private readonly GameInput.ExitDelegate _exit;
    private readonly Il2CppSystem.Action _close;
    private bool _released;
    private int _refreshCount;

    private sealed record Row(Business Business, LaunderingOperation? Operation, TMP_Text Name, TMP_Text Amount, TMP_Text Time);
    private sealed record HitProbe(string Button, string Point, string? Hit, bool Passed);

    internal bool Alive => _panel != null && _home != null;
    internal bool IsOpen => Alive && Phone.ActiveApp == _panel;

    internal PhoneStatusApp(HomeScreen home)
    {
        _home = home;
        var apps = home.transform.parent.Find("AppsCanvas")
                   ?? throw new InvalidOperationException("Phone AppsCanvas not found.");
        var template = apps.Find("Journal")?.GetComponent<RectTransform>()
                       ?? throw new InvalidOperationException("Phone landscape template not found.");
        _nativeApp = template.GetComponent<Il2CppScheduleOne.UI.Phone.JournalApp>();
        var contentTemplate = template.Find("Container")
                              ?? throw new InvalidOperationException("Phone content transform not found.");
        _contentScale = contentTemplate.localScale;
        _contentRotation = contentTemplate.localRotation;
        Mod.Instance.LoggerInstance.Msg($"Native phone content geometry: scale={_contentScale}, rotation={_contentRotation.eulerAngles}");
        _panel = new GameObject("LaunderingStatusApp");
        _panel.SetActive(false);
        var rect = _panel.AddComponent<RectTransform>();
        rect.SetParent(apps, false);
        CopyRect(template, rect);
        // The phone renders this canvas from the opposite side to the world
        // computer. Native App.SetOpen supplies this facing rotation at open;
        // our shell must supply it too, including for UI raycasts.
        rect.localRotation = template.localRotation * Quaternion.Euler(0, 180, 0);
        _backdrop = _panel.AddComponent<Image>();
        _backdrop.color = new Color(0.18f, 0.29f, 0.38f, 1);
        _backdrop.raycastTarget = false;

        var empty = new GameObject("NoBusinesses");
        empty.transform.SetParent(rect, false);
        _placeholder = empty.AddComponent<Text>();
        _placeholder.font = home.timeText.font;
        _placeholder.fontSize = 34;
        _placeholder.alignment = TextAnchor.MiddleCenter;
        _placeholder.color = Color.white;
        _placeholder.raycastTarget = false;
        _placeholder.text = "BUSINESSES\n\nNo businesses owned.";
        var emptyRect = empty.GetComponent<RectTransform>();
        emptyRect.anchorMin = Vector2.zero;
        emptyRect.anchorMax = Vector2.one;
        emptyRect.offsetMin = new Vector2(24, 24);
        emptyRect.offsetMax = new Vector2(-24, -24);
        emptyRect.localScale = _contentScale;
        emptyRect.localRotation = _contentRotation;

        _icon = Object.Instantiate(home.appIconPrefab, home.appIconContainer);
        _icon.name = "LaunderingStatusIcon";
        _icon.SetActive(true);
        _icon.transform.Find("Label").GetComponent<Text>().text = "Businesses";
        var iconImage = _icon.transform.Find("Mask/Image").GetComponent<Image>();
        if (NetworkSingleton<MoneyManager>.InstanceExists)
            iconImage.sprite = NetworkSingleton<MoneyManager>.Instance.LaunderingNotificationIcon;
        var badge = _icon.transform.Find("Notifications");
        if (badge != null) badge.gameObject.SetActive(false);
        var button = _icon.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener((UnityEngine.Events.UnityAction)Open);
        var selectable = _icon.GetComponent<UISelectable>();
        if (selectable != null && home.uiPanel != null) home.uiPanel.AddSelectable(selectable);

        _close = (Il2CppSystem.Action)Close;
        Phone.Instance.closeApps += _close;
        _exit = DelegateSupport.ConvertDelegate<GameInput.ExitDelegate>((Action<ExitAction>)(action =>
        {
            if (action.Used || !IsOpen || !Phone.Instance.IsOpen) return;
            action.Used = true;
            Close();
        })) ?? throw new InvalidOperationException("Could not register phone exit handler.");
        GameInput.RegisterExitListener(_exit, 1);
        Mod.Instance.LoggerInstance.Msg("Laundering app registered on the local phone.");
    }

    internal void Tick()
    {
        if (!Alive) return;
        if (IsOpen && Phone.InstanceExists && Phone.Instance.IsOpen) Refresh();
    }

    internal void HandleInput()
    {
        if (!IsOpen || !Phone.InstanceExists || !Phone.Instance.IsOpen) return;
        if (!GameInput.GetButtonDown(GameInput.ButtonCode.PrimaryClick)) return;
        // Native App.Update performs this collider test before closing an app.
        // Reuse its helper so the physical Home button follows the actual model,
        // orientation and camera rather than a separate screen-space hitbox.
        var home = _nativeApp != null && _nativeApp.IsHoveringButton();
        if (Mod.Diagnostics) Mod.Instance.LoggerInstance.Msg("Phone click before input reset: Home=" + home);
        if (home)
        {
            Mod.Instance.LoggerInstance.Msg("Physical phone Home button pressed.");
            Phone.Instance.RequestCloseApp();
        }
    }

    internal void Open()
    {
        try
        {
            Phone.Instance.RequestCloseApp();
            Refresh();
            AppsCanvas.Instance.SetIsOpen(true);
            _home.SetIsOpen(false);
            Phone.Instance.SetIsHorizontal(true);
            Phone.Instance.SetLookOffsetMultiplier(0.6f);
            Phone.ActiveApp = _panel;
            _panel.SetActive(true);
            Canvas.ForceUpdateCanvases();
            Mod.Instance.LoggerInstance.Msg($"Opened laundering status: {_rows.Count} rows.");
            if (Mod.Diagnostics) WriteSnapshot();
        }
        catch (Exception e) { Mod.Instance.LoggerInstance.Error("Opening laundering app: " + e); }
    }

    internal void Close()
    {
        if (!IsOpen) return;
        Phone.ActiveApp = null;
        _panel.SetActive(false);
        if (AppsCanvas.InstanceExists) AppsCanvas.Instance.SetIsOpen(false);
        if (_home != null) _home.SetIsOpen(true);
        if (Phone.InstanceExists)
        {
            Phone.Instance.SetIsHorizontal(false);
            Phone.Instance.SetLookOffsetMultiplier(1);
        }
    }

    private void BuildComputerPanel(LaunderingInterface source)
    {
        _entryTemplate = source.entryPrefab;
        _markerTemplate = source.timelineNotchPrefab;
        // Parent is inactive until the app opens. Remove game interaction/state
        // components before enabling the cloned presentation tree.
        _computerPanel = Object.Instantiate(source.container.gameObject, _panel.transform);
        _computerPanel.name = "NativeLaunderingComputerPanel";
        _computerPanel.SetActive(false);
        var selector = _computerPanel.transform.Find("NewLaunder");
        if (selector != null) Object.DestroyImmediate(selector.gameObject);
        foreach (var component in _computerPanel.GetComponentsInChildren<MonoBehaviour>(true))
            if (component.GetIl2CppType().Name is "UIContentPanel" or "UIScreen" or "UIScreenMonoState" or "UISelectable")
                Object.DestroyImmediate(component);

        var rt = _computerPanel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition3D = Vector3.zero;
        rt.localRotation = _contentRotation;
        rt.sizeDelta = new Vector2(400, 268);
        var screen = _panel.GetComponent<RectTransform>().rect;
        var scale = Math.Min(screen.width / 400, screen.height / 268) * 0.96f;
        rt.localScale = _contentScale * scale;

        var root = _computerPanel.transform;
        var nativeBackground = root.Find("Background").GetComponent<Image>();
        _backdrop.sprite = nativeBackground.sprite;
        _backdrop.type = nativeBackground.type;
        _backdrop.material = nativeBackground.material;
        var backgroundColor = nativeBackground.color;
        backgroundColor.a = 1;
        _backdrop.color = backgroundColor;
        _title = Label(root, "Topbar/Text (TMP)");
        _title.fontSize = 14;
        var titleRect = _title.GetComponent<RectTransform>();
        titleRect.anchorMin = titleRect.anchorMax = titleRect.pivot = new Vector2(0, 0.5f);
        titleRect.anchoredPosition = new Vector2(5, 0);
        titleRect.sizeDelta = new Vector2(280, 29);
        _pending = Label(root, "CurrentOperations/Total/Text");
        _capacity = Label(root, "CurrentOperations/Total/Max");
        root.Find("CurrentOperations/InsufficientCash").gameObject.SetActive(false);
        Label(root, "OperationList/Title").text = "All Active Laundering Operations";
        var listRect = root.Find("OperationList").GetComponent<RectTransform>();
        listRect.sizeDelta += new Vector2(0, 25);
        listRect.anchoredPosition += new Vector2(0, 12.5f);
        _content = root.Find("OperationList/Scroll View/Viewport/Content").GetComponent<RectTransform>();
        _noOperations = _content.Find("No Active Laundering Operations")?.gameObject;
        ClearRows();
        _notches = root.Find("CurrentOperations/Notches").GetComponent<RectTransform>();
        ClearChildren(_notches);

        var next = root.Find("CurrentOperations/LaunderButton").GetComponent<Button>();
        next.gameObject.name = "NextBusiness";
        next.onClick = new Button.ButtonClickedEvent(); // also removes serialized deposit listeners
        next.interactable = true;
        next.onClick.AddListener((UnityEngine.Events.UnityAction)NextBusiness);
        Label(next.transform, "Text (TMP)").text = "Next";
        var frame = next.transform.Find("Selected Frame");
        if (frame != null) frame.gameObject.SetActive(false);
        var nextRect = next.GetComponent<RectTransform>();
        nextRect.SetParent(root.Find("Topbar"), false);
        nextRect.anchorMin = nextRect.anchorMax = new Vector2(1, 0.5f);
        nextRect.pivot = new Vector2(1, 0.5f);
        nextRect.anchoredPosition = new Vector2(-58, 0);
        nextRect.sizeDelta = new Vector2(48, 20);

        // Reuse the native button style for a mouse-accessible back control.
        var back = Object.Instantiate(next.gameObject, root.Find("Topbar"));
        back.name = "BackToPhone";
        var backRect = back.GetComponent<RectTransform>();
        backRect.anchorMin = backRect.anchorMax = new Vector2(1, 0.5f);
        backRect.pivot = new Vector2(1, 0.5f);
        backRect.anchoredPosition = new Vector2(-5, 0);
        backRect.sizeDelta = new Vector2(48, 20);
        var backButton = back.GetComponent<Button>();
        backButton.onClick = new Button.ButtonClickedEvent();
        backButton.onClick.AddListener((UnityEngine.Events.UnityAction)Close);
        Label(back.transform, "Text (TMP)").text = "Back";
        ConfigureButtonHitbox(next);
        ConfigureButtonHitbox(backButton);
        _nextButton = next;
        _backButton = backButton;

        // Make the smaller right-hand column fit exact hour/minute values.
        Label(root, "OperationList/TimeLabel").text = "Time Left";
        _placeholder.gameObject.SetActive(false);
        _computerPanel.SetActive(true);
        Mod.Instance.LoggerInstance.Msg("Reused the game's laundering computer UI on the phone.");
    }

    private static List<Business> Owned()
    {
        var result = new List<Business>();
        if (Business.OwnedBusinesses != null)
            foreach (var b in Business.OwnedBusinesses)
                if (b != null && b.IsOwned) result.Add(b);
        return result;
    }

    private void NextBusiness()
    {
        var businesses = Owned();
        if (businesses.Count == 0) return;
        var index = businesses.FindIndex(b => b.PropertyCode == _selectedCode);
        _selectedCode = businesses[(index + 1) % businesses.Count].PropertyCode;
        _markerSignature = "";
        Refresh();
    }

    private void Refresh()
    {
        var businesses = Owned();
        if (_computerPanel == null)
        {
            var candidates = Object.FindObjectsOfType<LaunderingInterface>(true);
            foreach (var candidate in candidates)
                if (candidate.container != null && candidate.entryPrefab != null)
                { BuildComputerPanel(candidate); break; }
        }
        if (_computerPanel == null) return;
        var selected = businesses.Find(b => b.PropertyCode == _selectedCode) ?? businesses.FirstOrDefault();
        _selectedCode = selected?.PropertyCode;
        _title!.text = selected?.PropertyName ?? "Businesses";
        _pending!.text = Money(selected?.currentLaunderTotal ?? 0);
        _capacity!.text = Money(selected?.LaunderCapacity ?? 0);

        var operations = new List<(Business Business, LaunderingOperation? Operation)>();
        foreach (var b in businesses)
        {
            foreach (var op in b.LaunderingOperations) operations.Add((b, op));
        }
        var signature = string.Join(";", operations.Select(x => x.Business.PropertyCode + ":" + (x.Operation?.Pointer.ToString() ?? "idle")));
        if (_rowSignature != signature)
        {
            ClearRows();
            _rows.Clear();
            foreach (var item in operations)
            {
                var obj = Object.Instantiate(_entryTemplate!, _content);
                obj.name = "LaunderingStatusRow";
                obj.SetActive(true);
                var rowRect = obj.GetComponent<RectTransform>();
                rowRect.sizeDelta = new Vector2(360, 19);
                var row = new Row(item.Business, item.Operation, Label(obj.transform, "BusinessLabel"), Label(obj.transform, "AmountLabel"), Label(obj.transform, "TimeLabel"));
                row.Time.fontSize = Math.Min(row.Time.fontSize, 9);
                _rows.Add(row);
            }
            _rowSignature = signature;
        }
        if (_noOperations != null) _noOperations.SetActive(operations.Count == 0);
        foreach (var row in _rows)
        {
            row.Name.text = row.Business.PropertyName;
            row.Amount.text = row.Operation == null ? "--" : Money(row.Operation.amount);
            row.Time.text = row.Operation == null ? "Idle" : Remaining(row.Operation);
        }
        RefreshMarkers(selected);
        _refreshCount++;
        if (Mod.Diagnostics && IsOpen) WriteSnapshot();
    }

    private void RefreshMarkers(Business? business)
    {
        var ops = business?.LaunderingOperations;
        var pointers = new List<string>();
        if (ops != null) foreach (var op in ops) pointers.Add(op.Pointer.ToString());
        var signature = _selectedCode + ":" + string.Join(",", pointers);
        if (_markerSignature != signature)
        {
            ClearChildren(_notches!);
            _markers.Clear();
            if (ops != null)
                foreach (var op in ops)
                {
                    var marker = Object.Instantiate(_markerTemplate!, _notches);
                    marker.SetActive(true);
                    foreach (var label in marker.GetComponentsInChildren<TMP_Text>(true)) label.text = Money(op.amount);
                    _markers.Add(marker);
                }
            _markerSignature = signature;
        }
        if (ops == null) return;
        for (var i = 0; i < Math.Min(ops.Count, _markers.Count); i++)
        {
            var op = ops[i];
            var progress = Mathf.Clamp01((float)op.minutesSinceStarted / Math.Max(1, op.completionTime_Minutes));
            var rect = _markers[i].GetComponent<RectTransform>();
            // Native notches stretch vertically across the bar (sizeDelta.y=0).
            // Collapsing both Y anchors to 0.5 hides the green line while its
            // amount label survives. Move only X; keep the prefab's vertical
            // anchors and offset so line and child label travel as one marker.
            rect.anchorMin = new Vector2(progress, rect.anchorMin.y);
            rect.anchorMax = new Vector2(progress, rect.anchorMax.y);
            rect.anchoredPosition = new Vector2(0, rect.anchoredPosition.y);
        }
    }

    internal void WriteSnapshot()
    {
        var snapshot = new
        {
            SelectedBusiness = _selectedCode,
            Open = IsOpen,
            RefreshCount = _refreshCount,
            NativeComputerPanel = _computerPanel != null,
            BackgroundOpaque = _backdrop.color.a >= 1,
            ButtonHitTests = ProbeButtons(),
            PhoneIcons = _home.appIconContainer.childCount,
            ModIcons = Enumerable.Range(0, _home.appIconContainer.childCount).Count(i => _home.appIconContainer.GetChild(i).name == "LaunderingStatusIcon"),
            Rows = _rows.Select(r => new { Business = r.Name.text, Amount = r.Amount.text, Time = r.Time.text,
                RawAmount = r.Operation?.amount, ElapsedMinutes = r.Operation?.minutesSinceStarted,
                DurationMinutes = r.Operation?.completionTime_Minutes }).ToArray()
        };
        File.WriteAllText(Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "LaunderingStatus-snapshot.json"),
            System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    internal void Release()
    {
        if (_released) return;
        _released = true;
        Close();
        GameInput.DeregisterExitListener(_exit);
        if (Phone.InstanceExists) Phone.Instance.closeApps -= _close;
        if (_icon != null) Object.Destroy(_icon);
        if (_panel != null) Object.Destroy(_panel);
    }

    private static TMP_Text Label(Transform root, string path) =>
        root.Find(path)?.GetComponent<TMP_Text>() ?? throw new InvalidOperationException("Native laundering UI is missing: " + path);
    private static void ConfigureButtonHitbox(Button button)
    {
        // The computer's original label is 80 units wide; the header button is
        // 48. Let only the visible root Image participate in UI raycasts.
        var image = button.GetComponent<Image>();
        foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = graphic.Pointer == image.Pointer;
        image.raycastPadding = Vector4.zero;
        button.targetGraphic = image;
        var label = button.GetComponentInChildren<TMP_Text>(true).GetComponent<RectTransform>();
        label.anchorMin = Vector2.zero;
        label.anchorMax = Vector2.one;
        label.pivot = new Vector2(0.5f, 0.5f);
        label.offsetMin = label.offsetMax = Vector2.zero;
    }
    private HitProbe[] ProbeButtons()
    {
        if (!IsOpen || _nextButton == null || _backButton == null) return Array.Empty<HitProbe>();
        var raycaster = AppsCanvas.Instance.GetComponent<GraphicRaycaster>();
        var probes = new List<HitProbe>();
        foreach (var button in new[] { _nextButton, _backButton })
        {
            var rect = button.GetComponent<RectTransform>();
            var bounds = rect.rect;
            var points = new[]
            {
                ("top-left", new Vector2(bounds.xMin + 0.25f, bounds.yMax - 0.25f)),
                ("top-right", new Vector2(bounds.xMax - 0.25f, bounds.yMax - 0.25f)),
                ("bottom-left", new Vector2(bounds.xMin + 0.25f, bounds.yMin + 0.25f)),
                ("bottom-right", new Vector2(bounds.xMax - 0.25f, bounds.yMin + 0.25f)),
            };
            foreach (var (name, point) in points)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(raycaster.eventCamera,
                    rect.TransformPoint(new Vector3(point.x, point.y, 0)));
                var pointer = new UnityEngine.EventSystems.PointerEventData(Phone.Instance.eventSystem) { position = screen };
                var hits = new Il2CppSystem.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                raycaster.Raycast(pointer, hits);
                var hit = hits.Count > 0 ? hits[0].gameObject.GetComponentInParent<Button>() : null;
                probes.Add(new HitProbe(button.name, name, hit?.name, hit != null && hit.Pointer == button.Pointer));
            }
        }
        return probes.ToArray();
    }
    private static string Money(float value) => "$" + value.ToString("N0", CultureInfo.InvariantCulture);
    private static string Remaining(LaunderingOperation op)
    {
        var minutes = Math.Max(0, op.completionTime_Minutes - op.minutesSinceStarted);
        return minutes == 0 ? "Finishing" : minutes < 60 ? minutes + "m" : $"{minutes / 60}h {minutes % 60:00}m";
    }
    private static void ClearChildren(Transform root)
    {
        for (var i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);
    }
    private void ClearRows()
    {
        for (var i = _content!.childCount - 1; i >= 0; i--)
        {
            var child = _content.GetChild(i).gameObject;
            if (child != _noOperations) Object.DestroyImmediate(child);
        }
    }
    private static void CopyRect(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition3D = source.anchoredPosition3D;
        target.sizeDelta = source.sizeDelta;
        target.localScale = source.localScale;
        target.localRotation = source.localRotation;
    }
}
