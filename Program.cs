using SDL3;
using SponzaTest.Assets;
using SponzaTest.Graphics;
using SponzaTest.Scene;

namespace SponzaTest;

internal static class Program
{
    const string AppName = "SponzaTest";
    const string AppVer = "1.0";
    const string AppId = "dev.lucasangelo.sponzatest";
    const int WindowWidth = 1600;
    const int WindowHeight = 900;

    public static bool ShouldQuit = false;
    public static IntPtr Window;

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            SDL.SetAppMetadata(AppName, AppVer, AppId);

            if (!SDL.Init(SDL.InitFlags.Video))
                throw new InvalidOperationException($"Couldn't initialize SDL: {SDL.GetError()}");

            Window = SDL.CreateWindow(AppName, WindowWidth, WindowHeight,
                SDL.WindowFlags.Resizable | SDL.WindowFlags.HighPixelDensity);
            if (Window == IntPtr.Zero)
                throw new InvalidOperationException($"Couldn't create window: {SDL.GetError()}");

            Initialize(args);

            while (!ShouldQuit)
            {
                while (SDL.PollEvent(out var e))
                    Event(e);

                if (ShouldQuit) break;
                Update();
                Draw();
            }
        }
        finally
        {
            Cleanup();
            if (Window != IntPtr.Zero)
                SDL.DestroyWindow(Window);
            SDL.Quit();
        }
    }

    private static SdlGpuContext? _gpu;
    private static AssetLoader? _loader;
    
    private static SceneGraph? _graph;
    private static SceneRenderer? _renderer;
    private static LightingController? _lighting;
    
    private static Camera? _camera;
    private static OrbitControls? _controls;

    static void Initialize(string[] args)
    {
        _gpu = new SdlGpuContext(Window);
        _loader = new AssetLoader(_gpu);
        
        _graph = new SceneGraph();
        _renderer = new SceneRenderer(_gpu);
        _lighting = new LightingController(_graph.DirLight);

        var preset = ScenePreset.Sponza;
        preset.Populate(_graph, _loader, _lighting.Config);
        
        _lighting.UpdateLightFromAngles();
        
        _camera = new Camera { Position = preset.CameraPosition, Target = preset.CameraTarget };
        _controls = new OrbitControls(_camera);
    }

    static void Update()
    {
        
    }

    static void Draw()
    {
        _renderer!.Draw(_graph!, _camera!, _lighting!.Config.Exposure, _lighting.Config.AmbientIntensity, _lighting.Config.SkyboxExposure);
    }

    static void Event(SDL.Event e)
    {
        if ((SDL.EventType)e.Type == SDL.EventType.Quit)
            ShouldQuit = true;

        _controls!.HandleEvent(in e);
    }

    static void Cleanup()
    {
        _renderer?.Dispose();
        _graph?.Dispose();
        _loader?.Dispose();
        _gpu?.Dispose();
    }
}
