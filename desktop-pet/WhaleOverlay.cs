// WhaleOverlay — 鲸鱼娘悬浮桌宠(纯 WPF 透明置顶窗口)
// 立绘用 assets\*.png(带 alpha,由 sharp 从原 webp 转出;WIC 的 webp 解码会丢 alpha)
// 动效移植自网页版 dsh-whale-moe.css: wm-breathe / wm-sway / wm-pose-in /
//   wm-squint / wm-hop / wm-react / wm-spin + 注视跟随 + 粒子
// 注意: 按 C# 5 语法书写(csc v4.0.30319), 不用 $"", ?., 表达式体等新语法。
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WhaleOverlay
{
    public class MainWindow : System.Windows.Window
    {
        private Grid petStage;
        private Canvas fxLayer;
        private Image[] layers;
        private ScaleTransform[] layerScale;
        private RotateTransform[] layerRotate;
        private TranslateTransform[] layerGaze;
        private TranslateTransform breatheT;
        private Border bubble;
        private TextBlock bubbleText;

        private int active = 0;
        private string cfgDir, cfgPath, assetDir;
        private double scale = DEFAULT_SCALE;
        private const double DEFAULT_SCALE = 0.4;
        private string[] poseNames;
        private BitmapImage[] poseFrames;
        private DispatcherTimer poseTimer, poseHoldTimer, microTimer, bubbleFadeTimer, stateTimer, topTimer;
        private int bubbleFadeStep = 0;
        private bool pressActive = false;
        private bool dragging = false;
        private Point pressPoint, pressScreen;
        private double pressLeft, pressTop;
        private double savedX = double.NaN, savedY = double.NaN;
        private Random rnd = new Random();
        private string currentPose = "idle-cute";
        private bool probe = false;
        /* ── DSH 任务状态跟随 ── */
        private string dshHome = @"D:\CWB\Data\deepseek.date";
        private bool dshBusy = false;
        private bool dshTool = false;
        private bool dshFailure = false;
        private DateTime lastBusyUtc = DateTime.MinValue;
        private string lastLoggedState = "";
        private MenuItem autoStartItem;
        /* ── 可折叠工作内容(复用原有气泡) ── */
        private TextBlock bubbleHead;
        private bool workCollapsed = false;
        private string workShown = "";
        private DateTime workSince = DateTime.MinValue;
        private DateTime workHideAt = DateTime.MinValue;
        private StackPanel bubbleActions;
        private Button btnKnow, btnView;
        private Border hoverBar;
        private Button hoverFocusBtn, hoverBalanceBtn;
        private DispatcherTimer hoverHideTimer;
        private string petTitle = "主人";
        private string petSelf = "鲸鱼娘";
        private bool taskWarning = false;
        private int nameTick = 0;
        private const double BOTTOM_BAR_H = 34;   /* 宠物下方留给悬停图标按钮条的高度 */
        private string currentTask = "";
        private string currentSession = "";
        private bool taskDone = false;
        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RUN_NAME = "WhaleOverlay";

        private const string IDLE = "idle-cute";
        private static readonly string[] ROTATION = { "thinking", "success", "celebrate", "eat", "sleep", "afk", "wink", "star", "curious", "greet", "night", "work-pat" };
        /* 工作中固定用「抱笔记本」那张(原插件 POSES.tool -> running,README:
           "检测到工具运行自动切抱笔记本工作")。EXTRA 里的备选图也一并加载,
           方便随时换姿势。 */
        private const string WORK_POSE = "running";
        private static readonly string[] EXTRA_POSES = { "running", "tool", "work-debug", "work-deploy", "work-meeting", "work-slack", "work-slack-phone", "work-review", "work-idea" };
        private static readonly string[] LINES = {
            "主人～我一直在这里守着你哦🐋",
            "看屏幕累了吧，歇一会儿～",
            "一切安好，鲸鱼娘待命中🎀",
            "窗外风很轻，适合摸鱼两分钟🌬️",
            "有需要就双击我～"
        };

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint command);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

        public MainWindow()
        {
            Title = "鲸鱼娘桌宠";
            Topmost = true;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;

            foreach (string a in Environment.GetCommandLineArgs())
            {
                if (a == "--probe") probe = true;
            }
            if (probe) Background = new SolidColorBrush(Color.FromRgb(255, 0, 0));

            cfgDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhaleOverlay");
            try { Directory.CreateDirectory(cfgDir); }
            catch (Exception) { cfgDir = AppDomain.CurrentDomain.BaseDirectory; }
            cfgPath = Path.Combine(cfgDir, "cfg.txt");
            assetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            LoadCfg();

            BuildUI();
            LoadFrames();
            SetScale(scale, true);

            poseTimer = new DispatcherTimer();
            poseTimer.Tick += delegate { RandomPose(); };
            poseHoldTimer = new DispatcherTimer();
            poseHoldTimer.Tick += delegate { poseHoldTimer.Stop(); CrossFade(IDLE, 0); };
            microTimer = new DispatcherTimer();
            microTimer.Tick += delegate { MicroAction(); };
            bubbleFadeTimer = new DispatcherTimer();
            bubbleFadeTimer.Interval = TimeSpan.FromMilliseconds(120);
            bubbleFadeTimer.Tick += delegate { FadeTick(); };

            /* 不放托盘图标(用户不要后台那个蓝色感叹号):
               隐藏/退出用右键菜单,重新启动用 DSH 设置里「桌面悬浮桌宠」的启动按钮。 */

            Closing += delegate { SaveCfg(); };

            /* 悬停条延迟隐藏(700ms):够时间把鼠标移到下方按钮上 */
            hoverHideTimer = new DispatcherTimer();
            hoverHideTimer.Interval = TimeSpan.FromMilliseconds(700);
            hoverHideTimer.Tick += delegate
            {
                hoverHideTimer.Stop();
                if (!dragging && hoverBar != null) hoverBar.Visibility = Visibility.Collapsed;
            };
            LoadNames();

            RestartPoseLoop();
            RestartMicroLoop();
            StartBreathe();

            /* 跟随 DSH 任务状态:每 600ms 轮询会话状态文件 */
            stateTimer = new DispatcherTimer();
            stateTimer.Interval = TimeSpan.FromMilliseconds(600);
            stateTimer.Tick += delegate { PollDsh(); };
            stateTimer.Start();
            /* 置顶守护:防止被全屏窗口压到下面 */
            topTimer = new DispatcherTimer();
            topTimer.Interval = TimeSpan.FromSeconds(2);
            topTimer.Tick += delegate { EnsureTopmost(); };
            topTimer.Start();
            /* 自启交给插件负责(插件加载时拉起、卸载时结束),这里不再自动写自启,
               避免和插件重复拉起。右键菜单里的开关仍可用。 */

            var wa = SystemParameters.WorkArea;
            Log("startup probe=" + probe + " cfgdir=" + cfgDir + " workarea=" + wa.Left + "," + wa.Top + "," + wa.Right + "," + wa.Bottom
                + " virtual=" + SystemParameters.VirtualScreenWidth + "x" + SystemParameters.VirtualScreenHeight
                + " primary=" + SystemParameters.PrimaryScreenWidth + "x" + SystemParameters.PrimaryScreenHeight);
            ContentRendered += delegate
            {
                Log("rendered left=" + Left + " top=" + Top + " actual=" + ActualWidth + "x" + ActualHeight
                    + " stage=" + (petStage == null ? -1 : petStage.ActualWidth) + "x" + (petStage == null ? -1 : petStage.ActualHeight)
                    + " src=" + (layers != null && layers[0].Source != null) + " op=" + (layers == null ? -1 : layers[0].Opacity));
            };
        }

        /* ───────────────────────── UI ───────────────────────── */

        private void BuildUI()
        {
            var root = new Grid();

            fxLayer = new Canvas();
            fxLayer.IsHitTestVisible = false;
            root.Children.Add(fxLayer);

            /* 一个气泡两用:头部=工作状态(可点击折叠),正文=台词/工作内容 */
            bubbleHead = new TextBlock();
            bubbleHead.Foreground = new SolidColorBrush(Color.FromRgb(168, 205, 255));
            bubbleHead.FontFamily = new FontFamily("Microsoft YaHei");
            bubbleHead.FontSize = 11;
            bubbleHead.Cursor = Cursors.Hand;
            bubbleHead.Visibility = Visibility.Collapsed;

            bubbleText = new TextBlock();
            bubbleText.Foreground = new SolidColorBrush(Color.FromRgb(242, 244, 255));
            bubbleText.FontFamily = new FontFamily("Microsoft YaHei");
            bubbleText.FontSize = 13;
            bubbleText.TextWrapping = TextWrapping.Wrap;
            bubbleText.MaxWidth = 226;

            var bubbleStack = new StackPanel();
            bubbleStack.Children.Add(bubbleHead);
            bubbleStack.Children.Add(bubbleText);

            /* 任务完成后的两个按钮:[知道了] 只关闭不跳转 / [查看] 跳到聊天页 */
            btnKnow = MakeBubbleButton("知道了", delegate { HideBubbleNow(); });
            btnView = MakeBubbleButton("查看", delegate { HideBubbleNow(); FocusDsh(); });
            bubbleActions = new StackPanel();
            bubbleActions.Orientation = Orientation.Horizontal;
            bubbleActions.HorizontalAlignment = HorizontalAlignment.Right;
            bubbleActions.Margin = new Thickness(0, 7, 0, 0);
            bubbleActions.Children.Add(btnKnow);
            bubbleActions.Children.Add(btnView);
            bubbleActions.Visibility = Visibility.Collapsed;
            bubbleStack.Children.Add(bubbleActions);

            bubble = new Border();
            bubble.Background = new SolidColorBrush(Color.FromArgb(226, 18, 22, 40));
            bubble.CornerRadius = new CornerRadius(11);
            bubble.Padding = new Thickness(10, 5, 10, 6);
            bubble.Child = bubbleStack;
            bubble.Opacity = 0;
            bubble.HorizontalAlignment = HorizontalAlignment.Center;
            bubble.VerticalAlignment = VerticalAlignment.Bottom;
            bubble.Margin = new Thickness(0, 0, 0, 160);
            bubble.Cursor = Cursors.Hand;
            /* 气泡可命中(里面有两个按钮);非按钮区域点击会冒泡到窗口层 → 拖动/折叠仍然正常 */
            bubble.IsHitTestVisible = true;
            root.Children.Add(bubble);

            breatheT = new TranslateTransform(0, 0);
            var breatheGroup = new TransformGroup();
            breatheGroup.Children.Add(breatheT);

            petStage = new Grid();
            petStage.RenderTransform = breatheGroup;
            petStage.RenderTransformOrigin = new Point(0.5, 0.9);
            petStage.HorizontalAlignment = HorizontalAlignment.Center;
            petStage.VerticalAlignment = VerticalAlignment.Bottom;
            petStage.Margin = new Thickness(0, 0, 0, BOTTOM_BAR_H);

            /* 不加任何底衬/描边/投影:立绘本身透明(用户要求画面干净)。
               注意:非要用 ShaderEffect 或 OpacityMask 的话,透明分层窗口下整窗会渲染成空白。 */

            layers = new Image[2];
            layerScale = new ScaleTransform[2];
            layerRotate = new RotateTransform[2];
            layerGaze = new TranslateTransform[2];
            for (int i = 0; i < 2; i++)
            {
                var img = new Image();
                img.Stretch = Stretch.Uniform;
                img.Opacity = 0;
                var tg = new TransformGroup();
                layerScale[i] = new ScaleTransform(1, 1);
                layerRotate[i] = new RotateTransform(0);
                layerGaze[i] = new TranslateTransform(0, 0);
                tg.Children.Add(layerScale[i]);
                tg.Children.Add(layerRotate[i]);
                tg.Children.Add(layerGaze[i]);
                img.RenderTransform = tg;
                img.RenderTransformOrigin = new Point(0.5, 0.92);
                layers[i] = img;
                petStage.Children.Add(img);
            }
            layers[0].Opacity = 1;
            root.Children.Add(petStage);

            /* 鼠标悬停时出现在宠物下方的图标按钮条:
               💻 = 呼出 DeepSeek Harness 桌面端   💰 = 查看余额(从右键菜单挪过来的) */
            hoverFocusBtn = MakeIconButton("💻", "呼出 DeepSeek Harness", delegate { FocusDsh(); });
            hoverBalanceBtn = MakeIconButton("💰", "查看余额", delegate { ShowBalance(); });
            var barStack = new StackPanel();
            barStack.Orientation = Orientation.Horizontal;
            barStack.HorizontalAlignment = HorizontalAlignment.Center;
            barStack.Children.Add(hoverFocusBtn);
            barStack.Children.Add(hoverBalanceBtn);
            hoverBar = new Border();
            hoverBar.Background = new SolidColorBrush(Color.FromArgb(210, 24, 30, 50));
            hoverBar.CornerRadius = new CornerRadius(10);
            hoverBar.Padding = new Thickness(3, 2, 3, 2);
            hoverBar.Child = barStack;
            hoverBar.HorizontalAlignment = HorizontalAlignment.Center;
            hoverBar.VerticalAlignment = VerticalAlignment.Bottom;
            hoverBar.Margin = new Thickness(0, 0, 0, 6);
            hoverBar.Visibility = Visibility.Collapsed;
            root.Children.Add(hoverBar);

            Content = root;
            MouseEnter += delegate
            {
                try { if (hoverHideTimer != null) hoverHideTimer.Stop(); } catch (Exception) { }
                if (!dragging) hoverBar.Visibility = Visibility.Visible;
            };
            MouseLeave += delegate
            {
                /* 延迟隐藏:给用户从宠物移到下方按钮的时间,不然一点就消失 */
                if (hoverHideTimer == null) { hoverBar.Visibility = Visibility.Collapsed; return; }
                hoverHideTimer.Stop();
                hoverHideTimer.Start();
            };

            var menu = new ContextMenu();
            AddMenuItem(menu, "放大", delegate { SetScale(scale * 1.25, false); Say("变大啦～", 1500); React(); });
            AddMenuItem(menu, "缩小", delegate { SetScale(scale / 1.25, false); Say("缩小一点咯～", 1500); React(); });
            AddMenuItem(menu, "重置大小", delegate { SetScale(DEFAULT_SCALE, false); });
            AddMenuItem(menu, "回到默认位置", delegate { ResetPosition(); });
            menu.Items.Add(new Separator());
            autoStartItem = new MenuItem();
            autoStartItem.Click += delegate { SetAutoStart(!IsAutoStart()); RefreshAutoStartLabel(); };
            menu.Items.Add(autoStartItem);
            menu.Opened += delegate { RefreshAutoStartLabel(); };
            menu.Items.Add(new Separator());
            AddMenuItem(menu, "呼出 DeepSeek Harness", delegate { FocusDsh(); });
            AddMenuItem(menu, "退出桌宠", delegate { Shutdown(); });
            ContextMenu = menu;

            MouseLeftButtonDown += OnPetDown;
            MouseLeftButtonUp += OnPetUp;
            MouseMove += OnPetMove;
        }

        private static void AddMenuItem(ContextMenu menu, string text, Action action)
        {
            var item = new MenuItem();
            item.Header = text;
            item.Click += delegate { action(); };
            menu.Items.Add(item);
        }

        private void LoadFrames()
        {
            try
            {
                var names = new System.Collections.Generic.List<string>();
                names.Add(IDLE);
                names.AddRange(ROTATION);
                names.AddRange(EXTRA_POSES);
                var frames = new System.Collections.Generic.List<BitmapImage>();
                foreach (string n in names)
                {
                    string png = Path.Combine(assetDir, "dsh-whale-state-" + n + ".png");
                    string webp = Path.Combine(assetDir, "dsh-whale-state-" + n + ".webp");
                    string path = File.Exists(png) ? png : webp;
                    if (!File.Exists(path)) continue;
                    BitmapImage bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(path);
                    bi.EndInit();
                    bi.Freeze();
                    frames.Add(bi);
                }
                poseNames = names.ToArray();
                poseFrames = frames.ToArray();
                if (poseFrames.Length > 0)
                {
                    layers[0].Source = Pose(IDLE);
                    layers[0].Opacity = 1;
                    StartSway(0);
                }
                Log("loaded " + poseFrames.Length + " frames from " + assetDir);
            }
            catch (Exception ex) { Log("frames: " + ex); }
        }

        private BitmapImage Pose(string name)
        {
            if (poseNames == null) return null;
            for (int i = 0; i < poseNames.Length && i < poseFrames.Length; i++)
            {
                if (poseNames[i] == name) return poseFrames[i];
            }
            return poseFrames != null && poseFrames.Length > 0 ? poseFrames[0] : null;
        }

        /* ─────────────── 动效: 呼吸 / 摇摆 / 交叉淡入 ─────────────── */

        private void StartBreathe()
        {
            // wm-breathe: 幅度加大到 6px(144px 小尺寸下也看得出在动)
            var a = new DoubleAnimation(0, -6, TimeSpan.FromSeconds(1.5));
            a.AutoReverse = true;
            a.RepeatBehavior = RepeatBehavior.Forever;
            var e = new SineEase();
            e.EasingMode = EasingMode.EaseInOut;
            a.EasingFunction = e;
            breatheT.BeginAnimation(TranslateTransform.YProperty, a);
        }

        private void StartSway(int idx)
        {
            // wm-sway: 0/25/75/100% → 0 / 1.2deg / -1.2deg / 0, 6s infinite
            var kf = new DoubleAnimationUsingKeyFrames();
            kf.Duration = new Duration(TimeSpan.FromSeconds(6));
            kf.RepeatBehavior = RepeatBehavior.Forever;
            var ease = new SineEase();
            ease.EasingMode = EasingMode.EaseInOut;
            kf.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))));
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(2.6, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5)), ease));
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3)), ease));
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(-2.6, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.5)), ease));
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(6)), ease));
            layerRotate[idx].BeginAnimation(RotateTransform.AngleProperty, kf);
        }

        // 双图层交叉淡入(wm-pose-in): 新图层 0.2→1 + translateY 4→0 + scale 1.04→1, 旧图层淡出
        private void CrossFade(string name, int holdMs)
        {
            if (poseFrames == null || poseFrames.Length == 0) return;
            BitmapImage b = Pose(name);
            if (b == null) return;
            currentPose = name;
            int next = 1 - active;
            layers[next].Source = b;

            var dur = new Duration(TimeSpan.FromMilliseconds(320));
            var fadeIn = new DoubleAnimation(0.2, 1, dur);
            var ce = new CubicEase();
            ce.EasingMode = EasingMode.EaseOut;
            fadeIn.EasingFunction = ce;
            fadeIn.FillBehavior = FillBehavior.HoldEnd;

            var ty = new DoubleAnimation(4, 0, dur);
            ty.EasingFunction = ce;
            ty.FillBehavior = FillBehavior.HoldEnd;
            var sx = new DoubleAnimation(1.04, 1, dur);
            sx.EasingFunction = ce;
            sx.FillBehavior = FillBehavior.HoldEnd;
            var sy = new DoubleAnimation(1.04, 1, dur);
            sy.EasingFunction = ce;
            sy.FillBehavior = FillBehavior.HoldEnd;

            int fading = active;
            int incoming = next;
            fadeIn.Completed += delegate
            {
                layers[incoming].Opacity = 1;
                layers[incoming].BeginAnimation(OpacityProperty, null);
                layerGaze[incoming].BeginAnimation(TranslateTransform.YProperty, null);
                layers[fading].Opacity = 0;
                layers[fading].BeginAnimation(OpacityProperty, null);
                layerScale[fading].BeginAnimation(ScaleTransform.ScaleXProperty, null);
                layerScale[fading].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                layerRotate[fading].BeginAnimation(RotateTransform.AngleProperty, null);
            };

            layers[next].BeginAnimation(OpacityProperty, fadeIn);
            layerGaze[next].BeginAnimation(TranslateTransform.YProperty, ty);
            layerScale[next].BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            layerScale[next].BeginAnimation(ScaleTransform.ScaleYProperty, sy);

            var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(200)));
            layers[active].BeginAnimation(OpacityProperty, fadeOut);

            layers[next].BeginAnimation(OpacityProperty, fadeIn);
            StartSway(next);
            active = next;

            if (holdMs > 0)
            {
                poseHoldTimer.Interval = TimeSpan.FromMilliseconds(holdMs);
                poseHoldTimer.Stop();
                poseHoldTimer.Start();
            }
        }

        /* ─────────────── 微动作: 挤压 / 弹跳 / 旋转 / 跳跃 ─────────────── */

        private void Squint()
        {
            // wm-squint: translateY 0→-6 且 scale(1.04, 0.94), 560ms
            var dur = new Duration(TimeSpan.FromMilliseconds(560));
            var ease = new SineEase();
            ease.EasingMode = EasingMode.EaseInOut;

            var kfY = new DoubleAnimationUsingKeyFrames();
            kfY.Duration = dur;
            kfY.FillBehavior = FillBehavior.Stop;
            kfY.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            kfY.KeyFrames.Add(new EasingDoubleKeyFrame(-6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(224)), ease));
            kfY.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)), ease));

            var kfX = new DoubleAnimationUsingKeyFrames();
            kfX.Duration = dur;
            kfX.FillBehavior = FillBehavior.Stop;
            kfX.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            kfX.KeyFrames.Add(new EasingDoubleKeyFrame(1.04, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(224)), ease));
            kfX.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)), ease));

            var kfYs = new DoubleAnimationUsingKeyFrames();
            kfYs.Duration = dur;
            kfYs.FillBehavior = FillBehavior.Stop;
            kfYs.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            kfYs.KeyFrames.Add(new EasingDoubleKeyFrame(0.94, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(224)), ease));
            kfYs.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)), ease));

            layerGaze[active].BeginAnimation(TranslateTransform.YProperty, kfY);
            layerScale[active].BeginAnimation(ScaleTransform.ScaleXProperty, kfX);
            layerScale[active].BeginAnimation(ScaleTransform.ScaleYProperty, kfYs);
        }

        private void React()
        {
            // 点击弹一下 scale 1→1.06→1
            var dur = new Duration(TimeSpan.FromMilliseconds(420));
            var ease = new CubicEase();
            ease.EasingMode = EasingMode.EaseOut;
            var kf = new DoubleAnimationUsingKeyFrames();
            kf.Duration = dur;
            kf.FillBehavior = FillBehavior.Stop;
            kf.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(1.07, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), ease));
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), ease));
            layerScale[active].BeginAnimation(ScaleTransform.ScaleXProperty, kf);
            layerScale[active].BeginAnimation(ScaleTransform.ScaleYProperty, kf);
        }

        private void Hop()
        {
            // wm-hop: 弹跳 720ms
            var kf = new DoubleAnimationUsingKeyFrames();
            kf.Duration = new Duration(TimeSpan.FromMilliseconds(720));
            kf.FillBehavior = FillBehavior.Stop;
            var be = new BackEase();
            be.EasingMode = EasingMode.EaseOut;
            be.Amplitude = 0.6;
            kf.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(-20, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)), be));
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(720))));
            layerGaze[active].BeginAnimation(TranslateTransform.YProperty, kf);
            SpawnParticles(8);
        }

        private void Spin()
        {
            var kf = new DoubleAnimationUsingKeyFrames();
            kf.Duration = new Duration(TimeSpan.FromMilliseconds(850));
            kf.FillBehavior = FillBehavior.Stop;
            var ease = new CubicEase();
            ease.EasingMode = EasingMode.EaseInOut;
            kf.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(360, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(850)), ease));
            layerRotate[active].BeginAnimation(RotateTransform.AngleProperty, kf);
            SpawnParticles(12);
        }

        private void SpawnParticles(int count)
        {
            if (fxLayer == null) return;
            string[] glyphs = { "✨", "💖", "⭐", "🫧", "🎀" };
            double baseX = Width / 2.0;
            double baseY = Height - 60.0 - (320 * scale) / 2.0;
            for (int i = 0; i < count; i++)
            {
                var tb = new TextBlock();
                tb.Text = glyphs[rnd.Next(glyphs.Length)];
                tb.FontSize = 12 + rnd.Next(8);
                tb.IsHitTestVisible = false;
                double dx = (rnd.NextDouble() - 0.5) * 120;
                Canvas.SetLeft(tb, baseX + dx);
                Canvas.SetTop(tb, baseY);
                fxLayer.Children.Add(tb);

                var move = new DoubleAnimation(0, -46 - rnd.Next(30), new Duration(TimeSpan.FromMilliseconds(900 + rnd.Next(400))));
                var fade = new DoubleAnimation(0.95, 0, new Duration(TimeSpan.FromMilliseconds(1000 + rnd.Next(400))));
                var tt = new TranslateTransform(0, 0);
                tb.RenderTransform = tt;
                var self = tb;
                fade.Completed += delegate
                {
                    if (fxLayer.Children.Contains(self)) fxLayer.Children.Remove(self);
                };
                tt.BeginAnimation(TranslateTransform.YProperty, move);
                tb.BeginAnimation(OpacityProperty, fade);
            }
        }

        /* ─────────────── 循环与互动 ─────────────── */

        private void RestartPoseLoop()
        {
            poseTimer.Interval = TimeSpan.FromSeconds(12 + rnd.Next(14));
            poseTimer.Start();
        }

        private void RestartMicroLoop()
        {
            microTimer.Interval = TimeSpan.FromSeconds(7 + rnd.Next(9));
            microTimer.Start();
        }

        private void RandomPose()
        {
            if (dshBusy) { RestartPoseLoop(); return; }
            if (currentPose != IDLE) return;
            string pick = ROTATION[rnd.Next(ROTATION.Length)];
            CrossFade(pick, 3600 + rnd.Next(2200));
            if (rnd.NextDouble() < 0.4) Say(LINES[rnd.Next(LINES.Length)], 3600);
            RestartPoseLoop();
        }

        private void MicroAction()
        {
            if (currentPose == IDLE) Squint();
            RestartMicroLoop();
        }

        private void Say(string text, int ms)
        {
            /* 完成提示还挂着(等用户点[知道了]/[查看])时,别被台词覆盖掉 */
            if (taskDone) return;
            if (bubbleHead != null) bubbleHead.Visibility = Visibility.Collapsed;
            if (bubbleText != null) { bubbleText.Visibility = Visibility.Visible; bubbleText.Text = Localize(text); }
            bubble.Opacity = 1;
            bubbleFadeStep = (int)Math.Ceiling(ms / 120.0);
            bubbleFadeTimer.Stop();
            bubbleFadeTimer.Start();
        }

        /* ── 称呼(设置面板「如何称呼我 / 她的自称」→ desktop-pet\names.json) ── */
        private void LoadNames()
        {
            try
            {
                string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "names.json");
                if (!File.Exists(f)) return;
                string t = File.ReadAllText(f);
                Match m = Regex.Match(t, "\"title\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (m.Success && JsonUnescape(m.Groups[1].Value).Length > 0) petTitle = JsonUnescape(m.Groups[1].Value);
                Match s = Regex.Match(t, "\"selfName\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (s.Success && JsonUnescape(s.Groups[1].Value).Length > 0) petSelf = JsonUnescape(s.Groups[1].Value);
            }
            catch (Exception) { }
        }

        private string Localize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            try { return text.Replace("鲸鱼娘", petSelf).Replace("主人", petTitle); }
            catch (Exception) { return text; }
        }

        private void FadeOutBubble()
        {
            if (bubble == null || bubble.Opacity <= 0) return;
            if (bubbleFadeTimer.IsEnabled) return;
            bubbleFadeStep = 25;
            bubbleFadeTimer.Start();
        }

        private void FadeTick()
        {
            bubbleFadeStep--;
            if (bubbleFadeStep <= 0)
            {
                bubbleFadeTimer.Stop();
                bubble.Opacity = 0;
            }
            else
            {
                bubble.Opacity = Math.Max(0, Math.Min(1, bubbleFadeStep / 10.0));
            }
        }

        /* ── 拖动 ────────────────────────────────────────────────────────
           整窗可拖(含气泡)。用「移动阈值」区分点击与拖动:
             · 按住后移动 >3px → 拖动:手动跟随鼠标(不用 DragMove),
               并把气泡藏起来,拖动过程中只剩立绘在动,不会看到那个深色方块
             · 没移动就松开 → 点击:落在气泡上=折叠/展开,落在立绘上=抚摸互动 */
        private void OnPetDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            pressActive = true;
            dragging = false;
            pressPoint = e.GetPosition(this);
            pressScreen = PointToScreen(pressPoint);
            pressLeft = Left;
            pressTop = Top;
            try { CaptureMouse(); } catch (Exception) { }
        }

        private void OnPetMove(object sender, MouseEventArgs e)
        {
            // 注视跟随(--wm-gaze-x/y): 只对活动图层做几像素的偏移
            try
            {
                if (layers != null && poseFrames != null && !dragging)
                {
                    Point gp = e.GetPosition(this);
                    double nx = (gp.X / Math.Max(1, Width)) - 0.5;
                    layerGaze[active].X = Math.Max(-4, Math.Min(4, nx * 8));
                }
            }
            catch (Exception) { }

            if (!pressActive || e.LeftButton != MouseButtonState.Pressed) return;
            try
            {
                Point cur = PointToScreen(e.GetPosition(this));
                double dx = cur.X - pressScreen.X;
                double dy = cur.Y - pressScreen.Y;
                if (!dragging && (Math.Abs(dx) > 3 || Math.Abs(dy) > 3))
                {
                    dragging = true;
                    if (bubble != null) bubble.Opacity = 0;   /* 拖动时只留立绘 */
                    if (hoverBar != null) hoverBar.Visibility = Visibility.Collapsed;
                }
                if (!dragging) return;
                /* 立绘本体不许出屏,但允许贴边(两侧透明留白可以露出屏幕) */
                Left = pressLeft + dx;
                Top = pressTop + dy;
                ClampToWorkArea();
            }
            catch (Exception) { }
        }

        private bool IsOverBubble(Point p)
        {
            try
            {
                if (bubble == null || bubble.Visibility != Visibility.Visible || bubble.Opacity <= 0.05) return false;
                Rect r = bubble.TransformToAncestor(this).TransformBounds(new Rect(new Point(0, 0), bubble.RenderSize));
                return r.Contains(p);
            }
            catch (Exception) { return false; }
        }

        private void OnPetUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            bool wasDrag = dragging;
            dragging = false;
            pressActive = false;
            try { ReleaseMouseCapture(); } catch (Exception) { }

            if (wasDrag)
            {
                SaveCfg();          /* 拖动结束:记住新位置 */
                return;
            }
            if (e.ClickCount >= 3)
            {
                Spin();
                Say("转圈圈～🌀", 1800);
                SaveCfg();
                return;
            }
            if (e.ClickCount >= 2)
            {
                CrossFade("celebrate", 2600);
                Hop();
                Say("耶～和主人贴贴！✨", 2400);
                SaveCfg();
                return;
            }
            /* 任务完成气泡:点非按钮区域 = 查看(跳到聊天页) */
            if (taskDone)
            {
                HideBubbleNow();
                FocusDsh();
                return;
            }
            /* 单击:点在气泡上就折叠/展开,否则是抚摸 */
            if (IsOverBubble(e.GetPosition(this)))
            {
                ToggleWorkPanel();
                return;
            }
            React();
            SpawnParticles(4);
            CrossFade("wink", 1500);
            Say(LINES[rnd.Next(LINES.Length)], 3400);
            /* 单击宠物顺带把 DeepSeek Harness 桌面端呼出来(用户要求) */
            FocusDsh();
            SaveCfg();
        }

        private void SetScale(double s, bool initial)
        {
            scale = Math.Max(0.15, Math.Min(3.0, s));
            double w = 320 * scale;
            double h = 320 * scale;
            if (petStage != null)
            {
                petStage.Width = w;
                petStage.Height = h;
                for (int i = 0; i < layers.Length; i++)
                {
                    layers[i].Width = w;
                    layers[i].Height = h;
                }
                if (bubble != null) bubble.Margin = new Thickness(0, 0, 0, h + BOTTOM_BAR_H + 12);
            }
            double nw = Math.Max(340 * scale + 40, 250);   /* 至少 250 宽,别夹住工作气泡 */
            double nh = 360 * scale + 120 + BOTTOM_BAR_H;
            if (!initial)
            {
                double cX = Left + Width / 2.0;
                double bY = Top + Height;
                Width = nw;
                Height = nh;
                Left = cX - Width / 2.0;
                Top = Math.Max(SystemParameters.WorkArea.Top, bY - Height);
            }
            else
            {
                Width = nw;
                Height = nh;
                if (!double.IsNaN(savedX) && !double.IsNaN(savedY))
                {
                    Left = savedX; Top = savedY;
                    ClampToWorkArea();      /* 上次的位置若在屏幕外,这里会拉回来 */
                }
                else
                {
                    ResetPosition();
                }
            }
            ClampToWorkArea();
            SaveCfg();
        }

        private void ResetPosition()
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 60;
            Top = wa.Bottom - Height - 20;
            ClampToWorkArea();
            SaveCfg();
        }

        /* 只约束「立绘本体」留在桌面内:窗口两侧/下方的透明留白允许露出屏幕,
           这样宠物可以贴到左右下三条边;上方留 100px 给任务气泡。 */
        private void ClampToWorkArea()
        {
            var wa = SystemParameters.WorkArea;
            double petW = 320 * scale;
            double sideMargin = Math.Max(0, (Width - petW) / 2.0);
            double bottomMargin = BOTTOM_BAR_H + 6;
            double minL = wa.Left - sideMargin;
            double maxL = wa.Right - petW - sideMargin;
            double minT = wa.Top + 100 - (Height - bottomMargin - petW);
            double maxT = wa.Bottom - Height + bottomMargin;
            if (maxL < minL) { minL = wa.Left; maxL = wa.Left; }
            if (maxT < minT) { minT = wa.Top; maxT = wa.Top; }
            Left = Math.Max(minL, Math.Min(Left, maxL));
            Top = Math.Max(minT, Math.Min(Top, maxT));
        }

        /* ── 查看余额 ────────────────────────────────────────────────────
           优先走插件宿主提供的接口(宿主在 Node 侧读凭据调官方接口,避开 .NET 的 TLS 坑),
           地址默认就是 DSH GUI 的 127.0.0.1:19387。 */
        private void ShowBalance()
        {
            Say("💰 正在查询余额…", 2000);
            System.Threading.Tasks.Task.Run(delegate
            {
                string msg = QueryBalance();
                Dispatcher.BeginInvoke(new Action(delegate { Say(msg, 8000); }));   /* 停留 8 秒 */
            });
        }

        private string QueryBalance()
        {
            string[] bases = { "http://127.0.0.1:19387", "http://127.0.0.1:19388" };
            foreach (string b in bases)
            {
                try
                {
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(b + "/api/dsh-newpet/balance");
                    req.Timeout = 12000;
                    using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream()))
                    {
                        string body = sr.ReadToEnd();
                        Match m = Regex.Match(body, "\"total\"\\s*:\\s*\"?([0-9.]+)");
                        if (m.Success)
                        {
                            Match g = Regex.Match(body, "\"granted\"\\s*:\\s*\"?([0-9.]+)");
                            Match t = Regex.Match(body, "\"topped\"\\s*:\\s*\"?([0-9.]+)");
                            return "💰 DeepSeek 余额 ¥" + m.Groups[1].Value
                                + (g.Success ? "（赠送 " + g.Groups[1].Value + " + 充值 " + (t.Success ? t.Groups[1].Value : "-") + "）" : "");
                        }
                        Match e = Regex.Match(body, "\"error\"\\s*:\\s*\"([^\"]*)\"");
                        if (e.Success && e.Groups[1].Value != "no-key") return "余额获取失败：" + e.Groups[1].Value;
                    }
                }
                catch (Exception) { /* 试下一个地址 */ }
            }
            /* 兜底:宿主接口不可用(或 DSH 未重启)时,本进程直连官方接口 */
            try
            {
                string key = ReadDeepSeekKey();
                if (key != null)
                {
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://api.deepseek.com/user/balance");
                    req.Headers.Add("Authorization", "Bearer " + key);
                    req.Timeout = 15000;
                    using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream()))
                    {
                        string body = sr.ReadToEnd();
                        Match m = Regex.Match(body, "\"total_balance\"\\s*:\\s*\"([0-9.]+)\"");
                        if (m.Success)
                        {
                            Match g = Regex.Match(body, "\"granted_balance\"\\s*:\\s*\"([0-9.]+)\"");
                            Match t = Regex.Match(body, "\"topped_up_balance\"\\s*:\\s*\"([0-9.]+)\"");
                            return "💰 DeepSeek 余额 ¥" + m.Groups[1].Value
                                + (g.Success ? "（赠送 " + g.Groups[1].Value + " + 充值 " + (t.Success ? t.Groups[1].Value : "-") + "）" : "");
                        }
                    }
                }
            }
            catch (Exception) { }
            return "余额获取失败：拿不到数据（检查网络或 API Key）";
        }

        private string ReadDeepSeekKey()
        {
            try
            {
                string[] paths = { Path.Combine(dshHome, ".credentials.yaml"), @"C:\Users\ThinkBook\.dsh\.credentials.yaml" };
                foreach (string p in paths)
                {
                    if (!File.Exists(p)) continue;
                    Match m = Regex.Match(File.ReadAllText(p), "DEEPSEEK_API_KEY:\\s*([A-Za-z0-9_-]+)");
                    if (m.Success) return m.Groups[1].Value;
                }
            }
            catch (Exception) { }
            return null;
        }

        /* 供窗口诊断和回归检查使用:Electron 隐藏到后台时主窗口仍然存在。
           只选无 owner 的 Chrome_WidgetWin_1,排除托盘、消息和输入法辅助窗口。
           不用 MainWindowHandle —— 隐藏窗口时它常常为 0。 */
        private static IntPtr FindDshWindow(System.Collections.Generic.List<uint> pids)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = -1;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (!pids.Contains(pid) || GetWindow(h, 4) != IntPtr.Zero) return true; // GW_OWNER
                var className = new StringBuilder(256);
                GetClassName(h, className, className.Capacity);
                if (className.ToString() != "Chrome_WidgetWin_1") return true;
                RECT r;
                if (!GetWindowRect(h, out r)) return true;
                long area = Math.Max(0L, (long)r.Right - r.Left) * Math.Max(0L, (long)r.Bottom - r.Top);
                if (area > bestArea) { bestArea = area; best = h; }
                return true;
            }, IntPtr.Zero);
            return best;
        }

        private static void FocusDsh()
        {
            try
            {
                string executable = null;
                foreach (Process p in Process.GetProcessesByName("DeepSeek Harness"))
                {
                    using (p)
                    {
                        if (executable != null) continue;
                        try
                        {
                            string candidate = p.MainModule.FileName;
                            if (File.Exists(candidate)) executable = candidate;
                        }
                        catch (Exception) { /* Electron 子进程可能已退出或不可访问,继续找主进程 */ }
                    }
                }
                if (executable == null) { Log("focus: 没找到正在运行的桌面端程序"); return; }
                /* DSH 的 single-instance 入口会通知已有进程执行 window.show/restore/focus。
                   不从外部 ShowWindow:窗口可见状态和模态对话框交给 Electron 自己管理,
                   这样再次点击叉号仍走正常的关闭到后台流程。不会创建第二个桌面端。 */
                var start = new ProcessStartInfo(executable);
                // 插件来自 Electron 的 Node 模式宿主,继承 ELECTRON_RUN_AS_NODE=1。
                // 为桌面端单独构造环境,否则同一 exe 会变成 Node 命令行,不会发出 second-instance。
                start.UseShellExecute = false;
                start.EnvironmentVariables.Remove("ELECTRON_RUN_AS_NODE");
                start.EnvironmentVariables.Remove("ELECTRON_NO_ASAR");
                start.EnvironmentVariables.Remove("NODE_OPTIONS");
                start.EnvironmentVariables.Remove("NODE_PATH");
                start.CreateNoWindow = true;
                start.WorkingDirectory = Path.GetDirectoryName(executable);
                using (Process launched = Process.Start(start)) { }
                Log("focus: 已清除 Node 模式环境并请求 single-instance 唤起现有窗口");
            }
            catch (Exception ex) { Log("focus: " + ex.Message); }
        }

        /* ── 置顶守护 ────────────────────────────────────────────────────
           Windows 上别的程序(尤其全屏 Electron 窗口)会把桌宠压到下面,
           只靠 Topmost=true 不保险:每 2 秒重新把窗口提到 HWND_TOPMOST。 */
        private void EnsureTopmost()
        {
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                IntPtr h = helper.Handle;
                if (h == IntPtr.Zero) return;
                SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            }
            catch (Exception) { }
        }

        private Button MakeIconButton(string icon, string tip, Action action)
        {
            var b = new Button();
            b.Content = icon;
            b.FontSize = 12;
            b.Width = 26;
            b.Height = 22;
            b.Margin = new Thickness(2, 0, 2, 0);
            b.Padding = new Thickness(0);
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = new SolidColorBrush(Color.FromArgb(110, 150, 190, 255));
            b.Background = new SolidColorBrush(Color.FromArgb(70, 70, 110, 190));
            b.Foreground = new SolidColorBrush(Color.FromRgb(240, 245, 255));
            b.Cursor = Cursors.Hand;
            b.ToolTip = tip;
            b.Click += delegate { try { action(); } catch (Exception) { } };
            return b;
        }

        /* ── 气泡按钮与"任务完成"状态 ───────────────────────────────────── */
        private Button MakeBubbleButton(string text, Action action)
        {
            var b = new Button();
            b.Content = text;
            b.FontSize = 12;
            b.Margin = new Thickness(7, 0, 0, 0);
            b.Padding = new Thickness(11, 3, 11, 3);
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = new SolidColorBrush(Color.FromArgb(130, 150, 190, 255));
            b.Background = new SolidColorBrush(Color.FromArgb(90, 60, 100, 180));
            b.Foreground = new SolidColorBrush(Color.FromRgb(235, 240, 255));
            b.Cursor = Cursors.Hand;
            b.Click += delegate { try { action(); } catch (Exception) { } };
            return b;
        }

        /* 任务完成:气泡显示"已完成 + 任务内容",并给出 [知道了] / [查看] 两个按钮 */
        private void ShowTaskDone()
        {
            try
            {
                taskDone = true;
                if (bubbleHead == null) return;
                bubbleHead.Visibility = Visibility.Visible;
                bubbleHead.Text = taskWarning ? "⚠️ 任务已结束（可能有报错）" : "✅ 任务已完成";
                bubbleText.Text = currentTask.Length > 0 ? CollapseText(currentTask, 80) : "（未取到任务内容）";
                bubbleText.Visibility = Visibility.Visible;
                bubbleActions.Visibility = Visibility.Visible;
                bubbleFadeTimer.Stop();
                bubbleFadeStep = 0;
                bubble.Opacity = 1;
                workHideAt = DateTime.UtcNow.AddMinutes(10);   /* 有按钮,不自动消失 */
                Log("task done: " + currentTask);
            }
            catch (Exception) { }
        }

        private void HideBubbleNow()
        {
            try
            {
                taskDone = false;
                if (bubbleActions != null) bubbleActions.Visibility = Visibility.Collapsed;
                if (bubbleHead != null) bubbleHead.Visibility = Visibility.Collapsed;
                if (bubble != null) bubble.Opacity = 0;
                if (bubbleFadeTimer != null) bubbleFadeTimer.Stop();
                workHideAt = DateTime.UtcNow;
            }
            catch (Exception) { }
        }

        /* ── 可折叠工作内容(直接写在原有气泡里) ────────────────────────── */
        private void UpdateWorkPanel(string text, int seconds)
        {
            try
            {
                if (bubble == null) return;
                if (dragging) { workHideAt = DateTime.UtcNow.AddSeconds(8); return; }   /* 拖动中不弹气泡 */
                if (taskDone) return;                                                   /* 完成提示优先,别覆盖按钮 */
                if (bubbleActions != null) bubbleActions.Visibility = Visibility.Collapsed;
                bubbleHead.Visibility = Visibility.Visible;
                bubbleHead.Text = (workCollapsed ? "▸ 工作中 · " : "▾ 工作中 · ") + seconds + "s  （点击" + (workCollapsed ? "展开" : "折叠") + "）";
                if (text != workShown) { workShown = text; bubbleText.Text = text; Log("work: " + text); }
                bubbleText.Visibility = workCollapsed ? Visibility.Collapsed : Visibility.Visible;
                bubbleFadeTimer.Stop();
                bubbleFadeStep = 0;
                bubble.Opacity = 1;
                workHideAt = DateTime.UtcNow.AddSeconds(8);
            }
            catch (Exception) { }
        }

        private void ToggleWorkPanel()
        {
            workCollapsed = !workCollapsed;
            if (bubbleText != null) bubbleText.Visibility = workCollapsed ? Visibility.Collapsed : Visibility.Visible;
            int secs = workSince == DateTime.MinValue ? 0 : (int)Math.Max(0, (DateTime.UtcNow - workSince).TotalSeconds);
            if (bubbleHead != null) bubbleHead.Text = (workCollapsed ? "▸ 工作中 · " : "▾ 工作中 · ") + secs + "s  （点击" + (workCollapsed ? "展开" : "折叠") + "）";
            SaveCfg();
        }

        private static string CollapseText(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = Regex.Replace(s, "\\s+", " ").Trim();
            if (t.Length <= max) return t;
            return "…" + t.Substring(t.Length - max);
        }

        private static string JsonUnescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == 't') sb.Append('\t');
                    else if (n == 'u' && i + 4 < s.Length)
                    {
                        int code;
                        if (int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                    }
                    else sb.Append(n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /* ── 跟随 DSH 任务状态 ────────────────────────────────────────────
           只读文件,不碰 DSH 的接口(接口要鉴权):
             · storages\session_projcache\sessions\*.json — 明文状态(openStep /
               pendingCalls / lastStepBoundary / llmRetry)
             · sessions\*\session.jsonl.zstd              — 正文(zstd 压缩),只用修改时间判忙闲
           映射:思考(忙) / 工具(忙且有 pendingCalls) / 完成(忙→闲且无错误信号) / 失败(有错误信号) */
        private void PollDsh()
        {
            try
            {
                DateTime newest = DateTime.MinValue;
                string newestJson = null;
                string projDir = Path.Combine(dshHome, "storages", "session_projcache", "sessions");
                if (Directory.Exists(projDir))
                {
                    string[] files = Directory.GetFiles(projDir, "*.json");
                    for (int i = 0; i < files.Length; i++)
                    {
                        DateTime t = File.GetLastWriteTimeUtc(files[i]);
                        if (t > newest) { newest = t; newestJson = files[i]; }
                    }
                }
                string sessRoot = Path.Combine(dshHome, "sessions");
                if (Directory.Exists(sessRoot))
                {
                    string[] dirs = Directory.GetDirectories(sessRoot);
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        string[] fs = Directory.GetFiles(dirs[i], "session.jsonl*");
                        for (int j = 0; j < fs.Length; j++)
                        {
                            DateTime t = File.GetLastWriteTimeUtc(fs[j]);
                            if (t > newest) newest = t;
                        }
                    }
                }
                if (newest == DateTime.MinValue) return;
                /* 每 ~6 秒重读一次称呼(设置面板改了就生效) */
                nameTick++;
                if (nameTick % 10 == 0) LoadNames();

                double age = (DateTime.UtcNow - newest).TotalMilliseconds;
                bool busy = age < 2500;
                bool tool = false;
                bool failure = false;
                string draft = "";
                string toolNames = "";
                string stepInfo = "";
                if (newestJson != null && age < 120000)
                {
                    string txt = File.ReadAllText(newestJson);
                    tool = Regex.IsMatch(txt, "\"pendingCalls\"\\s*:\\s*\\{\\s*\"");
                    failure = Regex.IsMatch(txt, "\"llmRetry\"\\s*:\\s*\\{\\s*\"") || Regex.IsMatch(txt, "\"failure\"\\s*:\\s*\\{\\s*\"");
                    if (!busy && Regex.IsMatch(txt, "\"lastStepBoundary\"\\s*:\\s*\\{[^}]*\"kind\"\\s*:\\s*\"start\"")) busy = true;
                    /* 我下达的工作任务 = 该会话最后一轮 turn 的 prompt */
                    MatchCollection prompts = Regex.Matches(txt, "\"prompt\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                    if (prompts.Count > 0)
                    {
                        string p = JsonUnescape(prompts[prompts.Count - 1].Groups[1].Value);
                        if (p.Length > 0 && p != currentTask) { currentTask = p; Log("task: " + currentTask); }
                    }
                    Match msess = Regex.Match(Path.GetFileName(newestJson), "session-([0-9a-fA-F-]+)");
                    if (msess.Success) currentSession = msess.Groups[1].Value;
                    Match md = Regex.Match(txt, "\"draft\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                    if (md.Success) draft = JsonUnescape(md.Groups[1].Value);
                    Match ms = Regex.Match(txt, "\"openStep\"\\s*:\\s*\\{[^}]*\"turn\"\\s*:\\s*(\\d+)[^}]*\"step\"\\s*:\\s*(\\d+)");
                    if (ms.Success) stepInfo = ms.Groups[1].Value + "." + ms.Groups[2].Value;
                    Match mc = Regex.Match(txt, "\"pendingCalls\"\\s*:\\s*\\{([^}]*)\\}");
                    if (mc.Success && mc.Groups[1].Value.Trim().Length > 0)
                    {
                        MatchCollection nm = Regex.Matches(mc.Groups[1].Value, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                        var names = new System.Collections.Generic.List<string>();
                        for (int k = 0; k < nm.Count && names.Count < 3; k++) names.Add(nm[k].Groups[1].Value);
                        if (names.Count > 0) toolNames = string.Join("、", names.ToArray());
                        else
                        {
                            int cnt = Regex.Matches(mc.Groups[1].Value, "\"call_[^\"]*\"\\s*:").Count;
                            toolNames = cnt > 0 ? "工具×" + cnt : "工具";
                        }
                    }
                }

                if (busy)
                {
                    lastBusyUtc = DateTime.UtcNow;
                    string want = WORK_POSE;   /* 工作中 = 坐着抱笔记本那张 */
                    if (!dshBusy)
                    {
                        dshBusy = true;
                        workSince = DateTime.UtcNow;
                        CrossFade(want, 0);
                        Say(tool ? "工具开工！这单交给我～" : "正在思考……尾巴都转起来了", 2200);
                    }
                    else if (currentPose != want)
                    {
                        CrossFade(want, 0);
                    }
                    string act;
                    if (toolNames.Length > 0) act = "🔧 正在调用：" + toolNames;
                    else if (draft.Length > 0) act = "✍️ " + CollapseText(draft, 150);
                    else act = "🤔 思考中…";
                    if (stepInfo.Length > 0) act = act + "  ·  第 " + stepInfo + " 步";
                    /* 正文先显示「我下达的任务」,再显示当前进度 */
                    string body = currentTask.Length > 0 ? "📋 " + CollapseText(currentTask, 70) + "\n" + act : act;
                    int secs = (int)Math.Max(0, (DateTime.UtcNow - workSince).TotalSeconds);
                    UpdateWorkPanel(body, secs);
                }
                else if (dshBusy && (DateTime.UtcNow - lastBusyUtc).TotalMilliseconds > 1800)
                {
                    dshBusy = false;
                    /* 不论有没有报错信号,都给「已完成 + [知道了]/[查看]」的可点击气泡:
                       之前误判成 failure 时会走"出错了"分支,用户就看不到完成提示了。 */
                    taskWarning = failure;
                    if (failure) CrossFade("failure", 4200);
                    else CrossFade("celebrate", 4200);
                    ShowTaskDone();
                    SpawnParticles(12);
                }
                dshTool = tool;
                dshFailure = failure;
                if (!busy && !taskDone && DateTime.UtcNow > workHideAt) FadeOutBubble();
                string tag = busy ? (tool ? "tool" : "thinking") : (failure ? "failure" : "idle");
                if (tag != lastLoggedState) { lastLoggedState = tag; Log("dsh state -> " + tag + " pose=" + currentPose + " (age=" + Math.Round(age) + "ms)"); }
            }
            catch (Exception ex) { Log("polldsh: " + ex.Message); }
        }

        /* ── 开机自启 ─────────────────────────────────────────────────────
           本机策略禁止写 HKCU\...\Run、也禁止建计划任务,所以主用「启动文件夹
           里放一个 .cmd 启动器」,注册表作为备选(能写就一起写)。 */
        private static readonly string STARTUP_FALLBACK =
            @"C:\Users\ThinkBook\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup";

        private string StartupCmd()
        {
            string dir = null;
            try { dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup); } catch (Exception) { }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = STARTUP_FALLBACK;
            return Path.Combine(dir, "WhaleOverlay.cmd");
        }

        private bool IsAutoStart()
        {
            try { if (File.Exists(StartupCmd())) return true; }
            catch (Exception) { }
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, false))
                {
                    if (k != null)
                    {
                        string v = k.GetValue(RUN_NAME) as string;
                        if (!string.IsNullOrEmpty(v)) return true;
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        private void SetAutoStart(bool on)
        {
            string cmd = StartupCmd();
            try
            {
                if (on)
                {
                    string exe = Process.GetCurrentProcess().MainModule.FileName;
                    File.WriteAllText(cmd, "@echo off\r\nstart \"\" \"" + exe + "\"\r\n");
                }
                else if (File.Exists(cmd))
                {
                    File.Delete(cmd);
                }
                Log("autostart=" + on + " file=" + cmd + " exists=" + File.Exists(cmd));
            }
            catch (Exception ex) { Log("setautostart(startup): " + ex.Message); }
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    if (k != null)
                    {
                        if (on) k.SetValue(RUN_NAME, "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
                        else k.DeleteValue(RUN_NAME, false);
                    }
                }
            }
            catch (Exception ex) { Log("setautostart(reg): " + ex.Message); }
        }

        private void RefreshAutoStartLabel()
        {
            if (autoStartItem != null) autoStartItem.Header = "开机自启：" + (IsAutoStart() ? "已开启 ✓" : "已关闭");
        }

        private void LoadCfg()
        {
            try
            {
                if (!File.Exists(cfgPath)) return;
                double v;
                foreach (string line in File.ReadAllLines(cfgPath))
                {
                    string[] kv = line.Split('=');
                    if (kv.Length == 2 && kv[0] == "scale" && double.TryParse(kv[1], out v)) scale = v;
                    if (kv.Length == 2 && kv[0] == "x" && double.TryParse(kv[1], out v)) savedX = v;
                    if (kv.Length == 2 && kv[0] == "y" && double.TryParse(kv[1], out v)) savedY = v;
                    if (kv.Length == 2 && kv[0] == "workcollapsed") workCollapsed = kv[1] == "1";
                }
                Log("loadcfg scale=" + scale + " from " + cfgPath);
            }
            catch (Exception ex) { Log("loadcfg: " + ex); }
        }

        private void SaveCfg()
        {
            try
            {
                Directory.CreateDirectory(cfgDir);
                File.WriteAllText(cfgPath, "x=" + Left + "\r\ny=" + Top + "\r\nscale=" + scale + "\r\nworkcollapsed=" + (workCollapsed ? "1" : "0") + "\r\n");
                Log("save scale=" + scale + " at " + Left + "," + Top);
            }
            catch (Exception ex) { Log("savecfg: " + ex); }
        }

        private void Shutdown()
        {
            SaveCfg();
            Close();
            System.Windows.Application.Current.Shutdown();
            Environment.Exit(0);
        }

        private static void Log(string line)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whale-overlay.log"),
                    DateTime.Now.ToString("HH:mm:ss") + " " + line + "\r\n");
            }
            catch (Exception) { }
        }
    }

    public static class Program
    {
        /* 单实例:插件会拉起它,开机自启也可能拉起它,重复启动时后一个直接退出 */
        private static System.Threading.Mutex singleInstance;

        [STAThread]
        public static void Main()
        {
            bool createdNew;
            singleInstance = new System.Threading.Mutex(true, "WhaleOverlay.SingleInstance.v1", out createdNew);
            if (!createdNew) return;
            /* 余额直连官方接口需要 TLS 1.2(.NET Framework 默认可能只有 TLS 1.0) */
            try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; }
            catch (Exception) { }
            try
            {
                var app = new System.Windows.Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whale-overlay.log"),
                            "DISPATCHER " + e.Exception + "\r\n");
                    }
                    catch (Exception) { }
                    e.Handled = true;
                };
                app.Run(new MainWindow());
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whale-overlay.log"),
                        "FATAL " + ex + "\r\n");
                }
                catch (Exception) { }
            }
        }
    }
}
