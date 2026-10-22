// =====================================================================================
//  Gamepad.cs
//  Legge il controller collegato, per il controller disegnato della pagina Controllers (i tasti si
//  illuminano quando li premi). Solo lettura, mai scritture.
//    Windows  prima XInput (i controller Xbox e quelli che si fingono Xbox, come gli 8BitDo), poi i
//             joystick di Windows ("winmm": PlayStation, Nintendo, gli altri controller USB/Bluetooth);
//    Linux    /dev/input/js0, il "joystick" del kernel (con nome e produttore da /sys/class/input);
//    macOS    non ancora.
//  I tasti si leggono "per posizione" (in basso, a destra, a sinistra, in alto) perché ogni marca
//  li chiama a modo suo (A/B, croce/cerchio, B/A...).
// =====================================================================================

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SteamAll.Core.Controllers;
using SteamAll.Core.Platform;

namespace SteamAll.Services;

/// <summary>Lo stato del controller in un istante. Levette da -1 a 1 (Y verso il basso), grilletti da 0 a 1.</summary>
public sealed record GamepadState(bool Connected, string Name, ControllerFamily Family,
    bool South, bool East, bool West, bool North, bool LeftBumper, bool RightBumper, bool Back, bool Start, bool Guide,
    bool LeftStick, bool RightStick, bool Up, bool Down, bool Left, bool Right,
    double LeftX, double LeftY, double RightX, double RightY, double LeftTrigger, double RightTrigger)
{
    public static GamepadState None { get; } = new(false, "", ControllerFamily.Generic,
        false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, 0, 0, 0, 0, 0, 0);
}

public interface IGamepadReader : IDisposable
{
    /// <summary>Lo stato adesso (GamepadState.None se non c'è nessun controller).</summary>
    GamepadState Read();
}

public static class Gamepads
{
    public static IGamepadReader Create(OsKind os) =>
        os == OsKind.Windows && OperatingSystem.IsWindows() ? new WindowsGamepadReader()
        : os == OsKind.Linux ? new LinuxGamepadReader()
        : new NoGamepadReader();
}

internal sealed class NoGamepadReader : IGamepadReader
{
    public GamepadState Read() => GamepadState.None;
    public void Dispose() { }
}

// ---------------------------------------------------------------------------------
//  Windows
// ---------------------------------------------------------------------------------

[SupportedOSPlatform("windows")]
internal sealed class WindowsGamepadReader : IGamepadReader
{
    private int joystick = -1;          // il joystick di winmm trovato (-1 = da cercare)
    private string joystickName = "";
    private ControllerFamily joystickFamily = ControllerFamily.Generic;
    private DateTime nextScan = DateTime.MinValue;

    public GamepadState Read()
    {
        // 1. XInput: i quattro "posti" dei controller Xbox.
        for (int i = 0; i < 4; i++)
        {
            if (XInputGetState(i, out XInputState s) != 0) continue;   // 0 = c'è
            XInputGamepad g = s.Gamepad;
            bool B(int bit) => (g.Buttons & bit) != 0;
            return new GamepadState(true, "Xbox controller", ControllerFamily.Xbox,
                B(0x1000), B(0x2000), B(0x4000), B(0x8000), B(0x100), B(0x200), B(0x20), B(0x10), false,
                B(0x40), B(0x80), B(0x1), B(0x2), B(0x4), B(0x8),
                g.LeftX / 32768.0, -g.LeftY / 32768.0, g.RightX / 32768.0, -g.RightY / 32768.0, g.LeftTrigger / 255.0, g.RightTrigger / 255.0);
        }

        // 2. I joystick di Windows. Cercarli è lento: lo facciamo al massimo ogni 2 secondi.
        if (joystick < 0 && DateTime.UtcNow >= nextScan) Scan();
        if (joystick < 0) return GamepadState.None;
        var info = new JoyInfoEx { Size = Marshal.SizeOf<JoyInfoEx>(), Flags = 0xFF };   // JOY_RETURNALL
        if (joyGetPosEx(joystick, ref info) != 0)
        {
            joystick = -1;   // scollegato
            return GamepadState.None;
        }
        bool Bt(int n) => (info.Buttons & (1u << n)) != 0;
        static double Axis(uint v) => (v - 32767.5) / 32767.5;
        // La croce direzionale arriva come "POV": l'angolo in centesimi di grado (65535 = al centro).
        uint pov = info.Pov;
        bool up = pov != 0xFFFF && (pov >= 31500 || pov <= 4500), right = pov != 0xFFFF && pov >= 4500 && pov <= 13500;
        bool down = pov != 0xFFFF && pov >= 13500 && pov <= 22500, left = pov != 0xFFFF && pov >= 22500 && pov <= 31500;
        // L'ordine dei tasti dipende dalla marca: [in basso, a destra, a sinistra, in alto].
        int[] face = joystickFamily switch
        {
            ControllerFamily.PlayStation => [1, 2, 0, 3],   // croce, cerchio, quadrato, triangolo
            ControllerFamily.Nintendo => [0, 1, 2, 3],      // B, A, Y, X
            _ => [0, 1, 2, 3],
        };
        bool sony = joystickFamily == ControllerFamily.PlayStation, nintendo = joystickFamily == ControllerFamily.Nintendo;
        int back = sony || nintendo ? 8 : 6, start = sony || nintendo ? 9 : 7, ls = sony || nintendo ? 10 : 8, rs = sony || nintendo ? 11 : 9;
        return new GamepadState(true, joystickName, joystickFamily,
            Bt(face[0]), Bt(face[1]), Bt(face[2]), Bt(face[3]), Bt(4), Bt(5), Bt(back), Bt(start), Bt(12),
            Bt(ls), Bt(rs), up, down, left, right,
            Axis(info.X), Axis(info.Y), Axis(sony ? info.Z : info.U), Axis(info.R),
            sony || nintendo ? (Bt(6) ? 1 : 0) : Math.Max(0, Axis(info.Z)), sony || nintendo ? (Bt(7) ? 1 : 0) : Math.Max(0, -Axis(info.Z)));
    }

