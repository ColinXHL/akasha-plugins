using System.ComponentModel;
using System.Runtime.InteropServices;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;

namespace AkashaAutomation.Core.Input;

public sealed class WindowsSendInputService : IInputService
{
    private readonly object _gate = new();
    private readonly HashSet<ushort> _pressedKeys = [];
    private bool _disposed;

    public ValueTask ExecuteAsync(
        InputActionGroup actions,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.IsGameForeground || context.Window?.Handle != NativeMethods.GetForegroundWindow())
        {
            throw new InvalidOperationException("SendInput is allowed only while the located game window is foreground.");
        }

        lock (_gate)
        {
            foreach (var action in actions.Actions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Execute(action, context);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            foreach (var key in _pressedKeys.ToArray())
            {
                SendKeyboard(key, keyUp: true);
            }

            _pressedKeys.Clear();
            SendMouse(NativeMethods.MouseEventLeftUp);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await ReleaseAllAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private void Execute(InputAction action, GameContextSnapshot context)
    {
        switch (action.Kind)
        {
            case InputActionKind.KeyDown:
                SendKeyboard(action.VirtualKey, keyUp: false);
                _pressedKeys.Add(action.VirtualKey);
                break;
            case InputActionKind.KeyUp:
                SendKeyboard(action.VirtualKey, keyUp: true);
                _pressedKeys.Remove(action.VirtualKey);
                break;
            case InputActionKind.KeyPress:
                SendKeyboardPress(action.VirtualKey);
                break;
            case InputActionKind.MouseMove:
                SendMouseMove(action.X, action.Y);
                break;
            case InputActionKind.MouseMoveClient:
                if (context.Window is null)
                {
                    throw new InvalidOperationException("A game window is required for client-coordinate mouse input.");
                }

                var clientPoint = MapToClient(action, context.Window.ClientSize);
                var point = new NativePoint(clientPoint.X, clientPoint.Y);
                if (!NativeMethods.ClientToScreen(context.Window.Handle, ref point))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to map game client coordinates to the screen.");
                }

                SendMouseMove(point.X, point.Y);
                break;
            case InputActionKind.MouseLeftClick:
                SendMouse(NativeMethods.MouseEventLeftDown);
                SendMouse(NativeMethods.MouseEventLeftUp);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "Unknown input action.");
        }
    }

    private static void SendKeyboard(ushort virtualKey, bool keyUp)
    {
        var input = NativeInput.Keyboard(DescribeKeyboardInput(virtualKey, keyUp));
        Send([input]);
    }

    private static void SendKeyboardPress(ushort virtualKey)
    {
        var descriptors = DescribeKeyboardPress(virtualKey);
        Send(descriptors.Select(NativeInput.Keyboard).ToArray());
    }

