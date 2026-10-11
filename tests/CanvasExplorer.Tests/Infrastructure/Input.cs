using System.Runtime.InteropServices;
using System.Windows;

namespace CanvasExplorer.Tests.Infrastructure;

/// <summary>Real mouse and keyboard input (SendInput). Keys go by virtual-key code, text as Unicode,
/// so the active keyboard layout (e.g. Russian) does not matter.</summary>
static class Input
{
    public const ushort Back = 0x08, Tab = 0x09, Enter = 0x0D, Shift = 0x10, Ctrl = 0x11, Alt = 0x12, Esc = 0x1B,
        Space = 0x20, End = 0x23, Home = 0x24, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, Delete = 0x2E,
        F2 = 0x71, F4 = 0x73, F5 = 0x74, Apps = 0x5D;

    public static ushort Letter(char c) => char.ToUpperInvariant(c);

    /// <summary>Presses a chord: modifiers first, released in reverse order.</summary>
    public static void Keys(params ushort[] chord)
    {
        foreach (var k in chord) Key(k, up: false);
        foreach (var k in chord.Reverse()) Key(k, up: true);
        Thread.Sleep(120);
    }

    public static void Type(string text)
    {
        foreach (var ch in text)
        {
            Send(KeyInput(0, ch, KEYEVENTF_UNICODE), KeyInput(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            Thread.Sleep(8);
        }
        Thread.Sleep(80);
    }

    public static void MoveTo(Point p)
    {
        SetCursorPos((int)Math.Round(p.X), (int)Math.Round(p.Y));
        Thread.Sleep(30);
    }

    public enum Button { Left, Right, Middle }

    public static void Click(Point p, Button button = Button.Left, int count = 1)
    {
        // Arrive from a few pixels away so WPF sees a real move before the press.
        MoveTo(new Point(p.X - 3, p.Y - 3));
        MoveTo(p);
        for (var i = 0; i < count; i++)
        {
            Mouse(button, up: false);
            Thread.Sleep(30);
            Mouse(button, up: true);
            Thread.Sleep(count > 1 ? 40 : 30);
        }
        Thread.Sleep(150);
    }

    /// <summary>Press, move in small steps (pausing at each waypoint for <paramref name="hold"/> ms), release.</summary>
    public static void Drag(Button button, int hold, params Point[] points)
    {
        MoveTo(points[0]);
        Mouse(button, up: false);
        Thread.Sleep(80);
        var from = points[0];
        foreach (var to in points.Skip(1))
        {
            for (var i = 1; i <= 16; i++)
            {
                MoveTo(new Point(from.X + (to.X - from.X) * i / 16, from.Y + (to.Y - from.Y) * i / 16));
                Thread.Sleep(10);
            }
            from = to;
            var end = DateTime.UtcNow.AddMilliseconds(hold);
            // Keep nudging while waiting: drag-and-drop only updates on movement.
            while (DateTime.UtcNow < end)
            {
                MoveTo(new Point(to.X + 1, to.Y));
                MoveTo(to);
                Thread.Sleep(30);
            }
        }
        Mouse(button, up: true);
        Thread.Sleep(250);
    }

    public static void Wheel(Point p, int notches)
    {
        MoveTo(p);
        for (var i = 0; i < Math.Abs(notches); i++)
        {
            Send(MouseInput(MOUSEEVENTF_WHEEL, notches > 0 ? 120 : -120));
            Thread.Sleep(30);
        }
        Thread.Sleep(150);
    }

    /// <summary>Holds a modifier key while running <paramref name="action"/>.</summary>
    public static void With(ushort modifier, Action action)
    {
        Key(modifier, up: false);
        Thread.Sleep(40);
        try { action(); }
        finally { Key(modifier, up: true); Thread.Sleep(60); }
    }

    static void Key(ushort vk, bool up)
    {
        var flags = up ? KEYEVENTF_KEYUP : 0u;
        if (vk is Left or Right or Up or Down or Home or End or Delete or Apps) flags |= KEYEVENTF_EXTENDEDKEY;
        Send(KeyInput(vk, 0, flags));
        Thread.Sleep(20);
    }

    static void Mouse(Button b, bool up)
    {
        uint flag = b switch
        {
            Button.Left => up ? 0x0004u : 0x0002u,
            Button.Right => up ? 0x0010u : 0x0008u,
            _ => up ? 0x0040u : 0x0020u,
        };
        Send(MouseInput(flag, 0));
    }

    // ---- Win32 ----

    const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4, MOUSEEVENTF_WHEEL = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public int mouseData; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    static INPUT KeyInput(ushort vk, ushort scan, uint flags) =>
        new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } } };

    static INPUT MouseInput(uint flags, int data) =>
        new() { type = 0, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = data } } };

    static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);
}
