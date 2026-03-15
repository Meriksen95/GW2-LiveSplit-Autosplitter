using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Xml;

namespace LiveSplit.GW2
{
    public class Gw2Component : LogicComponent
    {
        private const string FullWingMode = "FullWing";
        private const string RouteMode = "Route";
        private const string CombatStateInCombat = "inCombat";
        private const string CombatStateOutOfCombat = "outOfCombat";
        private const string PersistedSettingsFileName = "component-settings.xml";
        private const int SettingsSubtitleWrapWidth = 360;
        private const int SettingsValueWrapWidth = 300;
        private const int SettingsComboBoxWidth = 180;

        private readonly TimerModel _timer;
        private readonly Gw2MumbleReader _reader;
        private readonly Timer _updateTimer;

        private const int PollIntervalMs = 200;
        private const int TickStallMs = 1200;
        private const int SplitCooldownMs = 1000;

        private bool _runStarted = false;
        private bool _inTransition = false;
        private bool _configuredRunComplete = false;

        private uint _lastMapId = 0;
        private uint _lastInstance = 0;
        private uint _lastTick = 0;

        private DateTime _lastTickChangeTime = DateTime.MinValue;
        private DateTime _lastSplitTime = DateTime.MinValue;
        private string _lastTriggeredName = null;

        private string _debugText = "Waiting for GW2...";
        private string _configRootFolder;
        private string _configStatus = "Config not loaded";

        private string _selectedMode = FullWingMode;
        private string _selectedRouteFile = "";

        private DateTime _lastConfigCheckTime = DateTime.MinValue;
        private const int ConfigCheckIntervalMs = 1000;
        private Dictionary<string, DateTime> _knownConfigFiles = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private TableLayoutPanel _settingsPanel;
        private Label _settingsSubtitleLabel;
        private TableLayoutPanel _statusLayout;
        private Label _configRootValueLabel;
        private Label _configStatusValueLabel;
        private Label _liveDebugValueLabel;

        // Wing mode
        private readonly Dictionary<uint, List<ConfiguredSplit>> _splitsByMap =
            new Dictionary<uint, List<ConfiguredSplit>>();

        private readonly Dictionary<uint, int> _splitIndexByMap =
            new Dictionary<uint, int>();

        // Route mode
        private bool _routeMode = false;
        private string _routeName = "";
        private readonly List<ConfiguredSplit> _routeSplits = new List<ConfiguredSplit>();
        private int _currentRouteIndex = 0;

        public Gw2Component(LiveSplitState state)
        {
            _timer = new TimerModel { CurrentState = state };
            _reader = new Gw2MumbleReader();

            _configRootFolder = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Components",
                "GW2AutoSplitter"
            );

            LoadPersistedSettings();
            LoadConfig();

            _updateTimer = new Timer();
            _updateTimer.Interval = PollIntervalMs;
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();
        }

        public override string ComponentName => "GW2 Auto Splitter";

        private Dictionary<string, DateTime> GetConfigFileSnapshot()
        {
            var snapshot = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(_configRootFolder))
                return snapshot;

            foreach (string file in Directory.GetFiles(_configRootFolder, "*.json", SearchOption.AllDirectories))
            {
                snapshot[file] = File.GetLastWriteTimeUtc(file);
            }