    internal static WindowsKeyboardInputDescriptor DescribeKeyboardInput(ushort virtualKey, bool keyUp)
    {
        if (virtualKey == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualKey));
        }

        var scanCode = checked((ushort)(NativeMethods.MapVirtualKey(virtualKey, NativeMethods.MapVirtualKeyVirtualKeyToScanCode) & 0xFFFF));
        if (scanCode == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to map virtual key 0x{virtualKey:X2} to a scan code.");
        }

        return new WindowsKeyboardInputDescriptor(
            virtualKey,
            scanCode,
            IsExtendedKey(virtualKey),
            keyUp);
    }

    internal static WindowsKeyboardInputDescriptor[] DescribeKeyboardPress(ushort virtualKey) =>
    [
        DescribeKeyboardInput(virtualKey, keyUp: false),
        DescribeKeyboardInput(virtualKey, keyUp: true),
    ];

    private static bool IsExtendedKey(ushort virtualKey) => virtualKey is
        0x03 or 0x12 or 0xA4 or 0xA5 or 0x11 or 0xA3 or
        0x2D or 0x2E or 0x24 or 0x23 or 0x21 or 0x22 or
        0x27 or 0x26 or 0x25 or 0x28 or 0x90 or 0x2C or 0x6F;

    private static void SendMouse(uint flags)
    {
        var input = NativeInput.Mouse(0, 0, flags);
        Send([input]);
    }

    private static void SendMouseMove(int x, int y)
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SystemMetricVirtualScreenX);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SystemMetricVirtualScreenY);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SystemMetricVirtualScreenWidth);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SystemMetricVirtualScreenHeight);
        if (width <= 1 || height <= 1)
        {
            throw new InvalidOperationException("The virtual desktop dimensions are unavailable.");
        }

        var descriptor = DescribeVirtualDesktopMouseMove(x, y, left, top, width, height);
        var input = NativeInput.Mouse(
            descriptor.AbsoluteX,
            descriptor.AbsoluteY,
            NativeMethods.MouseEventMove | NativeMethods.MouseEventAbsolute | NativeMethods.MouseEventVirtualDesk);
        Send([input]);
    }

    internal static WindowsClientPoint MapToClient(InputAction action, CaptureSize clientSize)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Kind != InputActionKind.MouseMoveClient)
        {
            throw new ArgumentException("Only client-coordinate mouse actions can be mapped.", nameof(action));
        }

        if (action.ReferenceWidth <= 0 || action.ReferenceHeight <= 0)
        {
            return new WindowsClientPoint(action.X, action.Y);
        }

        var x = (int)Math.Round(action.X * (double)clientSize.Width / action.ReferenceWidth);
        var y = (int)Math.Round(action.Y * (double)clientSize.Height / action.ReferenceHeight);
        return new WindowsClientPoint(
            Math.Clamp(x, 0, clientSize.Width - 1),
            Math.Clamp(y, 0, clientSize.Height - 1));
    }

    internal static WindowsMouseMoveDescriptor DescribeVirtualDesktopMouseMove(
        int x,
        int y,
        int virtualLeft,
        int virtualTop,
        int virtualWidth,
        int virtualHeight)
    {
        if (virtualWidth <= 1 || virtualHeight <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualWidth), "Virtual desktop dimensions must exceed one pixel.");
        }

        var absoluteX = (int)Math.Clamp(Math.Round((x - virtualLeft) * 65535d / (virtualWidth - 1)), 0, 65535);
        var absoluteY = (int)Math.Clamp(Math.Round((y - virtualTop) * 65535d / (virtualHeight - 1)), 0, 65535);
        return new WindowsMouseMoveDescriptor(absoluteX, absoluteY);
    }

    private static void Send(NativeInput[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput did not submit every requested input.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;

        internal NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        internal uint Type;
        internal NativeInputUnion Data;

        internal static NativeInput Keyboard(WindowsKeyboardInputDescriptor descriptor) =>
            new()
            {
                Type = NativeMethods.InputKeyboard,
                Data = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput
                    {
                        VirtualKey = descriptor.VirtualKey,
                        ScanCode = descriptor.ScanCode,
                        Flags = (descriptor.IsExtended ? NativeMethods.KeyEventExtendedKey : 0) |
                                (descriptor.IsKeyUp ? NativeMethods.KeyEventKeyUp : 0),
                    },
                },
            };

        internal static NativeInput Mouse(int x, int y, uint flags) =>
            new()
            {
                Type = NativeMethods.InputMouse,
                Data = new NativeInputUnion
                {
                    Mouse = new NativeMouseInput { X = x, Y = y, Flags = flags },
                },
            };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)] internal NativeMouseInput Mouse;
        [FieldOffset(0)] internal NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    private static class NativeMethods
    {
        internal const uint InputMouse = 0;
        internal const uint InputKeyboard = 1;
        internal const uint KeyEventExtendedKey = 0x0001;
        internal const uint KeyEventKeyUp = 0x0002;
        internal const uint MapVirtualKeyVirtualKeyToScanCode = 0;
        internal const uint MouseEventMove = 0x0001;
        internal const uint MouseEventLeftDown = 0x0002;
        internal const uint MouseEventLeftUp = 0x0004;
        internal const uint MouseEventAbsolute = 0x8000;
        internal const uint MouseEventVirtualDesk = 0x4000;
        internal const int SystemMetricVirtualScreenX = 76;
        internal const int SystemMetricVirtualScreenY = 77;
        internal const int SystemMetricVirtualScreenWidth = 78;
        internal const int SystemMetricVirtualScreenHeight = 79;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll")]
        internal static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClientToScreen(nint windowHandle, ref NativePoint point);
    }
}

internal readonly record struct WindowsKeyboardInputDescriptor(
    ushort VirtualKey,
    ushort ScanCode,
    bool IsExtended,
    bool IsKeyUp);

internal readonly record struct WindowsClientPoint(int X, int Y);

internal readonly record struct WindowsMouseMoveDescriptor(int AbsoluteX, int AbsoluteY);
