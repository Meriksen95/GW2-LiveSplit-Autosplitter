using LiveSplit.Model;
using LiveSplit.UI.Components;
using System;

namespace LiveSplit.GW2
{
    public class Gw2Factory : IComponentFactory
    {
        public string ComponentName => "GW2 Auto Splitter";
        public string Description => "Starts LiveSplit when GW2 enters a target map via MumbleLink.";
        public ComponentCategory Category => ComponentCategory.Control;
        public string UpdateName => ComponentName;
        public string UpdateURL => "";
        public string XMLURL => "";
        public Version Version => new Version(0, 1, 0);

        public IComponent Create(LiveSplitState state)
        {
            return new Gw2Component(state);
        }
    }
}