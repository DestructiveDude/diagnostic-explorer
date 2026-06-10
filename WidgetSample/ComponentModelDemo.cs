using System.ComponentModel;
using DiagnosticExplorer.Props;

namespace WidgetSample;

public class ComponentModelDemo
{
    public ComponentModelDemo()
    {
        Prop1 = "Browsable - no category";
        Prop2 = "Browsable - with category";
        Prop3 = "Not browsable - but has property attribute";
    }

    [Browsable(true), Description("This is the description form Prop1")]
    public string Prop1 { get; set; }

    [Browsable(true), Description("This is the description form Prop2"), Category("Cat2")]
    public string Prop2 { get; set; }

    [Browsable(false)]
    [Property]
    public string Prop3 { get; internal set; }
}
