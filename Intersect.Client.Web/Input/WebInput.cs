using System.Numerics;
using Intersect.Client.Core.Controls;
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

    public WebInput(IJSInProcessRuntime js) : base(forceGlobal: true)
    {
        _js = js;
        _controlSet = new Controls();
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

    public new Vector2 MousePosition => _cachedMousePos;

    private Vector2 _prevMousePos;
    private readonly bool[] _prevMouseButtons = new bool[5];

    public override void Update(TimeSpan elapsed)
    {
        // Cache mouse position once per frame
        var x = _js.Invoke<float>("IntersectInput.getMouseX");
        var y = _js.Invoke<float>("IntersectInput.getMouseY");
        _cachedMousePos = new Vector2(x, y);

        // Send mouse move events to GWEN
        if (_cachedMousePos != _prevMousePos)
        {
            Interface.Interface.GwenInput?.ProcessMessage(
                new GwenInputMessage(
                    IntersectInput.InputEvent.MouseMove,
                    _cachedMousePos,
                    MouseButton.None,
                    Keys.None
                ));
            _prevMousePos = _cachedMousePos;
        }

        // Process queued mouse events (prevents losing clicks between frames)
        var mouseEvents = _js.Invoke<MouseEvent[]?>("IntersectInput.getMouseEvents");
        if (mouseEvents != null)
        {
            foreach (var evt in mouseEvents)
            {
                var btn = evt.Button switch
                {
                    0 => MouseButton.Left,
                    1 => MouseButton.Right,
                    2 => MouseButton.Middle,
                    _ => MouseButton.None
                };
                if (btn == MouseButton.None) continue;

                var pos = new Vector2(evt.X, evt.Y);
                var inputEvent = evt.Type == "down"
                    ? IntersectInput.InputEvent.MouseDown
                    : IntersectInput.InputEvent.MouseUp;

                Interface.Interface.GwenInput?.ProcessMessage(
                    new GwenInputMessage(inputEvent, pos, btn, Keys.None));
            }
        }

        // Process key events (for non-character keys like Backspace, Tab, Enter, etc.)
        var keyEvents = _js.Invoke<KeyEvent[]?>("IntersectInput.getKeyEvents");
        if (keyEvents != null)
        {
            foreach (var evt in keyEvents)
            {
                var key = (Keys)evt.Key;
                var inputEvent = evt.Type == "down"
                    ? IntersectInput.InputEvent.KeyDown
                    : IntersectInput.InputEvent.KeyUp;

                Interface.Interface.GwenInput?.ProcessMessage(
                    new GwenInputMessage(inputEvent, _cachedMousePos, MouseButton.None, key));

                // Fire game-level key handler for key-down events (Enter→chat, Escape→menu, hotkeys, etc.)
                // This mirrors what MonoInput does for the desktop client.
                if (evt.Type == "down")
                {
                    var modifier = Keys.None;
                    if (IsKeyDown(Keys.ControlKey) || IsKeyDown(Keys.LControlKey) || IsKeyDown(Keys.RControlKey))
                        modifier = Keys.Control;
                    else if (IsKeyDown(Keys.ShiftKey) || IsKeyDown(Keys.LShiftKey) || IsKeyDown(Keys.RShiftKey))
                        modifier = Keys.Shift;
                    else if (IsKeyDown(Keys.Menu) || IsKeyDown(Keys.LMenu) || IsKeyDown(Keys.RMenu))
                        modifier = Keys.Alt;

                    Core.Input.OnKeyPressed(modifier, key);
                }
            }
        }

        // Process text input
        var textInput = _js.Invoke<string[]?>("IntersectInput.getTextInput");
        if (textInput != null)
        {
            foreach (var ch in textInput)
            {
                if (ch.Length > 0)
                {
                    Interface.Interface.GwenInput?.ProcessMessage(
                        new GwenInputMessage(
                            IntersectInput.InputEvent.TextEntered,
                            _cachedMousePos,
                            MouseButton.None,
                            Keys.None,
                            unicode: ch
                        ));
                }
            }
        }

        // Process scroll
        var scroll = _js.Invoke<ScrollDelta>("IntersectInput.getScrollDelta");
        if (Math.Abs(scroll.Y) > 0.01f)
        {
            var scrollAmount = scroll.Y > 0 ? -1 : 1;
            Interface.Interface.GwenInput?.ProcessMessage(
                new GwenInputMessage(
                    IntersectInput.InputEvent.MouseScroll,
                    new Vector2(scrollAmount, scrollAmount),
                    MouseButton.None,
                    Keys.None
                ));
        }
    }

    public override void OpenKeyboard(KeyboardType type, string text, bool autoCorrection, bool multiLine, bool secure) { }

    public override void OpenKeyboard(KeyboardType keyboardType, Action<string?> inputHandler, string description,
        string text, bool multiline = false, uint maxLength = 1024, Rectangle? inputBounds = default) { }

    private record ScrollDelta(float X, float Y);
    private record MouseEvent(string Type, int Button, float X, float Y);
    private record KeyEvent(string Type, int Key);
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
