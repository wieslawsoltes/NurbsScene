using Uno.UI.Hosting;
namespace NurbsScene.App;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var host=UnoPlatformHostBuilder.Create().App(()=>new App()).UseX11().UseMacOS().UseWin32().Build();
        host.Run();
    }
}
