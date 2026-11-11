using Uno.UI.Hosting;
using Uno.UI.Composition.WebGpu;
namespace NurbsScene.App;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var host=UnoPlatformHostBuilder.Create().App(()=>new App())
            .GraphicsBackend(WebGpuBackend.CreateGraphicsProvider())
            .UseX11().UseMacOS().UseWin32().Build();
        host.Run();
    }
}
