using Uno.UI.Hosting;
using Uno.UI.Composition.WebGpu;
using NurbsScene.Controls.Drawing;
namespace NurbsScene.App;
public static class Program
{
    public static async Task Main(string[] args)
    {
        var host=UnoPlatformHostBuilder.Create().App(()=>new App())
            .GraphicsBackend(WebGpuBackend.CreateGraphicsProvider())
            .FontProvider(BundledFonts.CreateProvider())
            .UseWebAssembly().Build();
        await host.RunAsync();
    }
}