    private void Scan()
    {
        nextScan = DateTime.UtcNow.AddSeconds(2);
        int count = joyGetNumDevs();
        for (int id = 0; id < Math.Min(count, 16); id++)
        {
            var info = new JoyInfoEx { Size = Marshal.SizeOf<JoyInfoEx>(), Flags = 0xFF };
            if (joyGetPosEx(id, ref info) != 0) continue;
            var caps = new JoyCaps();
            if (joyGetDevCapsW((IntPtr)id, ref caps, Marshal.SizeOf<JoyCaps>()) != 0) continue;
            joystick = id;
            joystickFamily = SteamInputSettings.FamilyOf(caps.Mid);
            joystickName = caps.Name.Trim().Length > 0 ? caps.Name.Trim() : "Controller";
            return;
        }
    }

    public void Dispose() { }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad { public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LeftX, LeftY, RightX, RightY; }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState { public uint Packet; public XInputGamepad Gamepad; }

    [StructLayout(LayoutKind.Sequential)]
    private struct JoyInfoEx
    {
        public int Size, Flags;
        public uint X, Y, Z, R, U, V, Buttons, ButtonNumber, Pov, Reserved1, Reserved2;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct JoyCaps
    {
        public ushort Mid, Pid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint XMin, XMax, YMin, YMax, ZMin, ZMax, NumButtons, PeriodMin, PeriodMax, RMin, RMax, UMin, UMax, VMin, VMax, Caps, MaxAxes, NumAxes, MaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string OemVxd;
    }

    [DllImport("xinput1_4.dll")] private static extern int XInputGetState(int index, out XInputState state);
    [DllImport("winmm.dll")] private static extern int joyGetNumDevs();
    [DllImport("winmm.dll")] private static extern int joyGetPosEx(int id, ref JoyInfoEx info);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int joyGetDevCapsW(IntPtr id, ref JoyCaps caps, int size);
}

// ---------------------------------------------------------------------------------
//  Linux: /dev/input/js0 manda un "evento" di 8 byte per ogni cambiamento:
//  tempo (4), valore (2, con segno), tipo (1: 1 = tasto, 2 = asse; +0x80 = stato iniziale), numero (1).
//  Lo leggiamo in un thread a parte e teniamo l'ultimo valore di ogni tasto e asse.
// ---------------------------------------------------------------------------------

internal sealed class LinuxGamepadReader : IGamepadReader
{
    private readonly bool[] buttons = new bool[32];
    private readonly short[] axes = new short[16];
    private readonly object sync = new();
    private volatile bool connected, stop;
    private string name = "Controller";
    private ControllerFamily family = ControllerFamily.Generic;

    public LinuxGamepadReader() => new Thread(Loop) { IsBackground = true, Name = "gamepad" }.Start();

    private void Loop()
    {
        var e = new byte[8];
        while (!stop)
        {
            try
            {
                using var js = new FileStream("/dev/input/js0", FileMode.Open, FileAccess.Read);
                name = ReadSys("name") ?? "Controller";
                family = int.TryParse(ReadSys("id/vendor"), System.Globalization.NumberStyles.HexNumber, null, out int vendor)
                    ? SteamInputSettings.FamilyOf(vendor) : ControllerFamily.Generic;
                connected = true;
                while (!stop && js.Read(e, 0, 8) == 8)
                {
                    short value = BitConverter.ToInt16(e, 4);
                    int type = e[6] & 0x7F, number = e[7];
                    lock (sync)
                    {
                        if (type == 1 && number < buttons.Length) buttons[number] = value != 0;
                        else if (type == 2 && number < axes.Length) axes[number] = value;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            connected = false;
            Thread.Sleep(2000);   // nessun controller (o scollegato): riproviamo tra poco
        }
    }

    private static string? ReadSys(string file)
    {
        string path = Path.Combine("/sys/class/input/js0/device", file);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    public GamepadState Read()
    {
        if (!connected) return GamepadState.None;
        lock (sync)
        {
            // L'ordine del driver "xpad" (Xbox e compatibili): A B X Y LB RB Back Start Guide LS RS;
            // assi: levetta sinistra 0-1, grilletto sinistro 2, levetta destra 3-4, grilletto destro 5, croce 6-7.
            double A(int i) => axes[i] / 32767.0;
            return new GamepadState(true, name, family,
                buttons[0], buttons[1], buttons[2], buttons[3], buttons[4], buttons[5], buttons[6], buttons[7], buttons[8],
                buttons[9], buttons[10], axes[7] < 0, axes[7] > 0, axes[6] < 0, axes[6] > 0,
                A(0), A(1), A(3), A(4), (A(2) + 1) / 2, (A(5) + 1) / 2);
        }
    }

    public void Dispose() => stop = true;
}
