using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace SonicPickupFilters
{
	[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
	[BepInDependency(Jotunn.Main.ModGuid)]
	[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
	public sealed class SonicPickupFiltersPlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "com.sonicdm.valheim.sonicpickupfilters";

		public const string PluginName = "Sonic Pickup Filters";

		public const string PluginVersion = "0.1.1";

		internal const string HotkeyButtonName = "SonicPickupFilters_Toggle";

		internal static ManualLogSource ModLogger;

		internal static ConfigEntry<bool> Enabled;

		internal static ConfigEntry<bool> DebugLogging;

		internal static ConfigEntry<bool> DefaultCapUsesMaxStack;

		internal static ConfigEntry<string> Rules;

		internal static ConfigEntry<KeyCode> ToggleKey;

		internal static ConfigEntry<KeyboardShortcut> InventoryBlockModifier;

		internal static ConfigEntry<bool> AutoReloadConfig;

		internal static bool DebugEnabled => DebugLogging != null && DebugLogging.Value;

		private Harmony _harmony;

		private ButtonConfig _toggleButton;

		private bool _subscribedGui;

		private static SonicPickupFiltersPlugin _instance;

		private DateTime _configLastWriteUtc;

		private DateTime _configPendingWriteUtc;

		private float _configPendingSince = -1f;

		private float _configWatchNextCheck;

		/// <summary>Ignore cfg mtime bumps caused by our own SetRule/Save writes.</summary>
		private float _selfWriteGraceUntil = -1f;

		private void Awake()
		{
			_instance = this;
			ModLogger = Logger;

			Enabled = Config.Bind("General", "Enabled", true, "Enable Sonic Pickup Filters auto-pickup rules. UI/hotkey still work when off so you can configure.");
			DebugLogging = Config.Bind(
				"General",
				"DebugLogging",
				false,
				"Write diagnostic [DEBUG] lines to BepInEx LogOutput.log. Leave off in normal play — spam in loot piles is very expensive.");
			AutoReloadConfig = Config.Bind(
				"General",
				"AutoReloadConfig",
				true,
				"Reload this .cfg from disk while the game is running (after a short debounce). Lets you flip Enabled / keys / rules without restarting.");

			// Earlier 0.1.x builds forced DebugLogging=true into the persisted cfg.
			ConfigEntry<bool> debugMigrated = Config.Bind(
				"General",
				"DebugLoggingMigratedOff",
				false,
				"Internal: one-shot clear of forced DebugLogging from early 0.1.x builds.");
			if (!debugMigrated.Value)
			{
				DebugLogging.Value = false;
				debugMigrated.Value = true;
			}

			DefaultCapUsesMaxStack = Config.Bind(
				"Rules",
				"DefaultCapUsesMaxStack",
				true,
				"When Cap is turned on (Cap checkbox), fill the max with the item's max stack size. If false, default to 50.");
			Rules = Config.Bind(
				"Rules",
				"ItemRules",
				"",
				"Comma-separated prefab rules: Prefab:0 = Block, Prefab:N = Cap at N. Missing = Allow. Example: BlackMetalScrap:0,Wood:50");
			ToggleKey = Config.Bind(
				"UI",
				"ToggleKey",
				KeyCode.L,
				"Hotkey to open/close the Pickup Filter panel.");
			InventoryBlockModifier = Config.Bind(
				"UI",
				"InventoryBlockModifier",
				new KeyboardShortcut(KeyCode.LeftAlt),
				"Hold this modifier and right-click an item in your inventory to toggle Block (never auto-pick).");

			ModLocalization.Register();
			PickupRuleStore.Init(Rules);

			Rules.SettingChanged += (_, __) => PickupRuleStore.ReloadFromConfig();
			ToggleKey.SettingChanged += (_, __) => SyncToggleButtonKey();
			Enabled.SettingChanged += (_, __) => OnEnabledSettingChanged();

			_configLastWriteUtc = GetConfigWriteTimeUtc();

			Logger.LogInfo(
				PluginName + " " + PluginVersion + " Awake. Enabled=" + Enabled.Value +
				" DebugLogging=" + DebugLogging.Value +
				" ItemRules='" + (Rules.Value ?? string.Empty) + "'");

			// Drop any leftover panel if the plugin is soft-reloaded mid-session.
			PickupFilterPanel.DestroyPanel();

			if (GUIManager.IsHeadless())
			{
				Logger.LogInfo(PluginName + " " + PluginVersion + " skipped GUI/patches on headless server.");
				return;
			}

			_toggleButton = new ButtonConfig
			{
				Name = HotkeyButtonName,
				Key = ToggleKey != null ? ToggleKey.Value : KeyCode.L,
				ActiveInGUI = true,
				ActiveInCustomGUI = true
			};
			InputManager.Instance.AddButton(PluginGuid, _toggleButton);

			GUIManager.OnCustomGUIAvailable += OnCustomGuiAvailable;
			_subscribedGui = true;

			_harmony = new Harmony(PluginGuid);
			try
			{
				_harmony.PatchAll();
				Logger.LogInfo(PluginName + " " + PluginVersion + " loaded (Harmony ok).");
				DebugLog("DebugLog path OK — open panel / cycle category to see more [DEBUG] lines.");
			}
			catch (Exception ex)
			{
				try
				{
					_harmony.UnpatchSelf();
				}
				catch
				{
				}

				Enabled.Value = false;
				Logger.LogError(PluginName + " failed to apply Harmony patches and has been disabled.");
				Logger.LogError(ex);
			}

			TryRegisterCommands();

			// ScriptEngine mid-session reload: CustomGUI + InventoryGui already exist — event/Awake won't re-fire.
			TryCatchUpUi();
		}

		private void TryCatchUpUi()
		{
			try
			{
				if (GUIManager.Instance != null && GUIManager.CustomGUIFront != null)
				{
					OnCustomGuiAvailable();
				}

				InventoryBarButton.TryInjectExisting();
			}
			catch (Exception ex)
			{
				Logger.LogWarning("TryCatchUpUi failed: " + ex.Message);
			}
		}

		private void OnDestroy()
		{
			if (_instance == this)
			{
				_instance = null;
			}

			// Required for BepInEx ScriptEngine F6 reload — must unpatch + drop UI or hooks stack.
			if (_subscribedGui)
			{
				GUIManager.OnCustomGUIAvailable -= OnCustomGuiAvailable;
				_subscribedGui = false;
			}

			try
			{
				AutoPickupPatches.ResetRuntimeState();
			}
			catch
			{
			}

			PickupFilterPanel.DestroyPanel();
			InventoryBarButton.DestroyInjected();

			if (_harmony != null)
			{
				try
				{
					_harmony.UnpatchSelf();
				}
				catch (Exception ex)
				{
					Logger.LogWarning("UnpatchSelf on destroy: " + ex.Message);
				}

				_harmony = null;
			}

			Logger.LogInfo(PluginName + " unloaded (ScriptEngine-safe).");
		}

		private static bool _commandRegistered;

		private void TryRegisterCommands()
		{
			if (_commandRegistered)
			{
				return;
			}

			try
			{
				new Terminal.ConsoleCommand(
					"spf_dump",
					"Sonic Pickup Filters: dump Cap/Block rules + nearby auto-pickup candidates to console and log",
					args =>
					{
						string dump = AutoPickupPatches.DumpNearby(Player.m_localPlayer);
						foreach (string line in dump.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
						{
							args.Context.AddString(line);
						}

						ModLogger?.LogInfo(dump);
					},
					false, false, false, false, false, false, null, false, false, true);

				_commandRegistered = true;
				Logger.LogInfo("Registered command: spf_dump");
			}
			catch (Exception ex)
			{
				Logger.LogWarning("spf_dump register failed: " + ex.Message);
			}
		}

		private void OnCustomGuiAvailable()
		{
			PickupFilterPanel.OnGuiReady();
		}

		private void Update()
		{
			if (GUIManager.IsHeadless())
			{
				return;
			}

			WatchConfigFileForChanges();
			PickupFilterPanel.ProcessQueuedRefresh();

			if (WasTogglePressed())
			{
				PickupFilterPanel.TogglePanel();
			}
		}

		private bool WasTogglePressed()
		{
			if (Console.IsVisible())
			{
				return false;
			}

			if (Chat.instance != null && Chat.instance.HasFocus())
			{
				return false;
			}

			// Text fields steal keys — don't toggle while typing a Cap/search value.
			if (UnityEngine.EventSystems.EventSystem.current != null)
			{
				GameObject selected = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
				if (selected != null &&
				    (selected.GetComponent<UnityEngine.UI.InputField>() != null ||
				     selected.GetComponent<TMPro.TMP_InputField>() != null))
				{
					return false;
				}
			}

			KeyCode key = ToggleKey != null ? ToggleKey.Value : KeyCode.L;
			if (key != KeyCode.None && Input.GetKeyDown(key))
			{
				return true;
			}

			return ZInput.instance != null && ZInput.GetButtonDown(HotkeyButtonName);
		}

		private void SyncToggleButtonKey()
		{
			if (_toggleButton == null || ToggleKey == null)
			{
				return;
			}

			_toggleButton.Key = ToggleKey.Value;
			DebugLog("ToggleKey synced to " + ToggleKey.Value);
		}

		private void OnEnabledSettingChanged()
		{
			Logger.LogInfo(PluginName + " Enabled=" + IsModEnabled() + " (live).");
			if (IsModEnabled())
			{
				InventoryBarButton.TryInjectExisting();
			}
		}

		/// <summary>
		/// Call after writing ItemRules (or other cfg values) from in-game UI so AutoReload
		/// does not rebuild the panel and jump the scroll to the top.
		/// </summary>
		internal static void AcknowledgeConfigWrite()
		{
			if (_instance == null)
			{
				return;
			}

			_instance._selfWriteGraceUntil = Time.unscaledTime + 2f;
			_instance._configLastWriteUtc = _instance.GetConfigWriteTimeUtc();
			_instance._configPendingSince = -1f;
		}

		private void WatchConfigFileForChanges()
		{
			if (AutoReloadConfig == null || !AutoReloadConfig.Value)
			{
				_configPendingSince = -1f;
				return;
			}

			float now = Time.unscaledTime;
			if (now < _configWatchNextCheck)
			{
				return;
			}

			_configWatchNextCheck = now + 1f;
			DateTime writeUtc = GetConfigWriteTimeUtc();
			if (writeUtc == DateTime.MinValue)
			{
				return;
			}

			if (writeUtc == _configLastWriteUtc)
			{
				_configPendingSince = -1f;
				return;
			}

			// In-game Cap/Block saves rewrite the same .cfg — absorb those without RefreshIfOpen.
			if (now < _selfWriteGraceUntil)
			{
				_configLastWriteUtc = writeUtc;
				_configPendingSince = -1f;
				return;
			}

			if (_configPendingSince < 0f || writeUtc != _configPendingWriteUtc)
			{
				_configPendingWriteUtc = writeUtc;
				_configPendingSince = now;
				return;
			}

			if (now - _configPendingSince < 0.75f)
			{
				return;
			}

			_configLastWriteUtc = writeUtc;
			_configPendingSince = -1f;
			ReloadConfigFromDisk();
		}

		private DateTime GetConfigWriteTimeUtc()
		{
			try
			{
				string path = Config.ConfigFilePath;
				if (string.IsNullOrEmpty(path) || !File.Exists(path))
				{
					return DateTime.MinValue;
				}

				return File.GetLastWriteTimeUtc(path);
			}
			catch
			{
				return DateTime.MinValue;
			}
		}

		private void ReloadConfigFromDisk()
		{
			try
			{
				Config.Reload();
				_configLastWriteUtc = GetConfigWriteTimeUtc();
				_configPendingSince = -1f;
				SyncToggleButtonKey();
				PickupRuleStore.ReloadFromConfig();
				PickupFilterPanel.RefreshIfOpen();
				if (IsModEnabled())
				{
					InventoryBarButton.TryInjectExisting();
				}

				Logger.LogInfo(PluginName + " config reloaded from disk.");
			}
			catch (Exception ex)
			{
				Logger.LogError(PluginName + " failed to reload config from disk.");
				Logger.LogError(ex);
			}
		}

		internal static bool IsModEnabled()
		{
			return Enabled == null || Enabled.Value;
		}

		internal static void DebugLog(string message)
		{
			if (DebugEnabled && ModLogger != null)
			{
				// LogInfo so it shows with default BepInEx log level (LogDebug is often filtered).
				ModLogger.LogInfo("[DEBUG] " + message);
			}
		}
	}
}
