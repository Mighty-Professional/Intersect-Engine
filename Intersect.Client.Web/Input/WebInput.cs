using System.Numerics;
using Intersect.Client.Framework.GenericClasses;
using Intersect.Client.Framework.Gwen.Input;
using Intersect.Client.Framework.Input;
using Intersect.Client.General;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Input;

/// <summary>
/// Web input implementation using DOM keyboard/mouse events via JS interop.
/// </summary>
public class WebInput : GameInput
{
    private readonly IJSInProcessRuntime _js;
    private IControlSet _controlSet;
    private Vector2 _cachedMousePos;

    public WebInput(IJSInProcessRuntime js)
    {
        _js = js;
        _controlSet = new WebControlSet();
        Current = this;
    }

    public override IControlSet ControlSet
    {
        get => _controlSet;
        set => _controlSet = value;
    }

    public override bool MouseHitInterface => Interface.Interface.DoesMouseHitInterface();

    public override bool IsMouseInBounds => true;

    public override InputDeviceType CursorMovementDevice { get; set; } = InputDeviceType.Mouse;

    public override bool IsKeyDown(Keys key) => _js.Invoke<bool>("IntersectInput.isKeyDown", (int)key);

    public override bool WasKeyDown(Keys key) => _js.Invoke<bool>("IntersectInput.wasKeyDown", (int)key);

    public override bool IsMouseButtonDown(MouseButton mb) => _js.Invoke<bool>("IntersectInput.isMouseButtonDown", (int)mb);

    public override bool WasMouseButtonDown(MouseButton mb) => _js.Invoke<bool>("IntersectInput.wasMouseButtonDown", (int)mb);

    public override Vector2 GetMousePosition()
    {
        return _cachedMousePos;
    }

    public override Vector2 MousePosition => _cachedMousePos;

    public override void Update(TimeSpan elapsed)
    {
        // Cache mouse position once per frame (M7 fix)
        var x = _js.Invoke<float>("IntersectInput.getMouseX");
        var y = _js.Invoke<float>("IntersectInput.getMouseY");
        _cachedMousePos = new Vector2(x, y);

        // Process text input
        var textInput = _js.Invoke<string[]?>("IntersectInput.getTextInput");
        if (textInput != null)
        {
            foreach (var ch in textInput)
            {
                if (ch.Length > 0)
                {
                    Interface.Interface.GwenInput?.ProcessMessage(
                        new IntersectInput.InputEvent { Type = 4 /* TextEntered */, Character = ch[0] });
                }
            }
        }

        // Process scroll
        var scroll = _js.Invoke<ScrollDelta>("IntersectInput.getScrollDelta");
        if (Math.Abs(scroll.Y) > 0.01f)
        {
            Interface.Interface.GwenInput?.ProcessMessage(
                new IntersectInput.InputEvent
                {
                    Type = 5 /* MouseWheelScrolled */,
                    Delta = scroll.Y > 0 ? -1 : 1
                });
        }
    }

    public override void OpenKeyboard(KeyboardType type, string text, bool autoCorrection, bool multiLine, bool secure) { }

    public override void OpenKeyboard(KeyboardType keyboardType, Action<string?> inputHandler, string description,
        string text, bool multiline = false, uint maxLength = 1024, Rectangle? inputBounds = default) { }

    private record ScrollDelta(float X, float Y);
}

/// <summary>
/// Minimal control set for web.
/// </summary>
internal class WebControlSet : IControlSet
{
    private readonly Dictionary<Control, ControlMapping> _mappings = new();

    public ControlMapping? this[Control control]
    {
        get => _mappings.GetValueOrDefault(control);
        set { if (value != null) _mappings[control] = value; }
    }

    public IReadOnlyDictionary<Control, ControlMapping> Mappings => _mappings;

    public void ReloadFromOptions(Options options) { }
    public void ResetDefaults() { }
    public bool TryAdd(Control control, params ControlBinding[] bindings) => true;
    public bool TryAdd(Control control, ControlMapping mapping) { _mappings[control] = mapping; return true; }
    public bool TryGetMappingFor(Control control, out ControlMapping? mapping) => _mappings.TryGetValue(control, out mapping);
    public bool TryLoad() => true;
    public bool TryReload() => true;
    public bool TrySave() => true;
}
