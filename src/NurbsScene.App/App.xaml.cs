using Microsoft.UI.Xaml;
using NurbsScene.Controls;

namespace NurbsScene.App;

public partial class App : Application
{
    public static Window? MainWindow {get;private set;}
    public App(){InitializeComponent();UnhandledException+=(_,e)=>System.Console.Error.WriteLine("NURBSSCENE_UNHANDLED: "+e.Exception);}
    partial void ConfigureQualification(WorkspaceView workspace);
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var workspace=new WorkspaceView();MainWindow=new Window{Title="NurbsScene — NURBS + visual programming",Content=workspace};
        MainWindow.Closed+=(_,_)=>workspace.Dispose();MainWindow.Activate();ConfigureQualification(workspace);
    }
}