            return snapshot;
        }

        private bool ConfigFilesChanged()
        {
            Dictionary<string, DateTime> current = GetConfigFileSnapshot();

            if (_knownConfigFiles.Count != current.Count)
                return true;

            foreach (KeyValuePair<string, DateTime> pair in current)
            {
                if (!_knownConfigFiles.TryGetValue(pair.Key, out DateTime knownTime))
                    return true;

                if (knownTime != pair.Value)
                    return true;
            }

            return false;
        }

        private void CheckForConfigReload()
        {
            if ((DateTime.UtcNow - _lastConfigCheckTime).TotalMilliseconds < ConfigCheckIntervalMs)
                return;

            _lastConfigCheckTime = DateTime.UtcNow;

            if (!ConfigFilesChanged())
                return;

            LoadConfig();
        }

        private string GetPersistedSettingsPath()
        {
            return Path.Combine(_configRootFolder, PersistedSettingsFileName);
        }

        private void NormalizeSelections()
        {
            if (!string.Equals(_selectedMode, FullWingMode, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_selectedMode, RouteMode, StringComparison.OrdinalIgnoreCase))
            {
                _selectedMode = FullWingMode;
            }

            if (!string.IsNullOrWhiteSpace(_selectedRouteFile))
            {
                string routePath = Path.Combine(_configRootFolder, "routes", _selectedRouteFile);

                if (!File.Exists(routePath))
                    _selectedRouteFile = "";
            }

            if (IsRouteModeSelected() && string.IsNullOrWhiteSpace(_selectedRouteFile))
            {
                List<string> availableRouteFiles = GetAvailableRouteFiles();

                if (availableRouteFiles.Count > 0)
                    _selectedRouteFile = availableRouteFiles[0];
            }
        }

        private void LoadPersistedSettings()
        {
            try
            {
                string settingsPath = GetPersistedSettingsPath();

                if (!File.Exists(settingsPath))
                    return;

                var document = new XmlDocument();
                document.Load(settingsPath);

                XmlNode settings = document.SelectSingleNode("/ComponentSettings");

                if (settings?.Attributes?["Mode"] != null)
                    _selectedMode = settings.Attributes["Mode"].Value;

                if (settings?.Attributes?["RouteFile"] != null)
                    _selectedRouteFile = settings.Attributes["RouteFile"].Value;
            }
            catch
            {
                // Ignore unreadable persisted settings and fall back to defaults.
            }

            NormalizeSelections();
        }

        private void SavePersistedSettings()
        {
            try
            {
                Directory.CreateDirectory(_configRootFolder);
                NormalizeSelections();

                var document = new XmlDocument();
                XmlElement settings = document.CreateElement("ComponentSettings");
                settings.SetAttribute("Mode", _selectedMode ?? FullWingMode);
                settings.SetAttribute("RouteFile", _selectedRouteFile ?? "");
                document.AppendChild(settings);
                document.Save(GetPersistedSettingsPath());
            }
            catch
            {
                // Ignore persistence failures; LiveSplit layout settings still provide a fallback.
            }
        }

        private void LoadConfig()
        {
            NormalizeSelections();
            _splitsByMap.Clear();
            _splitIndexByMap.Clear();
            _routeSplits.Clear();
            _routeMode = false;
            _routeName = "";
            _currentRouteIndex = 0;
            _lastTriggeredName = null;
            _configuredRunComplete = false;

            try
            {
                string fullWingsFolder = Path.Combine(_configRootFolder, "fullwings");
                string encountersFolder = Path.Combine(_configRootFolder, "encounters");
                string routesFolder = Path.Combine(_configRootFolder, "routes");

                RouteConfigRoot route = null;

                if (IsRouteModeSelected() && !string.IsNullOrWhiteSpace(_selectedRouteFile))
                {
                    string selectedRoutePath = Path.Combine(routesFolder, _selectedRouteFile);
                    route = SplitConfigLoader.LoadRoute(selectedRoutePath);
                }

                if (IsRouteModeSelected() && route != null)
                {
                    _routeMode = true;
                    _routeName = route.Name ?? "Route";
                    Dictionary<string, EncounterConfigRoot> encounters = SplitConfigLoader.LoadEncounters(encountersFolder);

                    foreach (string encounterId in route.Encounters)
                    {
                        if (!encounters.TryGetValue(encounterId, out EncounterConfigRoot encounter))
                            continue;

                        foreach (SplitConfig splitConfig in encounter.Splits)
                        {
                            ITrigger trigger = BuildSplitTrigger(splitConfig, encounter.MapId);

                            if (trigger == null)
                                continue;

                            string splitName = string.IsNullOrWhiteSpace(splitConfig.Name)
                                ? encounter.Name
                                : splitConfig.Name;

                            _routeSplits.Add(new ConfiguredSplit(splitName, trigger));
                        }
                    }

                    _configStatus = $"Route mode: {_routeName} ({_selectedRouteFile}) | Splits={_routeSplits.Count}";
                }
                else if (IsRouteModeSelected())
                {
                    _configStatus = $"Route mode: could not load route '{_selectedRouteFile}'";
                }
                else
                {
                    Dictionary<uint, FullWingConfigRoot> configs = SplitConfigLoader.LoadFullWings(fullWingsFolder);

                    foreach (KeyValuePair<uint, FullWingConfigRoot> pair in configs)
                    {
                        uint mapId = pair.Key;
                        FullWingConfigRoot root = pair.Value;

                        List<ConfiguredSplit> splits = new List<ConfiguredSplit>();

                        foreach (SplitConfig splitConfig in root.Splits)
                        {
                            ITrigger trigger = BuildSplitTrigger(splitConfig, mapId);

                            if (trigger != null)
                            {
                                splits.Add(new ConfiguredSplit(splitConfig.Name, trigger));
                            }
                        }

                        _splitsByMap[mapId] = splits;
                        _splitIndexByMap[mapId] = 0;
                    }

                    _configStatus = $"Wing mode: loaded {_splitsByMap.Count} map configs";
                }

                _knownConfigFiles = GetConfigFileSnapshot();
            }
            catch (Exception ex)
            {
                _configStatus = $"Config error: {ex.Message}";
            }

            RefreshSettingsUi();
        }

        private ITrigger BuildTrigger(TriggerConfig config, uint defaultMapId)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.Type))
                return null;

            string type = config.Type.Trim().ToLowerInvariant();
            uint resolvedMapId = config.MapId ?? defaultMapId;

            if (type == "circle")
            {
                if (!config.X.HasValue || !config.Z.HasValue || !config.Radius.HasValue)
                    return null;

                return new PositionTrigger(
                    resolvedMapId,
                    config.X.Value,
                    config.Z.Value,
                    config.Radius.Value,
                    config.CombatState
                );
            }

            if (type == "sphere")
            {
                if (!config.X.HasValue || !config.Y.HasValue || !config.Z.HasValue || !config.Radius.HasValue)
                    return null;

                return new SphereTrigger(
                    resolvedMapId,
                    config.X.Value,
                    config.Y.Value,
                    config.Z.Value,
                    config.Radius.Value,
                    config.CombatState
                );
            }

            if (type == "polygon")
            {
                if (config.Points == null || config.Points.Count < 3)
                    return null;

                var points = new List<PolygonPoint>();

                foreach (TriggerPointConfig point in config.Points)
                {
                    if (!point.X.HasValue || !point.Z.HasValue)
                        return null;

                    points.Add(new PolygonPoint(point.X.Value, point.Z.Value));
                }

                return new PolygonTrigger(resolvedMapId, points, config.CombatState);
            }

            if (type == "map_not")
            {
                return new MapNotTrigger(resolvedMapId);
            }

            if (type == "map")
            {
                return new MapTrigger(resolvedMapId);
            }

            if (type == "bossdeath")
            {
                if (string.IsNullOrWhiteSpace(config.Boss))
                    return null;

                return new BossDeathTrigger(config.Boss);
            }

            return null;
        }

        private ITrigger BuildConfiguredTrigger(TriggerConfig config, uint defaultMapId)
        {
            ITrigger trigger = BuildTrigger(config, defaultMapId);

            if (trigger == null)
                return null;

            if (!string.IsNullOrWhiteSpace(config.Name))
                return new NamedTrigger(trigger, config.Name);

            return trigger;
        }

        private ITrigger BuildSplitTrigger(SplitConfig splitConfig, uint defaultMapId)
        {
            if (splitConfig == null)
                return null;

            var triggers = new List<ITrigger>();

            ITrigger primaryTrigger = BuildConfiguredTrigger(splitConfig.Trigger, defaultMapId);
            if (primaryTrigger != null)
                triggers.Add(primaryTrigger);

            if (splitConfig.OrTrigger != null)
            {
                foreach (TriggerConfig alternativeConfig in splitConfig.OrTrigger)
                {
                    ITrigger alternativeTrigger = BuildConfiguredTrigger(alternativeConfig, defaultMapId);
                    if (alternativeTrigger != null)
                        triggers.Add(alternativeTrigger);
                }
            }

            if (triggers.Count == 0)
                return null;

            if (triggers.Count == 1)
                return triggers[0];

            return new AnyTrigger(triggers);
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (!_reader.TryRead(out var data))
            {
                _debugText = BuildDisconnectedDebugText();
                RefreshSettingsUi();
                return;
            }

            CheckForConfigReload();

            bool inCombat = (data.context.uiState & 0x40) != 0;
            uint mapId = data.context.mapId;
            uint instance = data.context.instance;
            uint uiTick = data.link.uiTick;

            uint previousMapId = _lastMapId;

            HandleTickState(uiTick);
            HandleMapState(mapId, instance);

            float playerX = data.link.fAvatarPosition[0];
            float playerY = data.link.fAvatarPosition[1];
            float playerZ = data.link.fAvatarPosition[2];

            HandleManualReset();
            HandleManualStart();

            if (_configuredRunComplete)
            {
                PauseTimerIfConfiguredRunComplete();
                _debugText = BuildLiveDebugText(mapId, instance, playerX, playerY, playerZ, inCombat, uiTick);
                RefreshSettingsUi();
                return;
            }

            if (_routeMode)
                HandleRouteSplitProgress(previousMapId, mapId, playerX, playerY, playerZ, inCombat);
            else
                HandleWingSplitProgress(previousMapId, mapId, playerX, playerY, playerZ, inCombat);

            _debugText = BuildLiveDebugText(mapId, instance, playerX, playerY, playerZ, inCombat, uiTick);
            RefreshSettingsUi();
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
            if (_timer.CurrentState.CurrentPhase == TimerPhase.NotRunning && _runStarted)
            {
                _runStarted = false;
                _configuredRunComplete = false;
                _lastSplitTime = DateTime.MinValue;
                _currentRouteIndex = 0;
                ResetAllSplits();
                ResetWingSplitIndices();
            }
        }

        private void HandleManualStart()
        {
            if (_timer.CurrentState.CurrentPhase == TimerPhase.Running && !_runStarted)
            {
                _runStarted = true;
                _configuredRunComplete = false;
                _lastSplitTime = DateTime.UtcNow;
                ResetAllSplits();
            }
        }

        private void HandleWingSplitProgress(uint previousMapId, uint mapId, float playerX, float playerY, float playerZ, bool inCombat)
        {
            if (!_runStarted)
                return;

            if (_timer.CurrentState.CurrentPhase != TimerPhase.Running)
                return;

            if (!_splitsByMap.ContainsKey(mapId))
                return;

            List<ConfiguredSplit> splits = _splitsByMap[mapId];
            int index = _splitIndexByMap[mapId];

            if (index >= splits.Count)
                return;

            if (TryTriggerSplit(splits[index], previousMapId, mapId, playerX, playerY, playerZ, inCombat))
            {
                _splitIndexByMap[mapId]++;
                PauseTimerIfConfiguredRunComplete();
            }
        }

        private void HandleRouteSplitProgress(uint previousMapId, uint mapId, float playerX, float playerY, float playerZ, bool inCombat)
        {
            if (!_runStarted)
                return;

            if (_timer.CurrentState.CurrentPhase != TimerPhase.Running)
                return;

            if (_currentRouteIndex >= _routeSplits.Count)
                return;

            if (TryTriggerSplit(_routeSplits[_currentRouteIndex], previousMapId, mapId, playerX, playerY, playerZ, inCombat))
            {
                _currentRouteIndex++;
                PauseTimerIfConfiguredRunComplete();
            }
        }

        private void ResetAllSplits()
        {
            _lastTriggeredName = null;

            foreach (KeyValuePair<uint, List<ConfiguredSplit>> pair in _splitsByMap)
            {
                foreach (ConfiguredSplit split in pair.Value)
                    split.Trigger?.Reset();
            }

            foreach (ConfiguredSplit split in _routeSplits)
                split.Trigger?.Reset();
        }

        private bool TryTriggerSplit(ConfiguredSplit split, uint previousMapId, uint mapId, float playerX, float playerY, float playerZ, bool inCombat)
        {
            if (split?.Trigger == null)
                return false;

            if (_inTransition && !split.Trigger.CanTriggerDuringTransition)
                return false;

            if ((DateTime.UtcNow - _lastSplitTime).TotalMilliseconds < SplitCooldownMs)
                return false;

            if (!split.Trigger.IsTriggered(previousMapId, mapId, playerX, playerY, playerZ, inCombat, _inTransition, _lastTriggeredName))
                return false;

            _timer.Split();
            _lastSplitTime = DateTime.UtcNow;
            _lastTriggeredName = split.Trigger.LastTriggeredName;
            return true;
        }

        private void ResetWingSplitIndices()
        {
            List<uint> keys = new List<uint>(_splitIndexByMap.Keys);
            foreach (uint key in keys)
                _splitIndexByMap[key] = 0;
        }

        private void PauseTimerIfConfiguredRunComplete()
        {
            if (!AreAllConfiguredSplitsConsumed())
                return;

            _configuredRunComplete = true;

            if (_timer.CurrentState.CurrentPhase == TimerPhase.Running)
                _timer.Pause();
        }

        private bool AreAllConfiguredSplitsConsumed()
        {
            if (_routeMode)
                return _routeSplits.Count > 0 && _currentRouteIndex >= _routeSplits.Count;

            if (_splitsByMap.Count == 0)
                return false;

            foreach (KeyValuePair<uint, List<ConfiguredSplit>> pair in _splitsByMap)
            {
                int index = _splitIndexByMap.TryGetValue(pair.Key, out int currentIndex) ? currentIndex : 0;
                if (index < pair.Value.Count)
                    return false;
            }

            return true;
        }

        private bool IsRouteModeSelected()
        {
            return string.Equals(_selectedMode, RouteMode, StringComparison.OrdinalIgnoreCase);
        }

        private string GetModeText()
        {
            return _routeMode ? $"Route: {_routeName}" : "Full Wing";
        }

        private string GetCurrentSplitName(uint mapId)
        {
            if (_routeMode)
            {
                if (_currentRouteIndex < _routeSplits.Count)
                    return _routeSplits[_currentRouteIndex].Name;

                return "None";
            }

            if (!_splitsByMap.TryGetValue(mapId, out List<ConfiguredSplit> splits))
                return "None";

            int currentIndex = _splitIndexByMap.TryGetValue(mapId, out int index) ? index : 0;
            if (currentIndex >= splits.Count)
                return "None";

            return splits[currentIndex].Name;
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
            settings.SetAttribute("Mode", _selectedMode ?? FullWingMode);
            settings.SetAttribute("RouteFile", _selectedRouteFile ?? "");
            return settings;
        }

        private List<string> GetAvailableRouteFiles()
        {
            var routes = new List<string>();

            string routesFolder = Path.Combine(_configRootFolder, "routes");

            if (!Directory.Exists(routesFolder))
                return routes;

            foreach (string file in Directory.GetFiles(routesFolder, "*.json"))
            {
                routes.Add(Path.GetFileName(file));
            }

            routes.Sort(StringComparer.OrdinalIgnoreCase);
            return routes;
        }

        public override Control GetSettingsControl(LayoutMode mode)
        {
            var panel = new TableLayoutPanel();
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.ColumnCount = 1;
            panel.RowCount = 4;
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            panel.Padding = new Padding(10);
            panel.Margin = new Padding(0);
            panel.Dock = DockStyle.Fill;
            _settingsPanel = panel;

            var titleLabel = new Label();
            titleLabel.AutoSize = true;
            titleLabel.Margin = new Padding(0, 0, 0, 4);
            titleLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            titleLabel.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
            titleLabel.Text = "GW2 Auto Splitter";

            var subtitleLabel = new Label();
            subtitleLabel.AutoSize = true;
            subtitleLabel.Margin = new Padding(0, 0, 0, 12);
            subtitleLabel.MaximumSize = new Size(SettingsSubtitleWrapWidth, 0);
            subtitleLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            subtitleLabel.Text =
                "Choose a split mode and review live state below. " +
                "FullWing is for normal wing clears, Route is for custom encounter chains.";
            _settingsSubtitleLabel = subtitleLabel;

            var configGroup = new GroupBox();
            configGroup.AutoSize = true;
            configGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            configGroup.Dock = DockStyle.Top;
            configGroup.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            configGroup.Margin = new Padding(0, 0, 0, 10);
            configGroup.Padding = new Padding(10);
            configGroup.Text = "Configuration";

            var configLayout = new TableLayoutPanel();
            configLayout.AutoSize = true;
            configLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            configLayout.ColumnCount = 2;
            configLayout.RowCount = 2;
            configLayout.Dock = DockStyle.Top;
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var modeLabel = CreateFieldCaption("Mode");
            var routeLabel = CreateFieldCaption("Route file");

            var modeComboBox = new ComboBox();
            modeComboBox.Width = SettingsComboBoxWidth;
            modeComboBox.Anchor = AnchorStyles.Left;
            modeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            modeComboBox.Items.Add(FullWingMode);
            modeComboBox.Items.Add(RouteMode);
            modeComboBox.SelectedItem = _selectedMode ?? FullWingMode;

            var routeComboBox = new ComboBox();
            routeComboBox.Width = SettingsComboBoxWidth;
            routeComboBox.Anchor = AnchorStyles.Left;
            routeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

            List<string> routeFiles = GetAvailableRouteFiles();
            foreach (string routeFile in routeFiles)
                routeComboBox.Items.Add(routeFile);

            if (!string.IsNullOrWhiteSpace(_selectedRouteFile) && routeComboBox.Items.Contains(_selectedRouteFile))
                routeComboBox.SelectedItem = _selectedRouteFile;
            else if (routeComboBox.Items.Count > 0)
                routeComboBox.SelectedIndex = 0;

            routeComboBox.Enabled = IsRouteModeSelected();

            configLayout.Controls.Add(modeLabel, 0, 0);
            configLayout.Controls.Add(modeComboBox, 1, 0);
            configLayout.Controls.Add(routeLabel, 0, 1);
            configLayout.Controls.Add(routeComboBox, 1, 1);
            configGroup.Controls.Add(configLayout);

            var statusGroup = new GroupBox();
            statusGroup.AutoSize = true;
            statusGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            statusGroup.Dock = DockStyle.Top;
            statusGroup.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            statusGroup.Padding = new Padding(10);
            statusGroup.Text = "Live Status";

            var statusLayout = new TableLayoutPanel();
            statusLayout.AutoSize = true;
            statusLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            statusLayout.ColumnCount = 2;
            statusLayout.RowCount = 3;
            statusLayout.Dock = DockStyle.Top;
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _statusLayout = statusLayout;

            _configRootValueLabel = CreateFieldValueLabel(SettingsValueWrapWidth);
            _configStatusValueLabel = CreateFieldValueLabel(SettingsValueWrapWidth);
            _liveDebugValueLabel = CreateFieldValueLabel(SettingsValueWrapWidth, monospace: true);

            statusLayout.Controls.Add(CreateFieldCaption("Config root"), 0, 0);
            statusLayout.Controls.Add(_configRootValueLabel, 1, 0);
            statusLayout.Controls.Add(CreateFieldCaption("Config status"), 0, 1);
            statusLayout.Controls.Add(_configStatusValueLabel, 1, 1);
            statusLayout.Controls.Add(CreateFieldCaption("Debug"), 0, 2);
            statusLayout.Controls.Add(_liveDebugValueLabel, 1, 2);
            statusGroup.Controls.Add(statusLayout);

            modeComboBox.SelectedIndexChanged += (sender, e) =>
            {
                string newMode = modeComboBox.SelectedItem?.ToString() ?? FullWingMode;
                _selectedMode = newMode;

                routeComboBox.Enabled = IsRouteModeSelected();

                if (IsRouteModeSelected() && routeComboBox.SelectedItem != null)
                    _selectedRouteFile = routeComboBox.SelectedItem.ToString();

                SavePersistedSettings();
                LoadConfig();
            };

            routeComboBox.SelectedIndexChanged += (sender, e) =>
            {
                if (!IsRouteModeSelected())
                    return;

                _selectedRouteFile = routeComboBox.SelectedItem?.ToString() ?? "";
                SavePersistedSettings();
                LoadConfig();
            };

            panel.Controls.Add(titleLabel, 0, 0);
            panel.Controls.Add(subtitleLabel, 0, 1);
            panel.Controls.Add(configGroup, 0, 2);
            panel.Controls.Add(statusGroup, 0, 3);

            panel.Layout += (sender, e) => RefreshSettingsLayout();
            RefreshSettingsUi();
            RefreshSettingsLayout();

            return panel;
        }

        public override void SetSettings(XmlNode settings)
        {
            if (settings?.Attributes?["Mode"] != null)
                _selectedMode = settings.Attributes["Mode"].Value;

            if (settings?.Attributes?["RouteFile"] != null)
                _selectedRouteFile = settings.Attributes["RouteFile"].Value;

            SavePersistedSettings();
            LoadConfig();
        }

        private void RefreshSettingsUi()
        {
            UpdateLabel(_configRootValueLabel, _configRootFolder);
            UpdateLabel(_configStatusValueLabel, _configStatus);
            UpdateLabel(_liveDebugValueLabel, _debugText);
        }

        private void RefreshSettingsLayout()
        {
            if (_settingsPanel != null && !_settingsPanel.IsDisposed && _settingsSubtitleLabel != null && !_settingsSubtitleLabel.IsDisposed)
            {
                int subtitleWidth = Math.Max(SettingsSubtitleWrapWidth, _settingsPanel.DisplayRectangle.Width - 4);
                _settingsSubtitleLabel.MaximumSize = new Size(subtitleWidth, 0);
            }

            if (_statusLayout == null || _statusLayout.IsDisposed)
                return;

            int[] widths = _statusLayout.GetColumnWidths();
            if (widths.Length < 2 || widths[1] <= 0)
                return;

            int valueWidth = Math.Max(SettingsValueWrapWidth, widths[1] - 6);
            UpdateLabelWrapWidth(_configRootValueLabel, valueWidth);
            UpdateLabelWrapWidth(_configStatusValueLabel, valueWidth);
            UpdateLabelWrapWidth(_liveDebugValueLabel, valueWidth);
        }

        private string BuildDisconnectedDebugText()
        {
            return
                "Reader: Waiting for GW2 / MumbleLink" + Environment.NewLine +
                $"Timer: {_timer.CurrentState.CurrentPhase}" + Environment.NewLine +
                $"Run: {GetRunStateText()}" + Environment.NewLine +
                $"Mode: {GetModeText()}";
        }

        private string BuildLiveDebugText(uint mapId, uint instance, float playerX, float playerY, float playerZ, bool inCombat, uint uiTick)
        {
            return
                "Reader: Connected" + Environment.NewLine +
                $"Timer: {_timer.CurrentState.CurrentPhase}" + Environment.NewLine +
                $"Run: {GetRunStateText()}" + Environment.NewLine +
                $"Mode: {GetModeText()}" + Environment.NewLine +
                $"Map ID: {mapId}" + Environment.NewLine +
                GetInstanceDebugLine(instance) +
                $"Position: X={playerX:0.0}  Y={playerY:0.0}  Z={playerZ:0.0}" + Environment.NewLine +
                $"Combat: {(inCombat ? "Yes" : "No")}  Loading: {(_inTransition ? "Yes" : "No")}" + Environment.NewLine +
                $"Next split: {GetCurrentSplitName(mapId)}" + Environment.NewLine +
                $"Cooldown: {GetSplitCooldownText()}" + Environment.NewLine +
                $"Tick: {uiTick}";
        }

        private static string GetInstanceDebugLine(uint instance)
        {
            return instance == 0
                ? string.Empty
                : $"Instance ID: {instance}" + Environment.NewLine;
        }

        private string GetRunStateText()
        {
            if (_configuredRunComplete || _timer.CurrentState.CurrentPhase == TimerPhase.Ended)
                return "Completed";

            return _runStarted ? "Tracking" : "Idle";
        }

        private string GetSplitCooldownText()
        {
            if (_lastSplitTime == DateTime.MinValue)
                return "Ready";

            double remainingMs = SplitCooldownMs - (DateTime.UtcNow - _lastSplitTime).TotalMilliseconds;
            return remainingMs > 0 ? $"{remainingMs:0} ms" : "Ready";
        }

        private static Label CreateFieldCaption(string text)
        {
            return new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, 6, 12, 6),
                Text = text + ":"
            };
        }

        private static Label CreateFieldValueLabel(int maxWidth, bool monospace = false)
        {
            return new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, 6, 0, 6),
                MaximumSize = new Size(maxWidth, 0),
                Font = monospace
                    ? new Font("Consolas", 9f, FontStyle.Regular)
                    : new Font("Segoe UI", 9f, FontStyle.Regular)
            };
        }

        private static void UpdateLabel(Label label, string text)
        {
            if (label == null || label.IsDisposed)
                return;

            label.Text = text ?? string.Empty;
        }

        private static void UpdateLabelWrapWidth(Label label, int maxWidth)
        {
            if (label == null || label.IsDisposed)
                return;

            label.MaximumSize = new Size(maxWidth, 0);
        }

        public override void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        {
        }

        private sealed class ConfiguredSplit
        {
            public string Name { get; }
            public ITrigger Trigger { get; }

            public ConfiguredSplit(string name, ITrigger trigger)
            {
                Name = name;
                Trigger = trigger;
            }
        }

        private interface ITrigger
        {
            bool CanTriggerDuringTransition { get; }
            string LastTriggeredName { get; }
            bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerY, float playerZ, bool inCombat, bool inTransition, string previousTriggerName);
            void Reset();
        }

        private sealed class PositionTrigger : ITrigger
        {
            public uint MapId { get; }
            public float X { get; }
            public float Z { get; }
            public float Radius { get; }
            public string CombatState { get; }

            public PositionTrigger(uint mapId, float x, float z, float radius, string combatState)
            {
                MapId = mapId;
                X = x;
                Z = z;
                Radius = radius;
                CombatState = combatState;
            }

            public bool CanTriggerDuringTransition => false;

            public string LastTriggeredName => null;

            public bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerY, float playerZ, bool inCombat, bool inTransition, string previousTriggerName)
            {
                if (mapId != MapId)
                    return false;

                if (CombatState == CombatStateInCombat && !inCombat)
                    return false;

                if (CombatState == CombatStateOutOfCombat && inCombat)
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

        private sealed class SphereTrigger : ITrigger
        {
            public uint MapId { get; }
            public float X { get; }
            public float Y { get; }
            public float Z { get; }
            public float Radius { get; }
            public string CombatState { get; }

            public SphereTrigger(uint mapId, float x, float y, float z, float radius, string combatState)
            {
                MapId = mapId;
                X = x;
                Y = y;
                Z = z;
                Radius = radius;
                CombatState = combatState;
            }

            public bool CanTriggerDuringTransition => false;

            public string LastTriggeredName => null;

            public bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerY, float playerZ, bool inCombat, bool inTransition, string previousTriggerName)
            {
                if (mapId != MapId)
                    return false;

                if (CombatState == CombatStateInCombat && !inCombat)
                    return false;

                if (CombatState == CombatStateOutOfCombat && inCombat)
                    return false;

                float dx = playerX - X;
                float dy = playerY - Y;
                float dz = playerZ - Z;
                float distanceSquared = dx * dx + dy * dy + dz * dz;
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
            public string CombatState { get; }

            public PolygonTrigger(uint mapId, List<PolygonPoint> points, string combatState)
            {
                MapId = mapId;
                Points = points;
                CombatState = combatState;
            }

            public bool CanTriggerDuringTransition => false;

            public string LastTriggeredName => null;

            public bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerY, float playerZ, bool inCombat, bool inTransition, string previousTriggerName)
            {
                if (mapId != MapId)
                    return false;

                if (CombatState == CombatStateInCombat && !inCombat)
                    return false;

                if (CombatState == CombatStateOutOfCombat && inCombat)
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

            public bool CanTriggerDuringTransition => true;

            public string LastTriggeredName => null;

            public bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerY, float playerZ, bool inCombat, bool inTransition, string previousTriggerName)
            {
                return previousMapId != mapId && mapId == TargetMapId;
            }

            public void Reset()
            {
            }
        }

        private sealed class MapNotTrigger : ITrigger
        {
            public uint TargetMapId { get; }

            public MapNotTrigger(uint targetMapId)
            {
                TargetMapId = targetMapId;
            }

            public bool CanTriggerDuringTransition => true;

            public string LastTriggeredName => null;

            public bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerY, float playerZ, bool inCombat, bool inTransition, string previousTriggerName)
            {
                return mapId != TargetMapId;
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

            public bool CanTriggerDuringTransition => false;

            public string LastTriggeredName => null;

            public bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerY, float playerZ, bool inCombat, bool inTransition, string previousTriggerName)
            {
                return false;
            }

            public void Reset()
            {
            }
        }

        private sealed class AnyTrigger : ITrigger
        {
            private readonly List<ITrigger> _triggers;
            private string _lastTriggeredName;

            public AnyTrigger(List<ITrigger> triggers)
            {
                _triggers = triggers ?? new List<ITrigger>();
            }

            public bool CanTriggerDuringTransition
            {
                get
                {
                    foreach (ITrigger trigger in _triggers)
                    {
                        if (trigger != null && trigger.CanTriggerDuringTransition)
                            return true;
                    }

                    return false;
                }
            }

            public string LastTriggeredName => _lastTriggeredName;

            public bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerY, float playerZ, bool inCombat, bool inTransition, string previousTriggerName)
            {
                _lastTriggeredName = null;

                foreach (ITrigger trigger in _triggers)
                {
                    if (trigger == null)
                        continue;

                    if (inTransition && !trigger.CanTriggerDuringTransition)
                        continue;

                    if (trigger.IsTriggered(previousMapId, mapId, playerX, playerY, playerZ, inCombat, inTransition, previousTriggerName))
                    {
                        _lastTriggeredName = trigger.LastTriggeredName;
                        return true;
                    }
                }

                return false;
            }

            public void Reset()
            {
                _lastTriggeredName = null;

                foreach (ITrigger trigger in _triggers)
                    trigger?.Reset();
            }
        }

        private sealed class NamedTrigger : ITrigger
        {
            private readonly ITrigger _innerTrigger;
            private readonly string _name;
            private string _lastTriggeredName;

            public NamedTrigger(ITrigger innerTrigger, string name)
            {
                _innerTrigger = innerTrigger;
                _name = name;
            }

            public bool CanTriggerDuringTransition => _innerTrigger != null && _innerTrigger.CanTriggerDuringTransition;

            public string LastTriggeredName => _lastTriggeredName;

            public bool IsTriggered(uint previousMapId, uint mapId, float playerX, float playerY, float playerZ, bool inCombat, bool inTransition, string previousTriggerName)
            {
                _lastTriggeredName = null;

                if (_innerTrigger == null)
                    return false;

                if (!string.IsNullOrWhiteSpace(_name) &&
                    string.Equals(previousTriggerName, _name, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!_innerTrigger.IsTriggered(previousMapId, mapId, playerX, playerY, playerZ, inCombat, inTransition, previousTriggerName))
                    return false;

                _lastTriggeredName = !string.IsNullOrWhiteSpace(_name)
                    ? _name
                    : _innerTrigger.LastTriggeredName;

                return true;
            }

            public void Reset()
            {
                _lastTriggeredName = null;
                _innerTrigger?.Reset();
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
