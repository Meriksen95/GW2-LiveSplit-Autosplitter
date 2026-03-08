using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Xml;

namespace LiveSplit.GW2
{
    public class Gw2Component : LogicComponent
    {
        private readonly TimerModel _timer;
        private readonly Gw2MumbleReader _reader;
        private readonly Timer _updateTimer;

        private const int PollIntervalMs = 200;
        private const int EnterStableMs = 800;
        private const int TickStallMs = 1200;
        private const int SplitCooldownMs = 1500;

        private bool _armed = true;
        private bool _runStarted = false;
        private bool _inTransition = false;

        private uint _lastMapId = 0;
        private uint _lastInstance = 0;
        private uint _lastTick = 0;

        private DateTime _lastTickChangeTime = DateTime.MinValue;
        private DateTime _startSeenSince = DateTime.MinValue;
        private DateTime _lastSplitTime = DateTime.MinValue;

        private string _debugText = "Waiting for GW2...";
        private string _lastCharacterName = "";
        private string _configPath;
        private string _configStatus = "Config not loaded";

        private int _currentSplitIndex = 0;

        private SplitConfigRoot _config;
        private readonly List<ConfiguredSplit> _splits = new List<ConfiguredSplit>();

        public Gw2Component(LiveSplitState state)
        {
            _timer = new TimerModel { CurrentState = state };
            _reader = new Gw2MumbleReader();

            _configPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Components",
                "GW2Splitter",
                "gw2_splits.json"
            );

            LoadConfig();

            _updateTimer = new Timer();
            _updateTimer.Interval = PollIntervalMs;
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();
        }

        public override string ComponentName => "GW2 Auto Splitter";

        private void LoadConfig()
        {
            _splits.Clear();

            try
            {
                _config = SplitConfigLoader.Load(_configPath);

                foreach (SplitConfig splitConfig in _config.Splits)
                {
                    ITrigger primary = BuildTrigger(splitConfig.Primary);
                    ITrigger fallback = BuildTrigger(splitConfig.Fallback);

                    _splits.Add(new ConfiguredSplit(splitConfig.Name, primary, fallback));
                }

                _configStatus = $"Loaded: {Path.GetFileName(_configPath)} | Splits={_splits.Count}";
            }
            catch (Exception ex)
            {
                _config = null;
                _configStatus = $"Config error: {ex.Message}";
            }
        }

