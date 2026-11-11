using Uno.UI.Hosting;
using Uno.UI.Composition.WebGpu;
using Uno.UI.Composition.Managed;
using NurbsScene.Controls.Drawing;
namespace NurbsScene.App;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var host=UnoPlatformHostBuilder.Create().App(()=>new App())
            .GraphicsBackend(WebGpuBackend.CreateGraphicsProvider())
            .FontProvider(ManagedBackend.CreateFontProvider(BundledFonts.GetDefaultFontBytes()))
            .UseX11().UseMacOS().UseWin32().Build();
        host.Run();
    }
}
