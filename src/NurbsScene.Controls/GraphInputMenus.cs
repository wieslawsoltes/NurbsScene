using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NurbsScene.Parametric;

namespace NurbsScene.Controls;

public sealed partial class GraphEditorControl
{
    private void AddInputAndMatchingMenus(MenuFlyout menu,Graph expected,GraphNode node)
    {
        MenuFlyoutSubItem Sub(string text,string id)
        {
            var item=new MenuFlyoutSubItem{Text=text,FontSize=11,MinHeight=24,Padding=new(8,2,8,2)};
            AutomationProperties.SetAutomationId(item,id);_contextElements.Add(item);return item;
        }
        void Edit(string name,Action<Graph> action)
        {
            if(Session is null||!ReferenceEquals(Session.Graph,expected))return;
            try{Session.Edit(name,action);RefreshInspector();UpdateStatus();}
            catch(Exception error){Report(error.Message);}
        }
        if(node.Component is "Number" or "Integer")
        {
            var slider=new MenuFlyoutItem{Text="Edit slider…",FontSize=11,MinHeight=24,Padding=new(8,2,8,2)};
            AutomationProperties.SetAutomationId(slider,"GraphEditSlider");_contextElements.Add(slider);
            slider.Click+=(_,_)=>QueueNumberSliderEdit(expected,node.Id);menu.Items.Add(slider);
        }
        var matching=Sub("Data matching","GraphMatchingMenu");
        foreach(var mode in new[]{"longest","shortest","cross"})
        {
            var item=new ToggleMenuFlyoutItem{Text=mode switch{"longest"=>"Longest list","shortest"=>"Shortest list",_=>"Cross reference"},
                FontSize=11,MinHeight=24,Padding=new(8,2,8,2),IsChecked=node.Matching==mode};
            AutomationProperties.SetAutomationId(item,"GraphMatching_"+mode);_contextElements.Add(item);
            item.Click+=(_,_)=>Edit("Change data matching",g=>g.Update(g.GetNode(node.Id) with{Matching=mode}));matching.Items.Add(item);
        }
        menu.Items.Add(matching);
        var inputs=Sub("Inputs","GraphInputsMenu");
        foreach(var port in expected.Registry.Get(node.Component).Inputs)
        {
            var address=new PortAddress(node.Id,port.Name);var incoming=expected.Incoming(address);
            var input=Sub($"{port.Name} · {port.Kind} / {port.Access}","GraphInput_"+port.Name);
            foreach(var transform in new[]{"none","flatten","graft","simplify"})
            {
                var title=transform switch{"none"=>"As wired","flatten"=>"Flatten","graft"=>"Graft",_=>"Simplify"};
                var item=new ToggleMenuFlyoutItem{Text=title,FontSize=11,MinHeight=24,Padding=new(8,2,8,2),
                    IsChecked=incoming.Count>0&&incoming.All(w=>w.Transform==transform),IsEnabled=incoming.Count>0};
                AutomationProperties.SetAutomationId(item,$"GraphInputTransform_{port.Name}_{transform}");_contextElements.Add(item);
                item.Click+=(_,_)=>Edit("Input wires: "+title,g=>g.SetWireTransforms(g.Incoming(address).Select(w=>w.Id).ToArray(),transform));
                input.Items.Add(item);
            }
            input.Items.Add(new MenuFlyoutSeparator());
            var disconnect=new MenuFlyoutItem{Text=$"Disconnect ({incoming.Count} wires)",FontSize=11,MinHeight=24,Padding=new(8,2,8,2),IsEnabled=incoming.Count>0};
            AutomationProperties.SetAutomationId(disconnect,"GraphDisconnect_"+port.Name);_contextElements.Add(disconnect);
            disconnect.Click+=(_,_)=>Edit("Disconnect input",g=>{foreach(var wire in g.Incoming(address))g.Disconnect(wire.Id);});
            input.Items.Add(disconnect);inputs.Items.Add(input);
        }
        if(inputs.Items.Count>0)menu.Items.Add(inputs);
    }
}
