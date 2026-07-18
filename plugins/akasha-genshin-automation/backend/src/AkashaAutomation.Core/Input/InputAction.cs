namespace AkashaAutomation.Core.Input;

public enum InputActionKind
{
    KeyDown,
    KeyUp,
    KeyPress,
    MouseMove,
    MouseMoveClient,
    MouseLeftClick,
}

public sealed record InputAction(
    InputActionKind Kind,
    ushort VirtualKey = 0,
    int X = 0,
    int Y = 0,
    int ReferenceWidth = 0,
    int ReferenceHeight = 0)
{
    public static InputAction KeyPress(ushort virtualKey) =>
        new(InputActionKind.KeyPress, virtualKey);

    public static InputAction KeyDown(ushort virtualKey) =>
        new(InputActionKind.KeyDown, virtualKey);

    public static InputAction KeyUp(ushort virtualKey) =>
        new(InputActionKind.KeyUp, virtualKey);

    public static InputAction MouseMove(int x, int y) =>
        new(InputActionKind.MouseMove, X: x, Y: y);

    public static InputAction MouseMoveClient(int x, int y, int referenceWidth = 0, int referenceHeight = 0) =>
        new(
            InputActionKind.MouseMoveClient,
            X: x,
            Y: y,
            ReferenceWidth: referenceWidth,
            ReferenceHeight: referenceHeight);

    public static InputAction MouseLeftClick() => new(InputActionKind.MouseLeftClick);
}
