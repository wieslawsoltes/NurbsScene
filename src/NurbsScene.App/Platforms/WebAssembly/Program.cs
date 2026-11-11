using Uno.UI.Hosting;
using Uno.UI.Composition.WebGpu;
namespace NurbsScene.App;
public static class Program
{
    public static async Task Main(string[] args)
    {
        var host=UnoPlatformHostBuilder.Create().App(()=>new App())
            .GraphicsBackend(WebGpuBackend.CreateGraphicsProvider())
            .UseWebAssembly().Build();
        await host.RunAsync();
    }
}
