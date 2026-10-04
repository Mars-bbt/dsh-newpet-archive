// Windows integration test: run with DeepSeek Harness open. Restore its state afterwards.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

public static class FocusDshRegression
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Placement
    {
        public int Length, Flags, ShowCmd;
        public Point MinPosition, MaxPosition;
        public Rect NormalPosition;
    }
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h, ref Placement p);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr h, ref Placement p);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint message, IntPtr wParam, IntPtr lParam);

    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    static bool WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 60; i++)
        {
            if (condition()) return true;
            Thread.Sleep(100);
        }
        return condition();
    }

    [STAThread]
    public static int Main(string[] args)
    {
        IntPtr window = IntPtr.Zero;
        IntPtr previousForeground = GetForegroundWindow();
        var placement = new Placement();
        placement.Length = Marshal.SizeOf(typeof(Placement));
        bool captured = false, previouslyVisible = false;
        Action restoreFocus = null;
        try
        {
            var assembly = Assembly.LoadFrom(args[0]);
            var type = assembly.GetType("WhaleOverlay.MainWindow", true);
            var find = type.GetMethod("FindDshWindow", BindingFlags.NonPublic | BindingFlags.Static);
            var focus = type.GetMethod("FocusDsh", BindingFlags.NonPublic | BindingFlags.Static);
            restoreFocus = () => focus.Invoke(null, null);
            var pids = new List<uint>();
            foreach (Process process in Process.GetProcessesByName("DeepSeek Harness"))
                using (process) pids.Add((uint)process.Id);
            Check(pids.Count > 0, "Start DeepSeek Harness before running this test.");
            window = (IntPtr)find.Invoke(null, new object[] { pids });
            Check(window != IntPtr.Zero, "No desktop main window found.");
            Check(GetWindowPlacement(window, ref placement), "Cannot capture window placement.");
            captured = true;
            previouslyVisible = IsWindowVisible(window);
            Check((IntPtr)find.Invoke(null, new object[] { new List<uint>() }) == IntPtr.Zero,
                "Unrelated processes must not match.");

            if (args.Length > 1 && args[1] == "--close-cycle")
            {
                for (int i = 0; i < 3; i++)
                {
                    focus.Invoke(null, null);
                    Check(WaitFor(() => IsWindowVisible(window)), "Desktop window did not open.");
                    Thread.Sleep(2000); // Allow the second-instance notification to complete before closing.
                    // Title-bar X sends SC_CLOSE. The desktop shell must handle it and hide itself.
                    Check(PostMessage(window, 0x0112, new IntPtr(0xF060), IntPtr.Zero), "Cannot request title-bar close.");
                    Check(WaitFor(() => !IsWindowVisible(window)), "Title-bar close did not hide the desktop window.");
                    Console.WriteLine("PASS: open -> title-bar close -> background, cycle " + (i + 1));
                }
                return 0;
            }

            ShowWindow(window, 0); // SW_HIDE: close-to-background case
            Thread.Sleep(200);
            Check(!IsWindowVisible(window), "Window did not hide.");
            Check((IntPtr)find.Invoke(null, new object[] { pids }) == window,
                "Hidden main window was excluded.");
            focus.Invoke(null, null);
            Check(WaitFor(() => IsWindowVisible(window) && !IsIconic(window)), "Hidden window did not restore.");
            Check(WaitFor(() => GetForegroundWindow() == window), "Hidden window did not gain foreground.");
            Thread.Sleep(2000);
            Console.WriteLine("PASS: hidden background window is found, shown and focused.");

            ShowWindow(window, 6); // SW_MINIMIZE
            Thread.Sleep(200);
            Check(IsIconic(window), "Window did not minimize.");
            focus.Invoke(null, null);
            Check(WaitFor(() => IsWindowVisible(window) && !IsIconic(window)), "Minimized window did not restore.");
            Check(WaitFor(() => GetForegroundWindow() == window), "Restored window did not gain foreground.");
            Thread.Sleep(2000);
            Console.WriteLine("PASS: minimized desktop window is restored and focused.");

            ShowWindow(window, 3); // SW_SHOWMAXIMIZED
            Thread.Sleep(200);
            ShowWindow(window, 0);
            focus.Invoke(null, null);
            Check(WaitFor(() => IsWindowVisible(window) && IsZoomed(window)), "Maximized state was lost.");
            Console.WriteLine("PASS: hidden maximized window preserves its maximized state.");
            Console.WriteLine("PASS: unrelated processes do not match.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            if (captured)
            {
                SetWindowPlacement(window, ref placement);
                // Restore visibility through the shell, keeping Electron's own state in sync.
                restoreFocus();
                WaitFor(() => IsWindowVisible(window));
                Thread.Sleep(2000);
                if (!previouslyVisible)
                {
                    PostMessage(window, 0x0112, new IntPtr(0xF060), IntPtr.Zero);
                    WaitFor(() => !IsWindowVisible(window));
                }
            }
            if (previousForeground != IntPtr.Zero) SetForegroundWindow(previousForeground);
        }
    }
}