        private ITrigger BuildTrigger(TriggerConfig config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.Type))
                return null;

            string type = config.Type.Trim().ToLowerInvariant();

            if (type == "circle")
            {
                if (!config.MapId.HasValue || !config.X.HasValue || !config.Z.HasValue || !config.Radius.HasValue)
                    return null;

                return new PositionTrigger(
                    config.MapId.Value,
                    config.X.Value,
                    config.Z.Value,
                    config.Radius.Value
                );
            }

            if (type == "polygon")
            {
                if (!config.MapId.HasValue || config.Points == null || config.Points.Count < 3)
                    return null;

                var points = new List<PolygonPoint>();

                foreach (TriggerPointConfig point in config.Points)
                {
                    if (!point.X.HasValue || !point.Z.HasValue)
                        return null;

                    points.Add(new PolygonPoint(point.X.Value, point.Z.Value));
                }

                return new PolygonTrigger(config.MapId.Value, points);
            }

            if (type == "map")
            {
                if (!config.MapId.HasValue)
                    return null;

                return new MapTrigger(config.MapId.Value);
            }

            if (type == "bossdeath")
            {
                if (string.IsNullOrWhiteSpace(config.Boss))
                    return null;

                return new BossDeathTrigger(config.Boss);
            }

            return null;
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (!_reader.TryRead(out var data))
            {
                _debugText = "Could not read MumbleLink";
                return;
            }

            uint mapId = data.context.mapId;
            uint instance = data.context.instance;
            uint uiTick = data.link.uiTick;

            uint previousMapId = _lastMapId;

            HandleTickState(uiTick);
            HandleMapState(mapId, instance);

            float playerX = data.link.fAvatarPosition[0];
            float playerZ = data.link.fAvatarPosition[2];
            string charName = (data.link.name ?? "").Trim('\0');

            if (!string.IsNullOrWhiteSpace(charName))
                _lastCharacterName = charName;

            HandleManualReset();
            HandleManualStart();

            if (_config?.Start != null)
                HandleStart(mapId);

            HandleSplitProgress(previousMapId, mapId, playerX, playerZ);

            string currentSplitName =
                _currentSplitIndex < _splits.Count
                ? _splits[_currentSplitIndex].Name
                : "None";

            string runName = _config?.RunName ?? "No config";

            _debugText =
                $"Run={runName} | Map={mapId} | PrevMap={previousMapId} | Inst={instance} | Tick={uiTick} | " +
                $"Transition={_inTransition} | Armed={_armed} | Started={_runStarted} | " +
                $"SplitIndex={_currentSplitIndex}/{_splits.Count} | Next={currentSplitName} | " +
                $"Player=({playerX:0.0}, {playerZ:0.0}) | Char={_lastCharacterName} | {_configStatus}";
        }

        private void HandleTickState(uint uiTick)
        {
            if (uiTick != _lastTick)
            {
                _lastTick = uiTick;
                _lastTickChangeTime = DateTime.UtcNow;
                _inTransition = false;
            }
            else
            {
                if (_lastTickChangeTime != DateTime.MinValue &&
                    (DateTime.UtcNow - _lastTickChangeTime).TotalMilliseconds >= TickStallMs)
                {
                    _inTransition = true;
                }
            }
        }

        private void HandleMapState(uint mapId, uint instance)
        {
            if (mapId != _lastMapId || instance != _lastInstance)
            {
                _lastMapId = mapId;
                _lastInstance = instance;
            }
        }

        private void HandleManualReset()
        {
            if (_timer.CurrentState.CurrentPhase == TimerPhase.NotRunning && !_armed)
            {
                _armed = true;
                _runStarted = false;
                _currentSplitIndex = 0;
                _startSeenSince = DateTime.MinValue;
                _lastSplitTime = DateTime.MinValue;
                ResetAllSplits();
            }
        }

        private void HandleManualStart()
        {
            if (_timer.CurrentState.CurrentPhase == TimerPhase.Running && !_runStarted)
            {
                _armed = false;
                _runStarted = true;
                _currentSplitIndex = 0;
                _lastSplitTime = DateTime.UtcNow;
                _startSeenSince = DateTime.MinValue;
                ResetAllSplits();
            }
        }

        private void HandleStart(uint mapId)
        {
            if (_config == null || _config.Start == null)
                return;

            if (_inTransition)
                return;

            if (_runStarted)
                return;

            if (!_armed)
                return;

            bool startConditionMet = false;

            string startType = (_config.Start.Type ?? "").Trim().ToLowerInvariant();

            if (startType == "map")
            {
                startConditionMet = mapId == _config.Start.MapId;
            }

            if (startConditionMet)
            {
                if (_startSeenSince == DateTime.MinValue)
                    _startSeenSince = DateTime.UtcNow;

                bool enterStable =
                    (DateTime.UtcNow - _startSeenSince).TotalMilliseconds >= EnterStableMs;

                if (enterStable && _timer.CurrentState.CurrentPhase == TimerPhase.NotRunning)
                {
                    _timer.Start();
                    _armed = false;
                    _runStarted = true;
                    _currentSplitIndex = 0;
                    _lastSplitTime = DateTime.UtcNow;
                    ResetAllSplits();
                }
            }
            else
            {
                _startSeenSince = DateTime.MinValue;
            }
        }

        private void HandleSplitProgress(uint previousMapId, uint mapId, float playerX, float playerZ)
        {
            if (_config == null)
                return;

            if (!_runStarted)
                return;

            if (_timer.CurrentState.CurrentPhase != TimerPhase.Running)
                return;

            if (_currentSplitIndex >= _splits.Count)
                return;

            ConfiguredSplit split = _splits[_currentSplitIndex];

            bool isMapTrigger =
                split.Primary is MapTrigger ||
                split.Fallback is MapTrigger;

            if (_inTransition && !isMapTrigger)
                return;

            if ((DateTime.UtcNow - _lastSplitTime).TotalMilliseconds < SplitCooldownMs)
                return;

            bool primaryTriggered = split.Primary != null && split.Primary.IsTriggered(previousMapId, mapId, playerX, playerZ);
            bool fallbackTriggered = split.Fallback != null && split.Fallback.IsTriggered(previousMapId, mapId, playerX, playerZ);

            if (primaryTriggered || fallbackTriggered)
            {
                split.HasTriggered = true;
                _timer.Split();
                _lastSplitTime = DateTime.UtcNow;
                _currentSplitIndex++;
            }
        }

        private void ResetAllSplits()
        {
            foreach (ConfiguredSplit split in _splits)
            {
                split.HasTriggered = false;
                split.Primary?.Reset();
                split.Fallback?.Reset();
            }
        }

        public override void Dispose()
        {
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
            _reader?.Dispose();
        }

        public override XmlNode GetSettings(XmlDocument document)
        {
            XmlElement settings = document.CreateElement("Settings");
            settings.SetAttribute("ConfigPath", _configPath);
            return settings;
        }

        public override Control GetSettingsControl(LayoutMode mode)
        {
            Label label = new Label();
            label.AutoSize = true;
            label.Text =
                "GW2 Auto Splitter active\n" +
                $"Debug: {_debugText}\n\n" +
                $"Config: {_configPath}\n" +
                "JSON config is loaded from the Components folder.\n" +
                "Manual reset in LiveSplit rearms the splitter.";
            return label;
        }

        public override void SetSettings(XmlNode settings)
        {
        }

        public override void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        {
        }

        private sealed class ConfiguredSplit
        {
            public string Name { get; }
            public ITrigger Primary { get; }
            public ITrigger Fallback { get; }
            public bool HasTriggered { get; set; }

            public ConfiguredSplit(string name, ITrigger primary, ITrigger fallback)
            {
                Name = name;
                Primary = primary;
                Fallback = fallback;
                HasTriggered = false;
            }
        }

        private interface ITrigger
        {
            bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerZ);
            void Reset();
        }

        private sealed class PositionTrigger : ITrigger
        {
            public uint MapId { get; }
            public float X { get; }
            public float Z { get; }
            public float Radius { get; }

            public PositionTrigger(uint mapId, float x, float z, float radius)
            {
                MapId = mapId;
                X = x;
                Z = z;
                Radius = radius;
            }

            public bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerZ)
            {
                if (mapId != MapId)
                    return false;

                float dx = playerX - X;
                float dz = playerZ - Z;
                float distanceSquared = dx * dx + dz * dz;
                float radiusSquared = Radius * Radius;

                return distanceSquared <= radiusSquared;
            }

            public void Reset()
            {
            }
        }

        private sealed class PolygonTrigger : ITrigger
        {
            public uint MapId { get; }
            public List<PolygonPoint> Points { get; }

            public PolygonTrigger(uint mapId, List<PolygonPoint> points)
            {
                MapId = mapId;
                Points = points;
            }

            public bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerZ)
            {
                if (mapId != MapId)
                    return false;

                return IsPointInsidePolygon(playerX, playerZ, Points);
            }

            public void Reset()
            {
            }

            private static bool IsPointInsidePolygon(float x, float z, List<PolygonPoint> polygon)
            {
                bool inside = false;
                int j = polygon.Count - 1;

                for (int i = 0; i < polygon.Count; i++)
                {
                    float xi = polygon[i].X;
                    float zi = polygon[i].Z;
                    float xj = polygon[j].X;
                    float zj = polygon[j].Z;

                    bool intersect =
                        ((zi > z) != (zj > z)) &&
                        (x < (xj - xi) * (z - zi) / ((zj - zi) == 0 ? 0.000001f : (zj - zi)) + xi);

                    if (intersect)
                        inside = !inside;

                    j = i;
                }

                return inside;
            }
        }

        private sealed class MapTrigger : ITrigger
        {
            public uint TargetMapId { get; }

            public MapTrigger(uint targetMapId)
            {
                TargetMapId = targetMapId;
            }

            public bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerZ)
            {
                return previousMapId != mapId && mapId == TargetMapId;
            }

            public void Reset()
            {
            }
        }

        private sealed class BossDeathTrigger : ITrigger
        {
            public string BossName { get; }

            public BossDeathTrigger(string bossName)
            {
                BossName = bossName;
            }

            public bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerZ)
            {
                return false;
            }

            public void Reset()
            {
            }
        }

        private sealed class PolygonPoint
        {
            public float X { get; }
            public float Z { get; }

            public PolygonPoint(float x, float z)
            {
                X = x;
                Z = z;
            }
        }
    }
}